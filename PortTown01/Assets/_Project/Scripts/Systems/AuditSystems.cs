using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PortTown01.Core;
using PortTown01.Econ;

namespace PortTown01.Systems
{
    // Verifies conservation of money/items given external sinks/sources,
    // checks negatives, and detects "stuck" production (quiet, reasoned).
    public class AuditSystem : ISimSystem
    {
        public string Name => "Audit";

        // --- Tunables ---
        private const float STUCK_WINDOW_SEC = 10.0f;
        private const int   MONEY_RESIDUAL_TOLERANCE = 0; // integer coins; 0 tolerance expected

        // --- Incremental reconciliation state (money) ---
        private long _prevTotalMoney = long.MinValue; // agents + escrow + CityBudget at last audit step
        private long _prevInflow  = 0;                // external mint
        private long _prevOutflow = 0;                // external burn

        // --- Per-pool snapshots to compute component deltas (ΔA/ΔE/ΔC) ---
        private int _prevAgentCoinsSum  = int.MinValue;
        private int _prevEscrowCoinsSum = int.MinValue;
        private int _prevCityCoinsSum   = int.MinValue;

        // --- Stuck detection snapshot ---
        private int    _prevForestStock = int.MinValue;
        private int    _prevMillLogs    = int.MinValue;
        private int    _prevMillPlanks  = int.MinValue;
        private double _lastChangeSimTime = 0.0;
        private bool   _stuckEventNoted = false; // log once per "no-change" episode until flow resumes

        // Optional verbose logger compiled only if AUDIT_VERBOSE is defined
        [System.Diagnostics.Conditional("AUDIT_VERBOSE")]
        private static void V(string msg) => UnityEngine.Debug.Log(msg);

