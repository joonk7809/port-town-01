using System.Linq;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Agents must walk to the vendor and be within range to buy exactly 1 Food.
    public class FoodTradeSystem : ISimSystem
    {
        public string Name => "FoodTrade";

        private const int FOOD_PRICE = 5;        // coins per unit
        private const float BUY_THRESHOLD = 50f; // need level to trigger a buy
        private const float ARRIVE_FACTOR = 1.5f; // how close to be considered "at vendor"

        public void Tick(World world, int _, float dt)
        {
            var vendor = world.Agents.FirstOrDefault(a => a.IsVendor);
            if (vendor == null) return;

            foreach (var a in world.Agents)
            {
                if (a.IsVendor) continue;
                bool hungry = a.Food < BUY_THRESHOLD;
                bool hasNoFoodItem = a.Carry.Get(ItemType.Food) <= 0;

                if (!hungry || !hasNoFoodItem) continue;

                // 1) Go to vendor first
                float arriveDist = a.InteractRange * ARRIVE_FACTOR;
                float dist = Vector3.Distance(a.Pos, vendor.Pos);
                if (dist > arriveDist)
                {
                    a.AllowWander = false;     // stop random targets
                    a.TargetPos = vendor.Pos;  // head to market
                    continue;                  // not there yet
                }

                // 2) At vendor: buy 1 if possible
                if (a.Coins >= FOOD_PRICE && vendor.Carry.Get(ItemType.Food) > 0)
                {
                    a.Coins -= FOOD_PRICE;
                    vendor.Coins += FOOD_PRICE;
                    vendor.Carry.TryRemove(ItemType.Food, 1);
                    a.Carry.Add(ItemType.Food, 1);

                    // after buying, let them resume other targets
                    a.AllowWander = true;
                }
            }
        }
    }
}
