using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PortTown01.Core;
using PortTown01.Econ;

namespace PortTown01.Systems
{
    /// Posts hunger-driven Food bids for agents and maintains vendor asks.
    public class FoodTradeSystem : ISimSystem
    {
        public string Name => "FoodTrade";

        // --- Tunables ---
        const float CRIT_HUNGER     = 28f;   // below this, always try to eat
        const float SEEK_HUNGER     = 55f;   // plan to eat if below this (when planner set Intent=Eat)
        const int   MAX_UNITS_WANT  = 2;     // never try to buy more than this at once
        const float BID_COOLDOWN    = 6f;    // seconds between bids per agent
        const int   ASK_CAP         = 20;    // vendor keeps this many units posted as asks

        private Worksite _stall;     // Trading worksite near the vendor
        private Agent _vendor;       // The vendor agent
        private readonly Dictionary<int, float> _nextBidAt = new(); // agentId -> simTime they can bid again

        public void Tick(World world, int _, float dt)
        {
            // --- lazy resolve stall + vendor ---
            if (_vendor == null) _vendor = world.Agents.FirstOrDefault(a => a.IsVendor);
            if (_stall  == null) _stall  = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.Trading);
            if (_vendor == null || _stall == null) return;

            float now = (float)world.SimTime;

            // --- Vendor: maintain a standing ask book up to ASK_CAP ---
            int openAskQty = world.FoodBook.Asks
                .Where(o => o.AgentId == _vendor.Id && o.Item == ItemType.Food && o.Qty > 0)
                .Sum(o => o.Qty);
            int inv = _vendor.Carry.Get(ItemType.Food);
            int toPost = Mathf.Min(inv, ASK_CAP - openAskQty);
            if (toPost > 0)
            {
                // move inventory to ask escrow & post
                if (_vendor.Carry.TryRemove(ItemType.Food, toPost))
                {
                    world.FoodBook.Asks.Add(new Offer
                    {
                        Id         = world.FoodBook.NextOfferId++,
                        AgentId    = _vendor.Id,
                        Item       = ItemType.Food,
                        Side       = Side.Sell,
                        Qty        = toPost,
                        UnitPrice  = world.FoodPrice, // live price
                        PostTick   = world.Tick,
                        ExpiryTick = world.Tick + 1800, // ~90s at 20 Hz
                        EscrowItems= toPost
                    });
                }
            }

            // --- Buyers: post hunger-driven bids when near the stall ---
            foreach (var a in world.Agents)
            {
                if (a.IsVendor) continue;

                int foodCarried = a.Carry.Get(ItemType.Food);
                bool critical = a.Food < CRIT_HUNGER && foodCarried == 0;
                bool plannerSaysEat = a.Intent == AgentIntent.Eat;

                // Gate by intent unless critical
                if (!critical && !plannerSaysEat) continue;

                // Must be near the stall to post
                float dist = Vector3.Distance(a.Pos, _stall.StationPos);
                if (dist > a.InteractRange * 1.25f)
                {
                    // Nudge toward stall; your MovementSystem will handle the rest
                    a.TargetPos = _stall.StationPos;
                    continue;
                }

                // Cooldown
                if (_nextBidAt.TryGetValue(a.Id, out var tNext) && now < tNext) continue;

                // Desired qty (don’t hoard)
                int needUnits = Mathf.Clamp(MAX_UNITS_WANT - foodCarried, 1, MAX_UNITS_WANT);

                // Willingness-to-pay from hunger:
                // hungrier (lower Food) → higher bid around the live price
                float h = Mathf.Clamp01((SEEK_HUNGER - a.Food) / Mathf.Max(1f, SEEK_HUNGER)); // 0..1
                int wtp = Mathf.RoundToInt(world.FoodPrice - 1 + h * 5); // span ≈ -1..+4
                wtp = Mathf.Clamp(wtp, EconDefs.FOOD_PRICE_MIN, EconDefs.FOOD_PRICE_MAX);

                // Budget: how many can we afford at this price?
                int affordQty = Mathf.Min(needUnits, wtp > 0 ? a.Coins / wtp : 0);
                if (affordQty <= 0) continue;

                int escrow = wtp * affordQty;
                a.Coins -= escrow;

                world.FoodBook.Bids.Add(new Offer
                {
                    Id         = world.FoodBook.NextOfferId++,
                    AgentId    = a.Id,
                    Item       = ItemType.Food,
                    Side       = Side.Buy,
                    Qty        = affordQty,
                    UnitPrice  = wtp,
                    PostTick   = world.Tick,
                    ExpiryTick = world.Tick + 800, // ~40s @ 20 Hz
                    EscrowCoins= escrow
                });

                _nextBidAt[a.Id] = now + BID_COOLDOWN;
            }

            // --- Optional: purge expired orders to keep the book tidy ---
            // (Cheap O(N) filter; do this here so we don't need a separate system.)
            world.FoodBook.Bids.RemoveAll(o => o.Qty <= 0 || (o.ExpiryTick > 0 && world.Tick >= o.ExpiryTick));
            world.FoodBook.Asks.RemoveAll(o => o.Qty <= 0 || (o.ExpiryTick > 0 && world.Tick >= o.ExpiryTick));
        }
    }
}
