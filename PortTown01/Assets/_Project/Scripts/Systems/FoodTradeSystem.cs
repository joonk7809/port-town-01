using System.Linq;
using UnityEngine;
using PortTown01.Core;
using PortTown01.Econ;

namespace PortTown01.Systems
{
    // Posts bids/asks when agents are at the market; no transfer here (matching is separate).
    public class FoodTradeSystem : ISimSystem
    {
        public string Name => "FoodTrade_Post";

        private const int FOOD_PRICE  = 5;      // vendor ask price
        private const float BUY_TRIG  = 50f;    // buy if Food need < this
        private const float ARRIVE_F  = 1.5f;   // range multiplier to consider "arrived"

        public void Tick(World world, int _, float dt)
        {
            var vendor = world.Agents.FirstOrDefault(a => a.IsVendor);
            if (vendor == null) return;

            // --- 1) Ensure vendor maintains a standing ask (escrow items) ---
            // If no open ask, create one for all vendor stock at fixed price.
            int vendorStock = vendor.Carry.Get(ItemType.Food);
            bool hasOpenAsk = world.FoodBook.Asks.Any(o => o.AgentId == vendor.Id && o.Qty > 0);
            if (!hasOpenAsk && vendorStock > 0)
            {
                // Move items to escrow (remove from inventory now)
                vendor.Carry.TryRemove(ItemType.Food, vendorStock);

                var ask = new Offer
                {
                    Id = world.FoodBook.NextOfferId++,
                    AgentId = vendor.Id,
                    Item = ItemType.Food,
                    Side = Side.Sell,
                    Qty = vendorStock,
                    UnitPrice = FOOD_PRICE,
                    PostTick = world.Tick,
                    ExpiryTick = -1,
                    EscrowItems = vendorStock
                };
                world.FoodBook.Asks.Add(ask);
                UnityEngine.Debug.Log($"[ASK] vendor={vendor.Id} qty={vendorStock} price={FOOD_PRICE} tick={world.Tick}");

            }

            // --- 2) For each hungry agent at the stall, post ONE-unit bid (escrow coins) ---
            foreach (var a in world.Agents)
            {
                if (a.IsVendor) continue;

                // must be hungry and not already holding Food
                if (!(a.Food < BUY_TRIG && a.Carry.Get(ItemType.Food) == 0)) continue;

                // go to vendor first (set target if far)
                float arriveDist = a.InteractRange * ARRIVE_F;
                float dist = Vector3.Distance(a.Pos, vendor.Pos);
                if (dist > arriveDist)
                {
                    a.AllowWander = false;
                    a.TargetPos = vendor.Pos;
                    continue;
                }

                // At stall. If they already have an open bid, skip.
                bool hasOpenBid = world.FoodBook.Bids.Any(o => o.AgentId == a.Id && o.Qty > 0);
                if (hasOpenBid) continue;

                // Need coins to escrow
                if (a.Coins < FOOD_PRICE) continue;

                // Escrow coins immediately (remove from wallet)
                a.Coins -= FOOD_PRICE;

                var bid = new Offer
                {
                    Id = world.FoodBook.NextOfferId++,
                    AgentId = a.Id,
                    Item = ItemType.Food,
                    Side = Side.Buy,
                    Qty = 1,
                    UnitPrice = FOOD_PRICE,  // price-taker at vendor price for now
                    PostTick = world.Tick,
                    ExpiryTick = -1,
                    EscrowCoins = FOOD_PRICE
                };
                world.FoodBook.Bids.Add(bid);
                UnityEngine.Debug.Log($"[BID] agent={a.Id} qty=1 price={FOOD_PRICE} tick={world.Tick}");

                // Let them resume other work after posting
                a.AllowWander = true;
            }
        }
    }
}
