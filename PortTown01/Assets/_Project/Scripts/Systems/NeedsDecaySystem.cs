using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    public class NeedsDecaySystem : ISimSystem
    {
        public string Name => "NeedsDecay";

        // per-second decay (tweak-friendly)
        const float FOOD_DECAY_PER_SEC = 0.05f; // ~3 pts/min
        const float REST_DECAY_PER_SEC = 0.02f; // ~1.2 pts/min

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
