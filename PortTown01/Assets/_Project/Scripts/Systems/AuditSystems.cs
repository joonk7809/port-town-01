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
        private const float STUCK_WINDOW_SEC = 10.0f;
        private static readonly bool VERBOSE_OK_SUMMARY = false; // runtime flag; avoids CS0162
        private const int   MONEY_RESIDUAL_TOLERANCE = 0; // integer coins; 0 tolerance expected

        // --- Incremental reconciliation state (money) ---
        // agents + escrow + CityBudget at last audit step
        private long _prevTotalMoney = long.MinValue;
        // external mint/burn at last audit step
        private long _prevInflow  = 0;
        private long _prevOutflow = 0;

        // --- Per-pool snapshots to compute component deltas (ΔA/ΔE/ΔC) ---
        private int _prevAgentCoinsSum  = int.MinValue;
        private int _prevEscrowCoinsSum = int.MinValue;
        private int _prevCityCoinsSum   = int.MinValue;

        // --- Stuck detection snapshot ---
        private int    _prevForestStock = int.MinValue;
        private int    _prevMillLogs    = int.MinValue;
        private int    _prevMillPlanks  = int.MinValue;
        private double _lastChangeSimTime = 0.0;

        public void Tick(World world, int tick, float dt)
        {
            // Run exactly once per second on shared cadence
            if (!SimTicks.Every1Hz(tick)) return;

            // ---------- 1) MONEY CONSERVATION (incremental, integer-coins) ----------

            // Pool tallies
            int agentCoins  = world.Agents.Sum(a => a.Coins);
            int escrowCoins = world.FoodBook.Bids.Where(b => b.Qty > 0).Sum(b => b.EscrowCoins); // coin escrow is bid-side
            int cityCoins   = world.CityBudget;

            // Component deltas (first observed step => 0)
            int dA = (_prevAgentCoinsSum  == int.MinValue) ? 0 : (agentCoins  - _prevAgentCoinsSum);
            int dE = (_prevEscrowCoinsSum == int.MinValue) ? 0 : (escrowCoins - _prevEscrowCoinsSum);
            int dC = (_prevCityCoinsSum   == int.MinValue) ? 0 : (cityCoins   - _prevCityCoinsSum);

            long currentTotal = (long)agentCoins + (long)escrowCoins + (long)cityCoins;
            long inflow       = world.CoinsExternalInflow;
            long outflow      = world.CoinsExternalOutflow;

            long expectedNow;
            if (_prevTotalMoney == long.MinValue)
            {
                // Establish baselines on first observation
                _prevTotalMoney = currentTotal;
                _prevInflow     = inflow;
                _prevOutflow    = outflow;

                expectedNow     = currentTotal;

                Debug.Log($"[AUDIT] Baseline coins set: total={currentTotal} (agents={agentCoins}, escrow={escrowCoins}, city={cityCoins})");
            }
            else
            {
                long dIn  = inflow  - _prevInflow;
                long dOut = outflow - _prevOutflow;

                expectedNow = _prevTotalMoney + (dIn - dOut);
                long residual = currentTotal - expectedNow;

                if (Math.Abs(residual) > MONEY_RESIDUAL_TOLERANCE)
                {
                    Debug.LogError(
                        $"[AUDIT][MONEY] Non-conservation: now={currentTotal} expected={expectedNow} residual={residual} @tick {tick} | " +
                        $"ΔA={dA}, ΔE={dE}, ΔC={dC} | step Δin={dIn}, Δout={dOut} | prevTotal={_prevTotalMoney}, inflowCum={inflow}, outflowCum={outflow}");
                    // Track worst absolute residual in World (int-bounded)
                    int absRes = (int)Math.Min(int.MaxValue, Math.Abs(residual));
                    world.MoneyResidualAbsMax = Math.Max(world.MoneyResidualAbsMax, absRes);
                }

                // Advance snapshots
                _prevTotalMoney = currentTotal;
                _prevInflow     = inflow;
                _prevOutflow    = outflow;
            }

            // Advance per-pool snapshots (always, including baseline tick)
            _prevAgentCoinsSum  = agentCoins;
            _prevEscrowCoinsSum = escrowCoins;
            _prevCityCoinsSum   = cityCoins;

            if (VERBOSE_OK_SUMMARY)
            {
                Debug.Log($"[AUDIT][OK] moneyNow={currentTotal} " +
                          $"(agents={agentCoins}, escrow={escrowCoins}, city={cityCoins})");
            }

            // ---------- 2) NEGATIVE COIN CHECKS ----------
            if (cityCoins < 0)
                Debug.LogError($"[AUDIT][MONEY] CityBudget negative: {cityCoins}");

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

            // ---------- 3) ITEM NEGATIVES + ESCROW (type-aware, no cross-type conservation) ----------
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

            // Optionally expose a simple "worst negative seen" KPI for items (no cross-type conservation here)
            // world.ItemResidualAbsMax = Math.Max(world.ItemResidualAbsMax, 0); // hook when you add it

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
                double idleFor = world.SimTime - _lastChangeSimTime;
                bool haveLoggers = world.Agents.Any(a => a.Role == JobRole.Logger);
                if (idleFor >= STUCK_WINDOW_SEC && haveLoggers)
                {
                    Debug.LogWarning($"[AUDIT][STUCK] No production change for ~{idleFor:F1}s " +
                                     $"(forest={forestStock}, millLogs={millLogs}, planks={millPlanks}).");
                    _lastChangeSimTime = world.SimTime;
                }
            }
        }
    }
}
