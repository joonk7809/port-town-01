using System.Linq;
using UnityEngine;
using PortTown01.Core;
using PortTown01.Econ;

namespace PortTown01.Systems
{
    public class PlannerSystem : ISimSystem
    {
        public string Name => "Planner";

        // Tunables
        private const float C_DIST_PER_M     = 0.02f;  // travel cost per meter
        private const float C_WAIT_PER_SEC   = 0.05f;  // queue time disutility
        private const float FATIGUE_COST     = 0.02f;  // simple penalty if low rest
        private const float HYSTERESIS_BAND  = 0.15f;  // utility band to prevent thrash

        private float _accum;

        public void Tick(World world, int tick, float dt)
        {
            _accum += dt;
            if (_accum < 0.2f) return; // run at 5 Hz to avoid churn
            _accum = 0f;

            // precompute stall stats (for M/M/1)
            var stall = world.Worksites.FirstOrDefault(w => w.Type == WorkType.Trading);
            float mu = 1f / Mathf.Max(0.01f, stall?.ServiceDurationSec ?? 1.2f); // μ
            float lambda = EstimateLambda(world, stall); // inferred arrivals/sec

            float rho = Mathf.Clamp01(lambda / Mathf.Max(0.01f, mu));
            float Wq  = (rho * rho) / (mu * Mathf.Max(0.001f, 1f - rho)); // M/M/1

            foreach (var a in world.Agents)
            {
                // sleep overrides
                if (a.Phase == DayPhase.Sleep)
                {
                    a.Intent = AgentIntent.Sleep;
                    a.TargetPos = a.HomePos;
                    continue;
                }

                // eat override: critical hunger -> Eat intent
                bool critical = a.Food < 28f && a.Carry.Get(ItemType.Food) == 0;
                if (critical)
                {
                    a.Intent = AgentIntent.Eat;
                    a.TargetPos = stall?.StationPos ?? a.Pos;
                    continue;
                }

                // Work if in Work phase and has a role, else leisure
                if (a.Phase == DayPhase.Work && a.Role != JobRole.None)
                {
                    // choose between their job vs detour to eat if moderately hungry
                    float uWork = UtilityForWork(world, a);
                    float uEat  = UtilityForEat(world, a, stall, Wq);
                    float delta = uEat - uWork;

                    if (delta > HYSTERESIS_BAND)
                    {
                        a.Intent   = AgentIntent.Eat;
                        a.TargetPos = stall?.StationPos ?? a.TargetPos;
                    }
                    else
                    {
                        a.Intent = AgentIntent.Work;
                        // Work target chosen by specific work systems (DemoHarvest/Haul)
                    }
                }
                else
                {
                    // leisure vs eat if hungry
                    float uLeisure = 0f;
                    float uEat = UtilityForEat(world, a, stall, Wq);
                    if (uEat > uLeisure + HYSTERESIS_BAND)
                    {
                        a.Intent = AgentIntent.Eat;
                        a.TargetPos = stall?.StationPos ?? a.TargetPos;
                    }
                    else
                    {
                        a.Intent = AgentIntent.Leisure;
                        // leisure target handled by wanderer
                    }
                }
            }
        }

        private float UtilityForWork(World world, Agent a)
        {
            // Rough throughput assumptions (tune after you log actual rates):
            // Logger: ~0.03 logs/sec (≈ 1.8 logs/min) when in steady loop
            // Hauler: ~0.05 crates/sec (≈ 3 crates/min) accounting for travel/queue at dock
            float expectedPerMin = a.Role switch
            {
                JobRole.Logger => 1.8f, // logs per minute estimate
                JobRole.Hauler => 3.0f, // crates per minute estimate
                _ => 0f
            };

            float wagePerMin = a.Role switch
            {
                JobRole.Logger => expectedPerMin * EconDefs.WAGE_PER_LOG,
                JobRole.Hauler => expectedPerMin * EconDefs.HAUL_PAY_PER_CRATE,
                _ => 0f
            };

            float dist = (a.TargetPos - a.Pos).magnitude;
            float cost = dist * C_DIST_PER_M + (a.Rest < 35f ? FATIGUE_COST * (35f - a.Rest) : 0f);
            return wagePerMin - cost;
        }


        private float UtilityForEat(World world, Agent a, Worksite stall, float Wq)
        {
            if (stall == null) return -999f;
            float dist = (stall.StationPos - a.Pos).magnitude;
            float travelCost = dist * C_DIST_PER_M;
            float queueCost  = Mathf.Clamp(Wq, 0f, 30f) * C_WAIT_PER_SEC; // bound extreme ρ->1
            float needRelief = Mathf.Max(0f, 80f - a.Food) * 0.2f;        // more hungry → more utility
            return needRelief - travelCost - queueCost - (a.Rest < 35f ? FATIGUE_COST * (35f - a.Rest) : 0f);
        }

        private float EstimateLambda(World world, Worksite stall)
        {
            if (stall == null) return 0.01f;

            // Proxy arrival rate = agents within 6m targeting stall / second
            int approaching = world.Agents.Count(x =>
                (x.Intent == AgentIntent.Eat || x.Food < 40f) &&
                (stall.StationPos - x.Pos).sqrMagnitude < 36f);

            // smooth a bit
            return Mathf.Lerp(0.01f, approaching, 0.2f);
        }
    }
}
