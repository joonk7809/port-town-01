using System.Linq;
using UnityEngine;
using PortTown01.Core;
using PortTown01.Econ;

namespace PortTown01.Systems
{
    // Queue-like stall with timed checkout. During Work, only buy if *critically* hungry.
    public class FoodTradeSystem : ISimSystem
    {
        public string Name => "FoodTrade_Post";

        private const int   FOOD_PRICE = 5;
        private const float BUY_TRIG   = 55f; // leisure/evening shopping kicks in sooner
        private const float CRIT_TRIG  = 28f; // workers stay on shift unless quite hungry
        private const float ARRIVE_F   = 1.2f;


        private Worksite _market;

        public void Tick(World world, int _, float dt)
        {
            var vendor = world.Agents.FirstOrDefault(a => a.IsVendor);
            if (vendor == null) return;

            if (_market == null)
                _market = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.Trading);

            // Vendor: maintain standing ask (escrow items)
            int vendorStock = vendor.Carry.Get(ItemType.Food);
            bool hasOpenAsk = world.FoodBook.Asks.Any(o => o.AgentId == vendor.Id && o.Qty > 0);
            if (!hasOpenAsk && vendorStock > 0)
            {
                vendor.Carry.TryRemove(ItemType.Food, vendorStock);
                world.FoodBook.Asks.Add(new Offer
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
                });
            }

            var stallPos = (_market != null) ? _market.StationPos : vendor.Pos;

            foreach (var a in world.Agents)
            {
                if (a.IsVendor) continue;

                // Decide threshold based on phase
                bool onShift = a.Phase == DayPhase.Work;
                float thresh = onShift ? CRIT_TRIG : BUY_TRIG;

                bool needsFood = (a.Food < thresh) && (a.Carry.Get(ItemType.Food) == 0);
                if (!needsFood) continue; // do NOT hijack workers unless critical

                // Walk to stall
                float arriveDist = a.InteractRange * ARRIVE_F;
                if (Vector3.Distance(a.Pos, stallPos) > arriveDist)
                {
                    a.AllowWander = false;
                    a.TargetPos = stallPos;
                    continue;
                }

                // If we have a stall, enforce single-occupant timed checkout
                if (_market != null)
                {
                    if (_market.OccupantId is null)
                    {
                        _market.OccupantId = a.Id;
                        _market.InUse = true;
                        _market.ServiceRemainingSec = Mathf.Max(0.01f, _market.ServiceDurationSec);
                    }

                    if (_market.OccupantId != a.Id)
                        continue; // wait your turn

                    // Occupant: tick checkout
                    _market.ServiceRemainingSec -= dt;
                    if (_market.ServiceRemainingSec > 0f)
                    {
                        a.AllowWander = false;
                        a.TargetPos = stallPos;
                        continue;
                    }

                    // Post one 1-unit bid if none open
                    bool hasOpenBid = world.FoodBook.Bids.Any(o => o.AgentId == a.Id && o.Qty > 0);
                    if (!hasOpenBid && a.Coins >= FOOD_PRICE)
                    {
                        a.Coins -= FOOD_PRICE; // escrow
                        world.FoodBook.Bids.Add(new Offer
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
                        });
                    }

                    // Release stall
                    _market.OccupantId = null;
                    _market.InUse = false;
                    _market.ServiceRemainingSec = 0f;

                    a.AllowWander = true;
                    continue;
                }

                // Fallback (no stall defined): proximity-only
                bool hasOpen = world.FoodBook.Bids.Any(o => o.AgentId == a.Id && o.Qty > 0);
                if (!hasOpen && a.Coins >= FOOD_PRICE)
                {
                    a.Coins -= FOOD_PRICE;
                    world.FoodBook.Bids.Add(new Offer
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
                    });
                }
            }
        }
    }
}
