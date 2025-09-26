using System.Collections.Generic;
using PortTown01.Core;

namespace PortTown01.Systems
{
    public class MillProcessingSystem : ISimSystem
    {
        public string Name => "MillProcessing";

        private readonly Dictionary<int, float> _timers = new(); // per-mill timer
        private const float SEC_PER_PLANK = 0.75f;               // tune later

        public void Tick(World world, int _, float dt)
        {
            // For each mill building, run a simple single-station timer:
            foreach (var b in world.Buildings)
            {
                if (b.Type != BuildingType.Mill) continue;

                if (!_timers.ContainsKey(b.Id)) _timers[b.Id] = 0f;

                // If there’s at least 1 log in storage, advance timer and process
                while (b.Storage.Get(ItemType.Log) > 0 && _timers[b.Id] + dt >= SEC_PER_PLANK)
                {
                    // consume 1 log, produce 1 plank
                    b.Storage.TryRemove(ItemType.Log, 1);
                    b.Storage.Add(ItemType.Plank, 1);

                    // spend processing time
                    if (_timers[b.Id] + dt >= SEC_PER_PLANK)
                    {
                        // carry over any fractional leftover into the next loop
                        float newAccum = _timers[b.Id] + dt - SEC_PER_PLANK;
                        _timers[b.Id] = newAccum;
                        dt = 0f; // remaining dt is accounted for above
                    }
                }

                // If we didn’t consume enough to cross a threshold, just accumulate time
                _timers[b.Id] += dt;
                if (_timers[b.Id] > SEC_PER_PLANK) _timers[b.Id] = SEC_PER_PLANK; // clamp
            }
        }
    }
}
