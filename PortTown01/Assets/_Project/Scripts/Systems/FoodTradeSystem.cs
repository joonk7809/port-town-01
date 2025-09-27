using System.Linq;
using UnityEngine;
using PortTown01.Core;
using PortTown01.Econ;

namespace PortTown01.Systems
{
    // Agents must (a) walk to the market stall, (b) acquire the single-occupant Trading worksite,
    // then (c) post exactly one 1-unit bid (escrow coins). Vendor keeps a standing ask.
    public class FoodTradeSystem : ISimSystem
    {
        public string Name => "FoodTrade_Post";

        private const int FOOD_PRICE  = 5;    // vendor ask price
        private const float BUY_TRIG  = 50f;  // buy if Food need < this
        private const float ARRIVE_F  = 1.2f; // arrival multiplier vs InteractRange

        private Worksite _market;             // cached market stall

        public void Tick(World world, int _, float dt)
        {
            var vendor = world.Agents.FirstOrDefault(a => a.IsVendor);
            if (vendor == null) return;

            // cache the stall
            if (_market == null)
                _market = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.Trading);

            // --- Vendor: standing ask with item escrow ---
            int vendorStock = vendor.Carry.Get(ItemType.Food);
            bool hasOpenAsk = world.FoodBook.Asks.Any(o => o.AgentId == vendor.Id && o.Qty > 0);
            if (!hasOpenAsk && vendorStock > 0)
            {
                vendor.Carry.TryRemove(ItemType.Food, vendorStock); // escrow items now
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
            }

            foreach (var a in world.Agents)
            {
                if (a.IsVendor) continue;

                // only hungry + no Food item
                if (!(a.Food < BUY_TRIG && a.Carry.Get(ItemType.Food) == 0)) continue;

                // 1) walk to stall
                var stallPos = _market != null ? _market.StationPos : vendor.Pos;
                float arriveDist = a.InteractRange * ARRIVE_F;
                if (Vector3.Distance(a.Pos, stallPos) > arriveDist)
                {
                    a.AllowWander = false;
                    a.TargetPos = stallPos;
                    continue;
                }

                // 2) acquire stall (single occupant)
                if (_market != null)
                {
                    if (_market.OccupantId is null)
                    {
                        _market.OccupantId = a.Id;
                        _market.InUse = true;
                    }
                    if (_market.OccupantId != a.Id)
                    {
                        // someone else is occupying; wait in place
                        continue;
                    }
                }

                // 3) post ONE bid if you don't already have one
                bool hasOpenBid = world.FoodBook.Bids.Any(o => o.AgentId == a.Id && o.Qty > 0);
                if (!hasOpenBid && a.Coins >= FOOD_PRICE)
                {
                    a.Coins -= FOOD_PRICE; // escrow coins
                    var bid = new Offer
                    {
                        Id = world.FoodBook.NextOfferId++,
                        AgentId = a.Id,
                        Item = ItemType.Food,
                        Side = Side.Buy,
                        Qty = 1,
                        UnitPrice = FOOD_PRICE,
                        PostTick = world.Tick,
                        ExpiryTick = -1,
                        EscrowCoins = FOOD_PRICE
                    };
                    world.FoodBook.Bids.Add(bid);
                }

                // release the stall so next buyer can step up
                if (_market != null && _market.OccupantId == a.Id)
                {
                    _market.OccupantId = null;
                    _market.InUse = false;
                }

                a.AllowWander = true; // resume other behavior
            }
        }
    }
}
