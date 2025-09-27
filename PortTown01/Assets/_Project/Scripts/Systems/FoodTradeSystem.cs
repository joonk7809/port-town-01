using System.Linq;
using UnityEngine;
using PortTown01.Core;
using PortTown01.Econ;

namespace PortTown01.Systems
{
    // Queue-like stall: arrive -> acquire stall -> wait ServiceDurationSec -> post 1-unit bid -> release
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

            if (_market == null)
                _market = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.Trading);

            // --- Vendor maintains a standing ask (escrow the items) ---
            int vendorStock = vendor.Carry.Get(ItemType.Food);
            bool hasOpenAsk = world.FoodBook.Asks.Any(o => o.AgentId == vendor.Id && o.Qty > 0);
            if (!hasOpenAsk && vendorStock > 0)
            {
                vendor.Carry.TryRemove(ItemType.Food, vendorStock); // move to escrow
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

            // No stall? fall back to vendor pos
            var stallPos = (_market != null) ? _market.StationPos : vendor.Pos;

            foreach (var a in world.Agents)
            {
                if (a.IsVendor) continue;

                bool needsFood = (a.Food < BUY_TRIG) && (a.Carry.Get(ItemType.Food) == 0);
                if (!needsFood) continue;

                // Step 1: walk to stall
                float arriveDist = a.InteractRange * ARRIVE_F;
                if (Vector3.Distance(a.Pos, stallPos) > arriveDist)
                {
                    a.AllowWander = false;
                    a.TargetPos = stallPos;
                    continue;
                }

                // If there is a stall, enforce single-occupant + timed checkout
                if (_market != null)
                {
                    // Acquire if free
                    if (_market.OccupantId is null)
                    {
                        _market.OccupantId = a.Id;
                        _market.InUse = true;
                        _market.ServiceRemainingSec = Mathf.Max(0.01f, _market.ServiceDurationSec);
                    }

                    // If this agent is NOT the occupant, wait your turn
                    if (_market.OccupantId != a.Id)
                        continue;

                    // This agent IS the occupant: tick the checkout timer
                    _market.ServiceRemainingSec -= dt;
                    if (_market.ServiceRemainingSec > 0f)
                    {
                        // hold position while checking out
                        a.AllowWander = false;
                        a.TargetPos = stallPos;
                        continue;
                    }

                    // Timer finished → post ONE 1-unit bid if none open yet
                    bool hasOpenBid = world.FoodBook.Bids.Any(o => o.AgentId == a.Id && o.Qty > 0);
                    if (!hasOpenBid && a.Coins >= FOOD_PRICE)
                    {
                        a.Coins -= FOOD_PRICE; // escrow coins
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

                    // Release stall for the next person in line
                    _market.OccupantId = null;
                    _market.InUse = false;
                    _market.ServiceRemainingSec = 0f;

                    a.AllowWander = true; // resume other behavior
                    continue;
                }

                // If we don't have a formal stall, just require proximity (fallback)
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
