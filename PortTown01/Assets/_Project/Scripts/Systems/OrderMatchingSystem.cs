using System.Linq;
using UnityEngine;            // for Mathf
using PortTown01.Core;
using PortTown01.Econ;

namespace PortTown01.Systems
{
    // Price–time priority matcher with capacity-aware delivery.
    // - If buyer can't carry the full cross, we trade the largest partial that fits.
    // - If nothing fits, we cancel the bid and refund its escrow.
    public class OrderMatchingSystem : ISimSystem
    {
        public string Name => "OrderMatch";

        public void Tick(World world, int _, float dt)
        {
            var bids = world.FoodBook.Bids
                .Where(o => o.Qty > 0)
                .OrderByDescending(o => o.UnitPrice)
                .ThenBy(o => o.PostTick)
                .ToList();

            var asks = world.FoodBook.Asks
                .Where(o => o.Qty > 0)
                .OrderBy(o => o.UnitPrice)
                .ThenBy(o => o.PostTick)
                .ToList();

            int bi = 0, ai = 0;
            while (bi < bids.Count && ai < asks.Count)
            {
                var b = bids[bi];
                var a = asks[ai];

                // No price cross → stop
                if (b.UnitPrice < a.UnitPrice) break;

                // Max possible by open qty
                int maxCrossQty = System.Math.Min(b.Qty, a.Qty);
                if (maxCrossQty <= 0) break;

                var buyer  = world.Agents.First(x => x.Id == b.AgentId);
                var seller = world.Agents.First(x => x.Id == a.AgentId);

                // ---- 6C.4 capacity-aware settlement ----
                int price = a.UnitPrice;

                // Limit by escrows (coins/items)
                int byCoins = (price > 0) ? (b.EscrowCoins / price) : maxCrossQty; // how many units buyer can afford from escrow
                int byAskEscrow = a.EscrowItems;                                   // how many units seller has escrowed
                int qtyByEscrow = System.Math.Min(maxCrossQty, System.Math.Min(byCoins, byAskEscrow));

                if (qtyByEscrow <= 0)
                {
                    // Invariant broken or empty escrow: cancel both and advance
                    b.Qty = 0; b.EscrowCoins = 0; bi++;
                    a.Qty = 0; a.EscrowItems = 0; ai++;
                    continue;
                }

                // Limit by buyer capacity (handles future non-zero Food weight)
                int carryFit = qtyByEscrow;
                float unitKg = ItemDefs.KgPerUnit(ItemType.Food);
                if (unitKg > 0f)
                {
                    float remainingKg = buyer.CapacityKg - buyer.Carry.Kg;
                    if (remainingKg <= 0f) carryFit = 0;
                    else
                    {
                        int maxFit = Mathf.FloorToInt(remainingKg / unitKg);
                        carryFit = System.Math.Min(carryFit, System.Math.Max(0, maxFit));
                    }
                }

                if (carryFit <= 0)
                {
                    // Buyer cannot carry even 1 unit → refund full bid escrow and cancel the bid
                    buyer.Coins += b.EscrowCoins;
                    b.EscrowCoins = 0;
                    b.Qty = 0;
                    bi++;            // next bid
                    continue;        // keep same ask
                }

                int tradeQty = carryFit;

                // --- Deliver items to buyer (fits by construction) ---
                buyer.Carry.Add(ItemType.Food, tradeQty);

                // --- Coins from bid escrow → seller wallet ---
                int coinsToSeller = price * tradeQty;
                b.EscrowCoins -= coinsToSeller;
                seller.Coins  += coinsToSeller;

                // --- Remove items from ask escrow ---
                a.EscrowItems -= tradeQty;

                // --- Reduce open quantities ---
                b.Qty -= tradeQty;
                a.Qty -= tradeQty;

                // Advance indices if an order filled
                if (b.Qty == 0) bi++;
                if (a.Qty == 0) ai++;
            }
        }
    }
}