        public void Tick(World world, int tick, float dt)
        {
            // Run exactly once per second on shared cadence
            if (!SimTicks.Every1Hz(tick)) return;

            // ---------- 1) MONEY CONSERVATION (incremental, integer-coins) ----------
            int agentCoins  = world.Agents.Sum(a => a.Coins);
            int escrowCoins = world.FoodBook?.Bids.Where(b => b.Qty > 0).Sum(b => b.EscrowCoins) ?? 0;
            int cityCoins   = world.CityBudget;

            int dA = (_prevAgentCoinsSum  == int.MinValue) ? 0 : (agentCoins  - _prevAgentCoinsSum);
            int dE = (_prevEscrowCoinsSum == int.MinValue) ? 0 : (escrowCoins - _prevEscrowCoinsSum);
            int dC = (_prevCityCoinsSum   == int.MinValue) ? 0 : (cityCoins   - _prevCityCoinsSum);

            long currentTotal = (long)agentCoins + (long)escrowCoins + (long)cityCoins;
            long inflow       = world.CoinsExternalInflow;
            long outflow      = world.CoinsExternalOutflow;

            if (_prevTotalMoney == long.MinValue)
            {
                _prevTotalMoney = currentTotal;
                _prevInflow     = inflow;
                _prevOutflow    = outflow;
                V($"[AUDIT] Baseline coins set: total={currentTotal} (agents={agentCoins}, escrow={escrowCoins}, city={cityCoins})");
            }
            else
            {
                long dIn  = inflow  - _prevInflow;
                long dOut = outflow - _prevOutflow;

                long expectedNow = _prevTotalMoney + (dIn - dOut);
                long residual    = currentTotal - expectedNow;

                if (Math.Abs(residual) > MONEY_RESIDUAL_TOLERANCE)
                {
                    UnityEngine.Debug.LogError(
                        $"[AUDIT][MONEY] Non-conservation: now={currentTotal} expected={expectedNow} residual={residual} @tick {tick} | " +
                        $"ΔA={dA}, ΔE={dE}, ΔC={dC} | step Δin={dIn}, Δout={dOut} | prevTotal={_prevTotalMoney}, inflowCum={inflow}, outflowCum={outflow}");
                    int absRes = (int)Math.Min(int.MaxValue, Math.Abs(residual));
                    world.MoneyResidualAbsMax = Math.Max(world.MoneyResidualAbsMax, absRes);
                }

                _prevTotalMoney = currentTotal;
                _prevInflow     = inflow;
                _prevOutflow    = outflow;
            }

            _prevAgentCoinsSum  = agentCoins;
            _prevEscrowCoinsSum = escrowCoins;
            _prevCityCoinsSum   = cityCoins;

            // ---------- 2) NEGATIVE COIN CHECKS ----------
            if (cityCoins < 0) UnityEngine.Debug.LogError($"[AUDIT][MONEY] CityBudget negative: {cityCoins}");
            foreach (var a in world.Agents)
                if (a.Coins < 0)
                    UnityEngine.Debug.LogError($"[AUDIT][MONEY] Agent#{a.Id} has negative coins: {a.Coins}");
            foreach (var bid in world.FoodBook?.Bids.Where(o => o.Qty > 0) ?? Enumerable.Empty<Offer>())
                if (bid.EscrowCoins < 0)
                    UnityEngine.Debug.LogError($"[AUDIT][MONEY] Bid#{bid.Id} has negative escrow coins: {bid.EscrowCoins}");

            // ---------- 3) ITEM NEGATIVES + ESCROW ----------
            var itemTotals = new Dictionary<ItemType, int>();
            foreach (ItemType it in Enum.GetValues(typeof(ItemType))) itemTotals[it] = 0;

            foreach (var a in world.Agents)
            {
                foreach (var kv in a.Carry.Items)
                {
                    if (kv.Value < 0)
                        UnityEngine.Debug.LogError($"[AUDIT][ITEM] Agent#{a.Id} has negative {kv.Key}: {kv.Value}");
                    itemTotals[kv.Key] += Math.Max(0, kv.Value);
                }
            }
            foreach (var b in world.Buildings)
            {
                foreach (var kv in b.Storage.Items)
                {
                    if (kv.Value < 0)
                        UnityEngine.Debug.LogError($"[AUDIT][ITEM] Building#{b.Id} has negative {kv.Key}: {kv.Value}");
                    itemTotals[kv.Key] += Math.Max(0, kv.Value);
                }
            }
            int escrowFood = world.FoodBook?.Asks.Where(o => o.Item == ItemType.Food && o.Qty > 0).Sum(o => o.EscrowItems) ?? 0;
            if (escrowFood < 0)
                UnityEngine.Debug.LogError($"[AUDIT][ITEM] Ask escrow negative for Food: {escrowFood}");
            itemTotals[ItemType.Food] += Math.Max(0, escrowFood);

            // ---------- 4) STUCK DETECTION (reasoned, once-per-episode) ----------
            int forestStock = world.ResourceNodes.Count > 0 ? world.ResourceNodes[0].Stock : 0;
            int millLogs    = world.Buildings.Count  > 0 ? world.Buildings[0].Storage.Get(ItemType.Log)   : 0;
            int millPlanks  = world.Buildings.Count  > 0 ? world.Buildings[0].Storage.Get(ItemType.Plank) : 0;

            bool changed = (forestStock != _prevForestStock) ||
                           (millLogs    != _prevMillLogs)    ||
                           (millPlanks  != _prevMillPlanks);

            if (changed)
            {
                _prevForestStock   = forestStock;
                _prevMillLogs      = millLogs;
                _prevMillPlanks    = millPlanks;
                _lastChangeSimTime = world.SimTime;
                _stuckEventNoted   = false; // new flow; next stall should log again once
            }
            else
            {
                double idleFor = world.SimTime - _lastChangeSimTime;
                if (idleFor >= STUCK_WINDOW_SEC)
                {
                    bool haveLoggers = world.Agents.Any(a => a.Role == JobRole.Logger);
                    var forestNode   = world.ResourceNodes.Count > 0 ? world.ResourceNodes[0] : null;

                    if (haveLoggers && !_stuckEventNoted)
                    {
                        if (forestNode != null && forestNode.Stock <= 0)
                        {
                            if (forestNode.RegenPerSec <= 0f)
                                UnityEngine.Debug.LogWarning("[AUDIT][STALL] Forest exhausted (RegenPerSec=0). Production halted by design until configuration changes.");
                            else
                                UnityEngine.Debug.Log($"[AUDIT][STALL] Upstream empty; waiting for regen (RegenPerSec={forestNode.RegenPerSec:F2}).");

                            _stuckEventNoted = true; // log once for this no-change episode
                        }
                        else
                        {
                            UnityEngine.Debug.LogWarning($"[AUDIT][STUCK] No production change for ~{idleFor:F1}s (forest={forestStock}, millLogs={millLogs}, planks={millPlanks}).");
                            _stuckEventNoted = true;
                        }
                    }

                    // advance the window so we don't re-log every second even if _stuckEventNoted was false this time
                    _lastChangeSimTime = world.SimTime;
                }
            }
        }
    }
}
