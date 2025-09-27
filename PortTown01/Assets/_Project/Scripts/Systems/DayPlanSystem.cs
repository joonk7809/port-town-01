using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Maps SimTime to a 24h day: default 600 sec/day (10 min). Sets per-agent Phase and basic movement intent.
    public class DayPlanSystem : ISimSystem
    {
        public string Name => "DayPlan";

        // 10 minutes = 1 sim day; tweak to taste
        const float DAY_SECONDS = 600f;
        const float START_Hour = 9f;

        // Work 08:00–18:00, Sleep 22:00–06:00, else Leisure
        static bool IsWork(float t)  { return In(t, 8, 18); }
        static bool IsSleep(float t) { return In(t, 22, 24) || In(t, 0, 6); }
        static bool In(float t, float h0, float h1)
        {
            float s0 = h0 / 24f * DAY_SECONDS, s1 = h1 / 24f * DAY_SECONDS;
            return t >= s0 && t < s1;
        }

        public void Tick(World world, int _, float dt)
        {
            float tDay = (float)((world.SimTime + (START_HOUR/24f)*DAY_SECONDS) % DAY_SECONDS);

            foreach (var a in world.Agents)
            {
                // Vendor/boss ignore phases
                if (a.IsVendor) { a.Phase = DayPhase.Leisure; continue; }

                // Decide phase
                if (IsSleep(tDay))      a.Phase = DayPhase.Sleep;
                else if (IsWork(tDay))  a.Phase = DayPhase.Work;
                else                    a.Phase = DayPhase.Leisure;

                // Off-shift routing
                if (a.Phase == DayPhase.Sleep)
                {
                    a.AllowWander = false;
                    a.TargetPos = a.HomePos; // go home to sleep
                }
                else if (a.Phase == DayPhase.Leisure)
                {
                    a.AllowWander = true; // let Movement pick randoms when idle
                }
                // Phase.Work: let job systems set precise targets
            }
        }
    }
}
