using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PortTown01.Core;
using PortTown01.Econ;

namespace PortTown01.Systems
{
    // Verifies conservation of money/items given external sinks/sources,
    // checks negatives, and detects "stuck" production.
    public class AuditSystem : ISimSystem
    {
        public string Name => "Audit";

        // --- Tunables ---
        private const float CHECK_EVERY_SEC    = 1.0f; // kept for readability; we gate by tick
        private const float STUCK_WINDOW_SEC   = 10.0f;
        private const bool  VERBOSE_OK_SUMMARY = false;

        // --- Baseline (for info only) ---
        private bool _haveBaseline = false;
        private long _baselineCoins = 0; // includes agents + escrow + CityBudget at t=0

        // --- Incremental reconciliation state ---
        private long _prevTotalMoney = long.MinValue; // agents + escrow + CityBudget at last audit step
        private long _prevInflow     = 0;             // CoinsExternalInflow at last audit step
        private long _prevOutflow    = 0;             // CoinsExternalOutflow at last audit step

        // --- NEW: Per-component deltas (agents / escrow) ---
        private int _prevAgentCoinsSum  = int.MinValue;
        private int _prevEscrowCoinsSum = int.MinValue;

        // --- Stuck detection snapshot ---
        private int   _prevForestStock   = int.MinValue;
        private int   _prevMillLogs      = int.MinValue;
        private int   _prevMillPlanks    = int.MinValue;
        private float _lastChangeSimTime = 0f;

        public void Tick(World world, int tick, float dt)
        {
            // Run exactly once per second on shared cadence
            if (!SimTicks.Every1Hz(tick)) return;

            // ---------- 1) MONEY CONSERVATION (incremental) ----------
            int agentCoins  = world.Agents.Sum(a => a.Coins);
            int escrowCoins = world.FoodBook.Bids.Where(b => b.Qty > 0).Sum(b => b.EscrowCoins);
            int cityCoins   = world.CityBudget; // INT; includes unallocated city funds

            // Per-step deltas for components
            int dAgents = (_prevAgentCoinsSum  == int.MinValue) ? 0 : (agentCoins  - _prevAgentCoinsSum);
            int dEscrow = (_prevEscrowCoinsSum == int.MinValue) ? 0 : (escrowCoins - _prevEscrowCoinsSum);

            long currentTotal = (long)agentCoins + escrowCoins + cityCoins;
            long inflow       = world.CoinsExternalInflow;
            long outflow      = world.CoinsExternalOutflow;

            long expectedNow;
            if (_prevTotalMoney == long.MinValue)
            {
                // First observation establishes the baseline snapshots
                _prevTotalMoney = currentTotal;
                _prevInflow     = inflow;
                _prevOutflow    = outflow;

                _baselineCoins  = currentTotal;
                _haveBaseline   = true;
                expectedNow     = currentTotal;

                Debug.Log($"[AUDIT] Baseline coins set: {currentTotal} (agents={agentCoins}, escrow={escrowCoins}, city={cityCoins})");
            }
            else
            {
                long dIn  = inflow  - _prevInflow;
                long dOut = outflow - _prevOutflow;

                expectedNow = _prevTotalMoney + (dIn - dOut);
                long delta  = currentTotal - expectedNow;

                if (Mathf.Abs((int)delta) >= 2) // allow ±1 wiggle for integer rounding elsewhere
                {
                    Debug.LogError(
                        $"[AUDIT][MONEY] Non-conservation: now={currentTotal} vs expected={expectedNow} (Δ={delta}). " +
                        $"components: agents={agentCoins} (ΔA={dAgents}), escrow={escrowCoins} (ΔE={dEscrow}), city={cityCoins}; " +
                        $"step Δin={dIn}, Δout={dOut}, prevTotal={_prevTotalMoney}, inflowCum={inflow}, outflowCum={outflow}");
                    world.MoneyResidualAbsMax = Mathf.Max(world.MoneyResidualAbsMax, Mathf.Abs((int)delta));
                }

                // advance snapshots
                _prevTotalMoney = currentTotal;
                _prevInflow     = inflow;
                _prevOutflow    = outflow;
            }

            // advance per-component snapshots (do this every tick, including first)
            _prevAgentCoinsSum  = agentCoins;
            _prevEscrowCoinsSum = escrowCoins;

            if (VERBOSE_OK_SUMMARY)
            {
                Debug.Log($"[AUDIT][OK] moneyNow={currentTotal}, expectedNow={expectedNow}, " +
                          $"agents={agentCoins}, escrow={escrowCoins}, city={cityCoins}");
            }

            // ---------- 2) NEGATIVE COIN CHECKS ----------
            foreach (var a in world.Agents)
            {
                if (a.Coins < 0)
                    Debug.LogError($"[AUDIT][MONEY] Agent#{a.Id} has negative coins: {a.Coins}");
            }
            foreach (var bid in world.FoodBook.Bids.Where(o => o.Qty > 0))
            {
                if (bid.EscrowCoins < 0)
                    Debug.LogError($"[AUDIT][MONEY] Bid#{bid.Id} has negative escrow coins: {bid.EscrowCoins}");
            }

            // ---------- 3) ITEM CONSERVATION / NEGATIVES ----------
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
            int escrowFood = world.FoodBook.Asks.Where(o => o.Item == ItemType.Food && o.Qty > 0)
                                                .Sum(o => o.EscrowItems);
            if (escrowFood < 0)
                Debug.LogError($"[AUDIT][ITEM] Ask escrow negative for Food: {escrowFood}");
            itemTotals[ItemType.Food] += Math.Max(0, escrowFood);

            // ---------- 4) STUCK DETECTION ----------
            int forestStock = world.ResourceNodes.Count > 0 ? world.ResourceNodes[0].Stock : 0;
            int millLogs    = world.Buildings.Count > 0 ? world.Buildings[0].Storage.Get(ItemType.Log)   : 0;
            int millPlanks  = world.Buildings.Count > 0 ? world.Buildings[0].Storage.Get(ItemType.Plank) : 0;

            bool changed = (forestStock != _prevForestStock) ||
                           (millLogs    != _prevMillLogs)    ||
                           (millPlanks  != _prevMillPlanks);

            if (changed)
            {
                _prevForestStock   = forestStock;
                _prevMillLogs      = millLogs;
                _prevMillPlanks    = millPlanks;
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
                    _lastChangeSimTime = (float)world.SimTime;
                }
            }
        }
    }
}
