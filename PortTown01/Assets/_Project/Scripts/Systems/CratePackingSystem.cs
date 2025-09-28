using System.Collections.Generic;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // At the Mill, pack 10 Planks -> 1 Crate at a steady rate.
    public class CratePackingSystem : ISimSystem
    {
        public string Name => "CratePacking";

        const int   PLANKS_PER_CRATE = 10;
        const float SEC_PER_CRATE    = 1.5f;

        private readonly Dictionary<int, float> _timers = new(); // per-mill

        public void Tick(World world, int _, float dt)
        {
            foreach (var b in world.Buildings)
            {
                if (b.Type != BuildingType.Mill) continue;
                if (!_timers.ContainsKey(b.Id)) _timers[b.Id] = 0f;

                // if we have enough planks, run a station timer
                if (b.Storage.Get(ItemType.Plank) >= PLANKS_PER_CRATE)
                {
                    _timers[b.Id] += dt;
                    if (_timers[b.Id] >= SEC_PER_CRATE)
                    {
                        _timers[b.Id] = 0f;
                        b.Storage.TryRemove(ItemType.Plank, PLANKS_PER_CRATE);
                        b.Storage.Add(ItemType.Crate, 1);
                    }
                }
                else
                {
                    // idle timer doesn’t accumulate when starved
                    _timers[b.Id] = 0f;
                }
            }
        }
    }
}
