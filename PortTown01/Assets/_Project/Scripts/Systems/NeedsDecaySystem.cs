using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    public class NeedsDecaySystem : ISimSystem
    {
        public string Name => "NeedsDecay";

        // ~7.2 pts/min Food (=> ~72 over a 600s day), ~1.2 pts/min Rest
        const float FOOD_DECAY_PER_SEC = 0.20f;
        const float REST_DECAY_PER_SEC = 0.10f;

        public void Tick(World world, int _, float dt)
        {
            foreach (var a in world.Agents)
            {
                a.Food = Mathf.Max(0f, a.Food - FOOD_DECAY_PER_SEC * dt);
                a.Rest = Mathf.Max(0f, a.Rest - REST_DECAY_PER_SEC * dt);
            }
        }
    }
}
