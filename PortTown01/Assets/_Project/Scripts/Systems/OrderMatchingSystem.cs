using System.Linq;
using UnityEngine;            // for Mathf, Debug
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

                // Drop bad orders safely with diagnostics
                if (buyer == null)
                {
                    Debug.LogError($"[MATCH] Missing buyer Agent#{b.AgentId} for Bid#{b.Id}; canceling bid.");
                    SafeCancelBid(world, b, null);
                    bi++;
                    continue;
                }
                if (seller == null)
                {
                    Debug.LogError($"[MATCH] Missing seller Agent#{a.AgentId} for Ask#{a.Id}; canceling ask.");
                    SafeCancelAsk(world, a, null);
                    ai++;
                    continue;
                }

                // Maximum by posted quantities
                int maxByQty = Mathf.Min(b.Qty, a.Qty);
                if (maxByQty <= 0)
                {
                    if (b.Qty <= 0) bi++;
                    if (a.Qty <= 0) ai++;
                    continue;
                }

                // --- Escrow limits (coins and items) ---
                int price = Mathf.Max(1, a.UnitPrice); // settle at ask
                int byCoins     = b.EscrowCoins / price;            // units affordable by bid escrow
                int byAskEscrow = Mathf.Max(0, a.EscrowItems);      // units available in ask escrow
                int tradeCap    = Mathf.Min(maxByQty, Mathf.Min(byCoins, byAskEscrow));

                if (tradeCap <= 0)
                {
                    if (byCoins <= 0)
                    {
                        Debug.LogWarning($"[MATCH] Bid#{b.Id} underfunded (escrow={b.EscrowCoins}, price={price}) @tick {tick}; canceling bid.");
                        SafeCancelBid(world, b, buyer);
                        bi++;
                        continue;
                    }
                    if (byAskEscrow <= 0)
                    {
                        Debug.LogWarning($"[MATCH] Ask#{a.Id} under-escrowed (escrowItems={a.EscrowItems}) @tick {tick}; canceling ask.");
                        SafeCancelAsk(world, a, seller);
                        ai++;
                        continue;
                    }
                    // Fallback (shouldn't reach here)
                    Debug.LogWarning($"[MATCH] Deadlock fallback Bid#{b.Id}/Ask#{a.Id}; canceling both.");
                    SafeCancelBid(world, b, buyer);
                    SafeCancelAsk(world, a, seller);
                    bi++; ai++;
                    continue;
                }

                // --- Capacity limit on buyer carry ---
                int carryFit = tradeCap;
                float unitKg = ItemDefs.KgPerUnit(a.Item);
                if (unitKg > 0f)
                {
                    float remainingKg = buyer.CapacityKg - buyer.Carry.Kg;
                    if (remainingKg <= 0f)
                    {
                        Debug.LogWarning($"[MATCH] Buyer Agent#{buyer.Id} has no carry capacity; canceling Bid#{b.Id}.");
                        SafeCancelBid(world, b, buyer);
                        bi++;
                        continue;
                    }

                    int maxFit = Mathf.FloorToInt(remainingKg / unitKg);
                    carryFit = Mathf.Min(carryFit, Mathf.Max(0, maxFit));
                    if (carryFit <= 0)
                    {
                        Debug.LogWarning($"[MATCH] Buyer Agent#{buyer.Id} cannot fit any units (remainingKg={remainingKg:F2}, unitKg={unitKg:F2}); canceling Bid#{b.Id}.");
                        SafeCancelBid(world, b, buyer);
                        bi++;
                        continue;
                    }
                }

                // Final trade terms
                int tradeQty   = carryFit;
                int tradeCoins = tradeQty * price;

                // --- Pre-debit guards (defensive; should be implied by tradeCap) ---
                if (b.EscrowCoins < tradeCoins)
                {
                    Debug.LogError($"[MATCH][ESCROW] Bid#{b.Id} underfunded pre-debit: need={tradeCoins}, have={b.EscrowCoins}. Skipping match.");
                    SafeCancelBid(world, b, buyer);
                    bi++;
                    continue;
                }
                if (a.EscrowItems < tradeQty)
                {
                    Debug.LogError($"[MATCH][ESCROW] Ask#{a.Id} under-escrow pre-debit: needQty={tradeQty}, have={a.EscrowItems}. Skipping match.");
                    SafeCancelAsk(world, a, seller);
                    ai++;
                    continue;
                }

                // --- Settle: escrow coins -> seller; escrow items -> buyer ---
                // Coins
                b.EscrowCoins -= tradeCoins;
                seller.Coins  += tradeCoins;

                // Items
                a.EscrowItems -= tradeQty;
                buyer.Carry.Add(a.Item, tradeQty);

                // Counters/telemetry
                if (a.Item == ItemType.Food)
                {
                    world.FoodSold += tradeQty;
                    if (seller.IsVendor) world.VendorRevenue += tradeCoins;
                }

                // Reduce open quantities
                b.Qty -= tradeQty;
                a.Qty -= tradeQty;

                // --- Postconditions (no negatives allowed) ---
                if (b.EscrowCoins < 0)
                    Debug.LogError($"[MATCH][POST] Bid#{b.Id} escrow negative after match: {b.EscrowCoins}");
                if (a.EscrowItems < 0)
                    Debug.LogError($"[MATCH][POST] Ask#{a.Id} item escrow negative after match: {a.EscrowItems}");
                if (buyer.Coins < 0)
                    Debug.LogError($"[MATCH][POST] Buyer Agent#{buyer.Id} coins negative: {buyer.Coins}");
                if (seller.Coins < 0)
                    Debug.LogError($"[MATCH][POST] Seller Agent#{seller.Id} coins negative: {seller.Coins}");

                // Refund any residual escrow if bid fully filled
                if (b.Qty <= 0 && b.EscrowCoins > 0)
                {
                    buyer.Coins     += b.EscrowCoins;
                    // NOTE: do not hide bugs; refund should zero escrow exactly
                    b.EscrowCoins    = 0;
                }

                // Advance indices if an order filled
                if (b.Qty <= 0) bi++;
                if (a.Qty <= 0) ai++;
            }

            // Note: expiry/pruning of stale orders should be handled elsewhere (posting/cleanup systems).
        }

        private static void SafeCancelBid(World world, Offer bid, Agent buyer)
        {
            if (bid == null) return;
            if (bid.EscrowCoins > 0 && buyer != null)
            {
                buyer.Coins += bid.EscrowCoins; // refund entire remaining escrow
                bid.EscrowCoins = 0;
            }
            bid.Qty = 0;
        }

        private static void SafeCancelAsk(World world, Offer ask, Agent seller)
        {
            if (ask == null) return;
            if (ask.EscrowItems > 0 && seller != null)
            {
                seller.Carry.Add(ask.Item, ask.EscrowItems); // release item escrow
                ask.EscrowItems = 0;
            }
            ask.Qty = 0;
        }
    }
}
