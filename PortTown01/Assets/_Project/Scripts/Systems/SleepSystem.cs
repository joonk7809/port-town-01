using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    public class SleepSystem : ISimSystem
    {
        public string Name => "Sleep";

        const float REST_PER_SEC = 0.6f;  // tune (≈36/min)
        const float ARRIVE_F = 1.5f;

        public void Tick(World world, int _, float dt)
        {
            foreach (var a in world.Agents)
            {
                if (a.Phase != DayPhase.Sleep) continue;

                float arriveDist = a.InteractRange * ARRIVE_F;
                if (Vector3.Distance(a.Pos, a.HomePos) <= arriveDist)
                {
                    a.Rest = Mathf.Min(100f, a.Rest + REST_PER_SEC * dt);
                }
            }
        }
    }
}
