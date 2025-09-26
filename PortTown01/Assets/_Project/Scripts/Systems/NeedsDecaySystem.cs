using System;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Very simple linear decay so we can see numbers move.
    // Later we'll tie rates to activity and day/night.
    public class NeedsDecaySystem : ISimSystem
    {
        public string Name => "NeedsDecay";

        // per-second decay rates (tune later)
        const float FOOD_DECAY_PER_SEC = 0.008f;  // ~0.8 / 100s -> ~2 hrs to drop 60 pts (placeholder)
        const float REST_DECAY_PER_SEC = 0.004f;

        public void Tick(World world, int _, float dt)
        {
            foreach (var a in world.Agents)
            {
                a.Food = Math.Max(0f, a.Food - FOOD_DECAY_PER_SEC * dt * 100f);
                a.Rest = Math.Max(0f, a.Rest - REST_DECAY_PER_SEC * dt * 100f);
                // Status/Security unchanged for now
            }
        }
    }
}
