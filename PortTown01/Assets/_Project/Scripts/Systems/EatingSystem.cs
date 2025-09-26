using PortTown01.Core;
using UnityEngine;

namespace PortTown01.Systems
{
    // If agent has Food item and is below a target, consume 1 to restore need.
    public class EatingSystem : ISimSystem
    {
        public string Name => "Eating";

        private const float EAT_IF_BELOW = 70f;  // start eating under this
        private const float EAT_AMOUNT   = 30f;  // need restored per item

        public void Tick(World world, int _, float dt)
        {
            foreach (var a in world.Agents)
            {
                if (a.IsVendor) continue;

                if (a.Food < EAT_IF_BELOW && a.Carry.Get(ItemType.Food) > 0)
                {
                    if (a.Carry.TryRemove(ItemType.Food, 1))
                        a.Food = Mathf.Min(100f, a.Food + EAT_AMOUNT);
                }
            }
        }
    }
}
