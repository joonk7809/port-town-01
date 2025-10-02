using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Queue/Travel-aware planner with hysteresis and cooldowns.
    // Decides among: Work, Eat, Sleep, Leisure.
    public sealed class PlannerSystem : ISimSystem
    {
        public string Name => "Planner";

        private const int TICKS_PER_SEC = 20;

        // Need thresholds (0..100)
        private const float FOOD_TRIGGER  = 50f;
        private const float FOOD_CRITICAL = 25f;
        private const float REST_TRIGGER  = 40f;
        private const float REST_CRITICAL = 20f;

        // Utilities and costs
        private const float W_NEED = 0.8f;
        private const float C_TRAVEL_PER_SEC = 0.5f;
        private const float C_QUEUE_PER_SEC  = 0.6f;
        private const float C_FATIGUE        = 0.15f;

        private const float U_WORK_BASE    = 8.0f;
        private const float U_EAT_BASE     = 6.0f;
        private const float U_SLEEP_BASE   = 6.5f;
        private const float U_LEISURE_BASE = 1.0f;

        // Hysteresis and cooldowns
        private const float SWITCH_MARGIN = 2.0f;
        private const int   EAT_COOLDOWN_TICKS   = 60 * TICKS_PER_SEC;
        private const int   SLEEP_COOLDOWN_TICKS = 90 * TICKS_PER_SEC;

        private enum Intent { Work, Eat, Sleep, Leisure }

        private sealed class S
        {
            public Intent Last = Intent.Work;
            public int EatCooldownUntil = 0;
            public int SleepCooldownUntil = 0;
            // Cache last computed work target so switch block can use it
            public Vector3 LastWorkTarget;
        }

        private readonly Dictionary<int, S> _s = new Dictionary<int, S>();

        public void Tick(World world, int tick, float dt)
        {
            var stall  = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.Trading);
            var millWs = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.Milling);
            var dockWs = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.DockLoading);
            var forestWs = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.Logging);

            foreach (var a in world.Agents)
            {
                // Skip immobile infra actors
                if (a.SpeedMps <= 0f && !a.IsVendor && !a.IsEmployer) continue;

                if (!_s.TryGetValue(a.Id, out var st))
                    _s[a.Id] = st = new S();

                var phase = a.Phase;
                var pos   = a.Pos;
                float speed = Mathf.Max(0.05f, a.SpeedMps);

                float food = a.Food;
                float rest = a.Rest;

                // Candidate utilities
                float uWork = float.NegativeInfinity;
                float uEat  = float.NegativeInfinity;
                float uSleep= float.NegativeInfinity;
                float uLeis = U_LEISURE_BASE;

                // ------- EAT -------
                if (stall != null)
                {
                    bool crit   = food <= FOOD_CRITICAL;
                    bool hungry = food <= FOOD_TRIGGER;

                    if (hungry || crit)
                    {
                        float travelSec = TravelSeconds(pos, stall.StationPos, speed);
                        float queueSec  = EstimateFoodQueueWaitSec(world, stall, a);
                        float expectedRelief = Mathf.Min(30f, 100f - food);

                        bool onCooldown = tick < st.EatCooldownUntil;
                        if (!onCooldown || crit)
                        {
                            uEat = U_EAT_BASE
                                   + W_NEED * expectedRelief
                                   - C_TRAVEL_PER_SEC * travelSec
                                   - C_QUEUE_PER_SEC * queueSec;
                        }
                    }
                }

                // ------- SLEEP -------
                {
                    bool crit  = rest <= REST_CRITICAL;
                    bool tired = rest <= REST_TRIGGER || phase == DayPhase.Sleep;

                    if (tired || crit)
                    {
                        float travelSec = TravelSeconds(pos, a.HomePos, speed);
                        bool onCooldown = tick < st.SleepCooldownUntil;
                        if (!onCooldown || crit)
                        {
                            float expectedRelief = Mathf.Min(60f, 100f - rest);
                            float phaseBias = (phase == DayPhase.Sleep) ? 3.0f : 0.0f;

                            uSleep = U_SLEEP_BASE + phaseBias
                                     + W_NEED * expectedRelief
                                     - C_TRAVEL_PER_SEC * travelSec;
                        }
                    }
                }

                // ------- WORK (compute role-aware target ONCE) -------
                Vector3 workTarget = pos; // default
                {
                    switch (a.Role)
                    {
                        case JobRole.Logger:
                            if (forestWs != null) workTarget = forestWs.StationPos;
                            break;
                        case JobRole.Miller:
                            if (millWs != null) workTarget = millWs.StationPos;
                            break;
                        case JobRole.Hauler:
                            int crates = a.Carry.Get(ItemType.Crate);
                            if (crates > 0 && dockWs != null) workTarget = dockWs.StationPos;
                            else if (millWs != null) workTarget = millWs.StationPos;
                            break;
                        default:
                            workTarget = NearestOf(pos, forestWs?.StationPos, millWs?.StationPos, dockWs?.StationPos);
                            break;
                    }

                    float travelSec = TravelSeconds(pos, workTarget, speed);
                    float fatiguePenalty = C_FATIGUE * Mathf.Clamp01((100f - rest) / 100f);
                    float phaseBias = (phase == DayPhase.Work) ? 2.5f : 0.0f;

                    uWork = U_WORK_BASE + phaseBias - C_TRAVEL_PER_SEC * travelSec - fatiguePenalty;

                    st.LastWorkTarget = workTarget; // store for the switch below
                }

                // ------- Choose with hysteresis -------
                var bestIntent = Intent.Leisure;
                float bestU = uLeis;
                if (uWork  > bestU) { bestU = uWork;  bestIntent = Intent.Work; }
                if (uEat   > bestU) { bestU = uEat;   bestIntent = Intent.Eat; }
                if (uSleep > bestU) { bestU = uSleep; bestIntent = Intent.Sleep; }

                float uCurr = st.Last switch
                {
                    Intent.Work   => uWork,
                    Intent.Eat    => uEat,
                    Intent.Sleep  => uSleep,
                    _             => uLeis
                };

                bool critFood = food <= FOOD_CRITICAL;
                bool critRest = rest <= REST_CRITICAL;
                if (!critFood && !critRest)
                {
                    if (bestU < uCurr + SWITCH_MARGIN)
                        bestIntent = st.Last;
                }

                // ------- Apply targets (with anti-pile dither) -------
                switch (bestIntent)
                {
                    case Intent.Eat:
                        if (stall != null) a.TargetPos = DitherAround(stall.StationPos, a.Id, 0.7f);
                        a.AllowWander = false;
                        break;

                    case Intent.Sleep:
                        a.TargetPos = a.HomePos; // no dither; homes can be distinct
                        a.AllowWander = false;
                        break;

                    case Intent.Work:
                        // BUGFIX: use the role-aware workTarget we computed (do NOT overwrite with NearestOf again)
                        a.TargetPos = DitherAround(st.LastWorkTarget, a.Id, 1.0f);
                        a.AllowWander = false;
                        break;

                    default:
                        a.AllowWander = true;
                        break;
                }

                // Update cooldowns on transitions
                if (st.Last != bestIntent)
                {
                    if (bestIntent == Intent.Eat)
                        st.EatCooldownUntil = tick + EAT_COOLDOWN_TICKS;
                    else if (bestIntent == Intent.Sleep)
                        st.SleepCooldownUntil = tick + SLEEP_COOLDOWN_TICKS;
                }

                st.Last = bestIntent;
            }
        }

        // ---------- helpers ----------
        private static float TravelSeconds(Vector3 from, Vector3 to, float speedMps)
        {
            float d = Vector3.Distance(from, to);
            return d / Mathf.Max(0.05f, speedMps);
        }

        private static float EstimateFoodQueueWaitSec(World world, Worksite stall, Agent me)
        {
            float service  = Mathf.Max(0.1f, stall.ServiceDurationSec);
            float remaining = Mathf.Max(0f, stall.ServiceRemainingSec);

            int others = 0;
            Vector3 sPos = stall.StationPos;
            foreach (var ag in world.Agents)
            {
                if (ag.Id == me.Id) continue;
                if (Vector3.Distance(ag.Pos, sPos) <= 2.5f)
                    others++;
            }

            // One in service; rest waiting
            int waiting = Mathf.Max(0, others - 1);
            return waiting * service + remaining;
        }

        private static Vector3 NearestOf(Vector3 from, params Vector3?[] candidates)
        {
            float best = float.PositiveInfinity;
            Vector3 bestPos = from;
            foreach (var c in candidates)
            {
                if (!c.HasValue) continue;
                float d = Vector3.Distance(from, c.Value);
                if (d < best) { best = d; bestPos = c.Value; }
            }
            return bestPos;
        }

        private static Vector3 DitherAround(Vector3 center, int agentId, float radius)
        {
            // Golden-angle hash for uniform ring placement per agent (deterministic)
            const float TAU = 6.28318530718f;
            // hash in [0,1)
            float h = Mathf.Abs(Mathf.Sin(agentId * 12.9898f + 78.233f)) % 1f;
            float ang = h * TAU;
            return center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radius;
        }
    }
}
