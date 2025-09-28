using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PortTown01.Core;
using PortTown01.Econ;

namespace PortTown01.Systems
{
    // Verifies money conservation, non-negative counts, and detects "stuck" production.
    public class AuditSystem : ISimSystem
    {
        public string Name => "Audit";

        // --- Tunables ---
        const float CHECK_EVERY_SEC     = 1.0f;   // cadence for audits
        const float STUCK_WINDOW_SEC    = 10.0f;  // no change for this long => warn
        const bool  VERBOSE_OK_SUMMARY  = false;  // set true if you want a green OK line each check

        // --- Money baseline ---
        private bool  _haveBaseline = false;
        private int   _baselineCoins = 0; // sum(all agent coins) + sum(open bid escrow)

        // --- Cadence ---
        private float _accum = 0f;

        // --- Stuck detection snapshot ---
        private int   _prevForestStock = int.MinValue;
        private int   _prevMillLogs = int.MinValue;
        private int   _prevMillPlanks = int.MinValue;
        private float _lastChangeSimTime = 0f;

        public void Tick(World world, int _, float dt)
        {
            _accum += dt;
            if (_accum < CHECK_EVERY_SEC) return;
            _accum = 0f;

            // 1) Money conservation
            int agentCoins = world.Agents.Sum(a => a.Coins);
            int escrowCoins = world.FoodBook.Bids.Where(b => b.Qty > 0).Sum(b => b.EscrowCoins);
            int totalCoins = agentCoins + escrowCoins;

            if (!_haveBaseline)
            {
                _baselineCoins = totalCoins;
                _haveBaseline  = true;
                Debug.Log($"[AUDIT] Baseline coins set: {totalCoins}");
            }
            else if (totalCoins != _baselineCoins)
            {
                int delta = totalCoins - _baselineCoins;
                Debug.LogError($"[AUDIT][MONEY] Non-conservation: now={totalCoins} vs baseline={_baselineCoins} (Δ={delta}).");
            }

            // Negative coins check
            foreach (var a in world.Agents)
                if (a.Coins < 0)
                    Debug.LogError($"[AUDIT][MONEY] Agent#{a.Id} has negative coins: {a.Coins}");
            foreach (var bid in world.FoodBook.Bids.Where(o=>o.Qty>0))
                if (bid.EscrowCoins < 0)
                    Debug.LogError($"[AUDIT][MONEY] Bid#{bid.Id} has negative escrow coins: {bid.EscrowCoins}");

            // 2) Item sanity (non-negative everywhere) + quick totals
            var itemTotals = new Dictionary<ItemType, int>();
            foreach (ItemType it in Enum.GetValues(typeof(ItemType))) itemTotals[it] = 0;

            // agents
            foreach (var a in world.Agents)
            {
                foreach (var kv in a.Carry.Items)
                {
                    if (kv.Value < 0)
                        Debug.LogError($"[AUDIT][ITEM] Agent#{a.Id} has negative {kv.Key}: {kv.Value}");
                    itemTotals[kv.Key] += Math.Max(0, kv.Value);
                }
            }
            // buildings
            foreach (var b in world.Buildings)
            {
                foreach (var kv in b.Storage.Items)
                {
                    if (kv.Value < 0)
                        Debug.LogError($"[AUDIT][ITEM] Building#{b.Id} has negative {kv.Key}: {kv.Value}");
                    itemTotals[kv.Key] += Math.Max(0, kv.Value);
                }
            }
            // asks escrow (Food only right now)
            int escrowFood = world.FoodBook.Asks.Where(o => o.Item == ItemType.Food && o.Qty > 0).Sum(o => o.EscrowItems);
            if (escrowFood < 0) Debug.LogError($"[AUDIT][ITEM] Ask escrow negative for Food: {escrowFood}");
            itemTotals[ItemType.Food] += Math.Max(0, escrowFood);

            if (VERBOSE_OK_SUMMARY)
            {
                string itemsLine = string.Join(", ", itemTotals.Select(kv=>$"{kv.Key}:{kv.Value}"));
                Debug.Log($"[AUDIT][OK] coins={totalCoins} items[{itemsLine}]");
            }

            // 3) Stuck detection (forest→mill→planks)
            int forestStock = world.ResourceNodes.Count > 0 ? world.ResourceNodes[0].Stock : 0;
            int millLogs    = world.Buildings.Count > 0 ? world.Buildings[0].Storage.Get(ItemType.Log)   : 0;
            int millPlanks  = world.Buildings.Count > 0 ? world.Buildings[0].Storage.Get(ItemType.Plank) : 0;

            bool changed = (forestStock != _prevForestStock) || (millLogs != _prevMillLogs) || (millPlanks != _prevMillPlanks);
            if (changed)
            {
                _prevForestStock = forestStock;
                _prevMillLogs    = millLogs;
                _prevMillPlanks  = millPlanks;
                _lastChangeSimTime = world.SimTime;
            }
            else
            {
                float idleFor = (float)(world.SimTime - _lastChangeSimTime);
                bool haveLoggers = world.Agents.Any(a => a.Role == JobRole.Logger);
                if (idleFor >= STUCK_WINDOW_SEC && haveLoggers)
                {
                    Debug.LogWarning($"[AUDIT][STUCK] No production change for ~{idleFor:F1}s " +
                                     $"(forest={forestStock}, millLogs={millLogs}, planks={millPlanks}).");
                    // reset the timer so we don't spam every check
                    _lastChangeSimTime = (float)world.SimTime;
                }
            }
        }
    }
}
