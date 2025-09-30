using System.Linq;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Runs at 1 Hz: places restock orders when vendor inv+forSale < s, delivers after lead time,
    // pays wholesale cost to an external sink (CityWholesalerCoins) and records external outflow.
    // Recognizes COGS at payment time; delivery only moves items, never money.
    public class VendorRestockSystem : ISimSystem
    {
        public string Name => "VendorRestock";

        // Must match the fixed sim rate (20 Hz by default).
        // If you expose it elsewhere, read from a central constant.
        private const int TICKS_PER_SEC = 20;

        public void Tick(World world, int tick, float dt)
        {
            if (!SimTicks.Every1Hz(tick)) return;

            var vendor = world.Agents.FirstOrDefault(a => a.IsVendor);
            if (vendor == null) return;

            // Available for sale = on-hand + already escrowed in asks
            int vendorInv   = vendor.Carry.Get(ItemType.Food);
            int vendorEsc   = world.FoodBook?.Asks.Where(o => o.AgentId == vendor.Id && o.Qty > 0)
                                                  .Sum(o => o.EscrowItems) ?? 0;
            int forSale     = vendorInv + vendorEsc;

            // Base-stock policy: if below s, order up to S in batch size Q
            if (forSale < world.Food_s)
            {
                int targetRaise = Mathf.Max(0, world.Food_S - forSale);
                // Round up to whole batches of Q (at least one batch)
                int batchesNeeded = Mathf.CeilToInt(targetRaise / (float)world.Food_Q);
                int orderQty      = Mathf.Max(world.Food_Q, batchesNeeded * world.Food_Q);

                // Wholesale cost; keep int coins (ceil if unit cost is fractional)
                int unitWholesale = Mathf.CeilToInt(world.FoodWholesale);
                int orderCost     = Mathf.CeilToInt(orderQty * world.FoodWholesale);

                // If vendor cannot afford full order, cap to max whole batches they can pay now
                int batchCost = Mathf.Max(1, world.Food_Q * unitWholesale);
                if (vendor.Coins < orderCost)
                {
                    int maxBatches = Mathf.FloorToInt(vendor.Coins / (float)batchCost);
                    if (maxBatches <= 0)
                    {
                        // Cannot afford even one batch; skip this tick
#if UNITY_EDITOR
                        Debug.LogWarning($"[RESTOCK] Vendor cannot afford batch (coins={vendor.Coins}, batchCost={batchCost}).");
#endif
                        // No money or items move
                        goto DeliverDue; // continue to deliveries only
                    }
                    orderQty  = maxBatches * world.Food_Q;
                    orderCost = maxBatches * batchCost;
                }

                // Pay now (external sink), deliver later.
                // Money accounting: vendor loses coins; external outflow increases; optional wholesaler ledger for visibility.
                vendor.Coins               -= orderCost;
                world.CityWholesalerCoins  += orderCost;     // visibility only; not part of circulating money pool
                world.CoinsExternalOutflow += orderCost;     // audit uses this to reconcile

                // Schedule delivery after lead time (in seconds -> ticks)
                int leadTicks = Mathf.Max(1, Mathf.CeilToInt(world.FoodLeadTimeSec * TICKS_PER_SEC));
                int dueTick   = tick + leadTicks;

                world.PendingFoodDeliveries.Add((dueTick, orderQty, orderCost));

#if UNITY_EDITOR
                Debug.Log($"[RESTOCK] Ordered qty={orderQty} cost={orderCost} (coins now {vendor.Coins}); dueTick={dueTick}");
#endif
            }

        DeliverDue:
            // Deliver any due orders (items only; coins already paid)
            for (int i = world.PendingFoodDeliveries.Count - 1; i >= 0; i--)
            {
                var pd = world.PendingFoodDeliveries[i];
                if (pd.dueTick <= tick)
                {
                    vendor.Carry.Add(ItemType.Food, pd.qty);
                    world.PendingFoodDeliveries.RemoveAt(i);
#if UNITY_EDITOR
                    Debug.Log($"[RESTOCK] Delivered qty={pd.qty} @tick {tick}");
#endif
                }
            }
        }
    }
}
