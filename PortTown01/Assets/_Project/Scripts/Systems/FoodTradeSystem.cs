using System.Linq;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Minimal direct trade: if an agent is hungry and has coins, buy 1 Food from any vendor at a fixed price.
    public class FoodTradeSystem : ISimSystem
    {
        public string Name => "FoodTrade";

        private const int FOOD_PRICE = 5;        // coins per unit
        private const float BUY_THRESHOLD = 50f; // need level to trigger a buy
        private const float ARRIVE_FACTOR = 1.5f;

        public void Tick(World world, int _, float dt)
        {
            var vendors = world.Agents.Where(a => a.IsVendor).ToList();
            if (vendors.Count == 0) return;

            foreach (var a in world.Agents)
            {
                if (a.IsVendor) continue;

                // If hungry and no Food on hand, try to buy 1 unit
                bool hungry = a.Food < BUY_THRESHOLD;
                bool hasNoFoodItem = a.Carry.Get(ItemType.Food) <= 0;

                if (!hungry || !hasNoFoodItem) continue;
                float arriveDist = a.InteractRange * ARRIVE_FACTOR;
                float dist = Vector3.Distance(a.Pos, vendor.Pos);
                if (dist > arriveDist)
                {
                    a.AllowWander = false;            // stop random targets
                    a.TargetPos = vendor.Pos;         // head to market
                    continue;                          // not there yet
                }

                if (a.Coins < FOOD_PRICE) continue;

                // find a vendor with stock
                var v = vendors.FirstOrDefault(vd => vd.Carry.Get(ItemType.Food) > 0);
                if (v == null) continue;
                {
                    a.Coins -= FOOD_PRICE;
                    vendor.Coins += FOOD_PRICE;
                    vendor.Carry.TryRemove(ItemType.Food, 1);
                    a.Carry.Add(ItemType.Food, 1);

                    // Optional: after buying, let them wander/work again.
                    a.AllowWander = true;
                }
            }
        }
    }
}
