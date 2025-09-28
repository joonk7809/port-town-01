using System.Linq;
using UnityEngine;            // for Mathf
using PortTown01.Core;
using PortTown01.Econ;

namespace PortTown01.Systems
{
    // Price–time priority matcher with escrow- and capacity-aware delivery.
    // Invariants:
    // - Coins never created/destroyed: all bid escrow either pays a seller or is refunded to the bidder.
    // - Items never created/destroyed: all ask escrow either delivers to a buyer or returns to the seller on cancel.
    public class OrderMatchingSystem : ISimSystem
    {
        public string Name => "OrderMatch";

        public void Tick(World world, int tick, float dt)
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

                // Resolve agents
                var buyer  = world.Agents.FirstOrDefault(x => x.Id == b.AgentId);
                var seller = world.Agents.FirstOrDefault(x => x.Id == a.AgentId);
                if (buyer == null || seller == null)
                {
                    // Drop bad orders safely
                    SafeCancelBid(world, b, buyer);
                    SafeCancelAsk(world, a, seller);
                    bi++; ai++;
                    continue;
                }

                int maxByQty = Mathf.Min(b.Qty, a.Qty);
                if (maxByQty <= 0)
                {
                    // Advance whichever is empty
                    if (b.Qty <= 0) bi++;
                    if (a.Qty <= 0) ai++;
                    continue;
                }

                // --- Escrow limits ---
                int price = Mathf.Max(1, a.UnitPrice);
                int byCoins = b.EscrowCoins / price;                   // how many units escrow can afford
                int byAskEscrow = Mathf.Max(0, a.EscrowItems);         // how many units are escrowed for delivery
                int tradeCap = Mathf.Min(maxByQty, Mathf.Min(byCoins, byAskEscrow));

                // Decide which side is blocking if nothing affordable
                if (tradeCap <= 0)
                {
                    if (byCoins <= 0)
                    {
                        // Buyer has no spendable escrow at this price → refund and remove bid
                        SafeCancelBid(world, b, buyer);
                        bi++;   // move to next bid
                        continue; // keep same ask
                    }
                    if (byAskEscrow <= 0)
                    {
                        // Seller has no escrowed items (stale ask) → return any residual escrowed items and remove ask
                        SafeCancelAsk(world, a, seller);
                        ai++;   // move to next ask
                        continue; // keep same bid
                    }
                    // If we got here, it's a logic edge; advance both to avoid deadlock
                    SafeCancelBid(world, b, buyer);
                    SafeCancelAsk(world, a, seller);
                    bi++; ai++;
                    continue;
                }

                // --- Capacity limit on buyer (Food is 0 kg now, but keep generic) ---
                int carryFit = tradeCap;
                float unitKg = ItemDefs.KgPerUnit(a.Item);
                if (unitKg > 0f)
                {
                    float remainingKg = buyer.CapacityKg - buyer.Carry.Kg;
                    if (remainingKg <= 0f) carryFit = 0;
                    else
                    {
                        int maxFit = Mathf.FloorToInt(remainingKg / unitKg);
                        carryFit = Mathf.Min(carryFit, Mathf.Max(0, maxFit));
                    }
                }

                if (carryFit <= 0)
                {
                    // Buyer can't carry any: refund and drop this bid, keep ask
                    SafeCancelBid(world, b, buyer);
                    bi++;
                    continue;
                }

                int tradeQty   = carryFit;
                int tradeCoins = tradeQty * price;

                // --- Settle: escrow coins -> seller; escrow items -> buyer ---
                // Coins: debit bid escrow (never below 0), credit seller
                b.EscrowCoins -= tradeCoins;
                if (b.EscrowCoins < 0) b.EscrowCoins = 0; // defensive; should not happen given byCoins cap
                seller.Coins  += tradeCoins;

                // Items: reduce ask escrow and deliver to buyer
                a.EscrowItems -= tradeQty;
                if (a.EscrowItems < 0) a.EscrowItems = 0; // defensive
                buyer.Carry.Add(a.Item, tradeQty);

                // Ledgers
                if (a.Item == ItemType.Food)
                {
                    world.FoodSold += tradeQty;
                    if (seller.IsVendor) world.VendorRevenue += tradeCoins;
                }

                // Reduce open quantities
                b.Qty -= tradeQty;
                a.Qty -= tradeQty;

                // If the bid is fully filled, refund any residual escrow to the bidder
                if (b.Qty <= 0 && b.EscrowCoins > 0)
                {
                    buyer.Coins    += b.EscrowCoins;
                    b.EscrowCoins   = 0;
                }

                // Advance indices if an order filled
                if (b.Qty <= 0) bi++;
                if (a.Qty <= 0) ai++;
            }

            // Note: expiry/pruning of stale orders should be handled in the posting systems (e.g., FoodTradeSystem).
            // If you also prune here, ALWAYS refund bid escrow and return ask escrowed items to seller.
        }

        private static void SafeCancelBid(World world, Offer bid, Agent buyer)
        {
            if (bid == null) return;
            if (bid.EscrowCoins > 0 && buyer != null)
            {
                buyer.Coins += bid.EscrowCoins;
                bid.EscrowCoins = 0;
            }
            bid.Qty = 0;
        }

        private static void SafeCancelAsk(World world, Offer ask, Agent seller)
        {
            if (ask == null) return;
            if (ask.EscrowItems > 0 && seller != null)
            {
                // Return any still-escrowed items to the seller
                seller.Carry.Add(ask.Item, ask.EscrowItems);
                ask.EscrowItems = 0;
            }
            ask.Qty = 0;
        }
    }
}
