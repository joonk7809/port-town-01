using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    /// <summary>
    /// Prunes expired and empty orders, guaranteeing refunds/releases.
    /// Runs at 1 Hz. Uses time-to-live by comparing current tick to PostTick.
    /// </summary>
    public sealed class OrderBookCleanupSystem : ISimSystem
    {
        public string Name => "OrderGC";

        private const int TICKS_PER_SEC = 20;

        // TTLs (seconds). Keep modest to avoid zombie orders but not so short we kill real trades.
        private const int BID_TTL_SEC = 90;
        private const int ASK_TTL_SEC = 90;

        public void Tick(World world, int tick, float dt)
        {
            if (!SimTicks.Every1Hz(tick)) return;
            if (world.FoodBook == null) return;

            int bidExpiryTick = tick - BID_TTL_SEC * TICKS_PER_SEC;
            int askExpiryTick = tick - ASK_TTL_SEC * TICKS_PER_SEC;

            // ---- BIDS: refund coins then remove ----
            var bids = world.FoodBook.Bids;
            for (int i = bids.Count - 1; i >= 0; i--)
            {
                var b = bids[i];
                bool expired = b.PostTick <= bidExpiryTick;
                bool empty   = b.Qty <= 0;

                if (expired || empty)
                {
                    if (b.EscrowCoins > 0)
                    {
                        var buyer = world.Agents.FirstOrDefault(a => a.Id == b.AgentId);
                        if (buyer != null)
                        {
                            buyer.Coins += b.EscrowCoins;
                        }
                        else
                        {
                            Debug.LogWarning($"[ORDER_GC] Missing buyer Agent#{b.AgentId} for Bid#{b.Id} refund {b.EscrowCoins}; dropping coins (dev only).");
                        }
                        b.EscrowCoins = 0;
                    }
                    bids.RemoveAt(i);
                }
            }

            // ---- ASKS: release items then remove ----
            var asks = world.FoodBook.Asks;
            for (int i = asks.Count - 1; i >= 0; i--)
            {
                var a = asks[i];
                bool expired = a.PostTick <= askExpiryTick;
                bool empty   = a.Qty <= 0;

                if (expired || empty)
                {
                    if (a.EscrowItems > 0)
                    {
                        var seller = world.Agents.FirstOrDefault(ag => ag.Id == a.AgentId);
                        if (seller != null)
                        {
                            seller.Carry.Add(a.Item, a.EscrowItems);
                        }
                        else
                        {
                            Debug.LogWarning($"[ORDER_GC] Missing seller Agent#{a.AgentId} for Ask#{a.Id} release {a.EscrowItems}; dropping items (dev only).");
                        }
                        a.EscrowItems = 0;
                    }
                    asks.RemoveAt(i);
                }
            }
        }
    }
}
