using System.Linq;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Runs at 1 Hz: places restock orders when vendor inv+forSale < s, delivers after lead time,
    // pays wholesale c to CityWholesaler (true coin sink). Recognizes COGS at delivery.
    public class VendorRestockSystem : ISimSystem
    {
        public string Name => "VendorRestock";

        private float _accum;

        public void Tick(World world, int tick, float dt)
        {
            _accum += dt;
            if (_accum < 1f) return;
            _accum -= 1f;

            var vendor = world.Agents.FirstOrDefault(a => a.IsVendor);
            if (vendor == null) return;

            // Compute vendor "available to sell": inventory Food + escrowed Food in asks
            int vendorInv = vendor.Carry.Get(ItemType.Food);
            int vendorEscrow = world.FoodBook?.Asks.Where(o => o.AgentId == vendor.Id).Sum(o => o.EscrowItems) ?? 0;
            int forSale = vendorInv + vendorEscrow;

            // Place order if below s
            if (forSale < world.Food_s)
            {
                int targetRaise = Mathf.Max(0, world.Food_S - forSale);
                // round to batches of Q
                int batches = Mathf.CeilToInt(targetRaise / (float)world.Food_Q);
                int qty     = Mathf.Max(world.Food_Q, batches * world.Food_Q);

                int cost = Mathf.CeilToInt(qty * world.FoodWholesale);

                // If vendor can't fully pay, allow partial (or skip if <= 0)
                if (vendor.Coins < cost)
                {
                    // partial fill by batches
                    int maxBatches = Mathf.FloorToInt(vendor.Coins / (world.FoodWholesale * world.Food_Q));
                    if (maxBatches <= 0) return; // can't afford any
                    qty  = maxBatches * world.Food_Q;
                    cost = Mathf.CeilToInt(qty * world.FoodWholesale);
                }

                // Pay now to the sink; deliver later
                vendor.Coins          -= cost;
                world.CityWholesalerCoins += cost;

                int dueTick = tick + Mathf.CeilToInt(world.FoodLeadTimeSec);
                world.PendingFoodDeliveries.Add((dueTick, qty, cost));
                // (COGS recognized implicitly by coin outflow; add an optional ledger if you keep P&L)
            }

            // Deliver any due orders
            for (int i = world.PendingFoodDeliveries.Count - 1; i >= 0; i--)
            {
                var pd = world.PendingFoodDeliveries[i];
                if (pd.dueTick <= tick)
                {
                    vendor.Carry.Add(ItemType.Food, pd.qty);
                    world.PendingFoodDeliveries.RemoveAt(i);
                }
            }
        }
    }
}
