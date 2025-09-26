using System.Linq;
using PortTown01.Core;
using PortTown01.Econ;

namespace PortTown01.Systems
{
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

                if (b.UnitPrice < a.UnitPrice) break; // no cross

                int tradeQty = System.Math.Min(b.Qty, a.Qty);
                if (tradeQty <= 0) break;

                // --- settle using escrow ---
                // Seller gets buyer's escrow coins; Buyer gets seller's escrow items.
                int price = a.UnitPrice; // pay ask (could also do midpoint/last)
                int coinsToSeller = price * tradeQty;

                // Safety rails: ensure escrows are sufficient
                if (b.EscrowCoins < coinsToSeller || a.EscrowItems < tradeQty)
                {
                    // If broken invariants, cancel what’s broken and continue
                    b.Qty = 0; b.EscrowCoins = 0;
                    a.Qty = 0; a.EscrowItems = 0;
                    bi++; ai++;
                    continue;
                }

                var buyer  = world.Agents.First(x => x.Id == b.AgentId);
                var seller = world.Agents.First(x => x.Id == a.AgentId);

                // Transfer coins from bid escrow to seller wallet
                b.EscrowCoins -= coinsToSeller;
                seller.Coins  += coinsToSeller;

                // Transfer items from ask escrow to buyer inventory
                a.EscrowItems -= tradeQty;
                buyer.Carry.Add(ItemType.Food, tradeQty);

                // Reduce open qty
                b.Qty -= tradeQty;
                a.Qty -= tradeQty;

                // advance indices if an order filled
                if (b.Qty == 0) bi++;
                if (a.Qty == 0) ai++;

                UnityEngine.Debug.Log($"[MATCH] buyer={b.AgentId} seller={a.AgentId} qty={tradeQty} price={a.UnitPrice} tick={world.Tick}");

            }

            // (Optional) we could refund remaining escrow on filled/cancelled/expired orders here.
            // For week 1, we keep unfilled remainder in escrow to avoid dupes.
        }
    }
}
