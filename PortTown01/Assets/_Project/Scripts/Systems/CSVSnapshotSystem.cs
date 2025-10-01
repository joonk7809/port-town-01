using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    /// <summary>
    /// Per-second aggregate CSV snapshots with money integrity fields.
    /// Writes to Application.persistentDataPath/Snapshots/Aggregate.csv
    /// Columns are stable; append-only.
    /// </summary>
    public sealed class CSVSnapshotSystem : ISimSystem
    {
        public string Name => "CSV";

        private const string DIR  = "Snapshots";
        private const string FILE = "Aggregate.csv";

        // Money reconciliation baselines (align with AuditSystem)
        private long _prevTotalMoney = long.MinValue;
        private long _prevInflow;   // world.CoinsExternalInflow (cumulative)
        private long _prevOutflow;  // world.CoinsExternalOutflow (cumulative)

        public void Tick(World world, int tick, float dt)
        {
            if (!SimTicks.Every1Hz(tick)) return;

            // --- Money pools (integer coins) ---
            int agentCoins  = world.Agents.Sum(a => a.Coins);
            int escrowCoins = world.FoodBook?.Bids.Where(b => b.Qty > 0).Sum(b => b.EscrowCoins) ?? 0;
            int cityCoins   = world.CityBudget;
            long totalMoney = (long)agentCoins + escrowCoins + cityCoins;

            long inflow  = world.CoinsExternalInflow;
            long outflow = world.CoinsExternalOutflow;

            long expectedNow;
            long residual;

            if (_prevTotalMoney == long.MinValue)
            {
                // establish baselines on first sample
                _prevTotalMoney = totalMoney;
                _prevInflow     = inflow;
                _prevOutflow    = outflow;
                expectedNow     = totalMoney;
                residual        = 0;
            }
            else
            {
                long dIn  = inflow  - _prevInflow;
                long dOut = outflow - _prevOutflow;
                expectedNow = _prevTotalMoney + (dIn - dOut);
                residual    = totalMoney - expectedNow;

                // roll baselines
                _prevTotalMoney = totalMoney;
                _prevInflow     = inflow;
                _prevOutflow    = outflow;
            }

            // --- Commodity/throughput context (safe lookups) ---
            int foodPrice  = Mathf.Max(1, world.FoodPrice);
            int cratePrice = Mathf.Max(1, world.CratePrice);

            var vendor = world.Agents.FirstOrDefault(a => a.IsVendor);
            int vendorInvFood = vendor != null ? vendor.Carry.Get(ItemType.Food) : 0;
            int vendorEscFood = (vendor != null && world.FoodBook != null)
                ? world.FoodBook.Asks.Where(o => o.AgentId == vendor.Id && o.Qty > 0).Sum(o => o.EscrowItems)
                : 0;

            var mill = world.Buildings.FirstOrDefault(b => b.Type == BuildingType.Mill);
            int millLogs   = mill != null ? mill.Storage.Get(ItemType.Log)   : 0;
            int millPlanks = mill != null ? mill.Storage.Get(ItemType.Plank) : 0;
            int millCrates = mill != null ? mill.Storage.Get(ItemType.Crate) : 0;

            // Queue proxy: people within ~2.5m of stall
            var stall = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.Trading);
            int stallQueueCount = 0;
            if (stall != null)
            {
                var sPos = stall.StationPos;
                stallQueueCount = world.Agents.Count(a => Vector3.Distance(a.Pos, sPos) <= 2.5f);
            }

            // --- Write CSV ---
            try
            {
                var dir = Path.Combine(Application.persistentDataPath, DIR);
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, FILE);

                bool writeHeader = !File.Exists(path);
                using (var sw = new StreamWriter(path, append: true, Encoding.UTF8))
                {
                    if (writeHeader)
                    {
                        sw.WriteLine(string.Join(",",
                            // identifiers
                            "timestampUtc","tick","simTime",
                            // money integrity (pools + residual)
                            "totalMoney","agentsCoins","escrowCoins","cityCoins","residualMoney",
                            // external flows (cumulative)
                            "coinsExternalInflow","coinsExternalOutflow",
                            // prices
                            "foodPrice","cratePrice",
                            // stocks (selected)
                            "vendorInvFood","vendorEscrowFood",
                            "millLogs","millPlanks","millCrates",
                            // activity totals
                            "foodSold","cratesSold","vendorRevenue","dockRevenue","wagesHaul",
                            // service proxy
                            "stallQueueCount"
                        ));
                    }

                    sw.WriteLine(string.Join(",",
                        DateTime.UtcNow.ToString("o"),
                        tick,
                        world.SimTime.ToString("F1"),

                        totalMoney,
                        agentCoins,
                        escrowCoins,
                        cityCoins,
                        residual,

                        inflow,
                        outflow,

                        foodPrice,
                        cratePrice,

                        vendorInvFood,
                        vendorEscFood,

                        millLogs,
                        millPlanks,
                        millCrates,

                        world.FoodSold,
                        world.CratesSold,
                        world.VendorRevenue,
                        world.RevenueDock,
                        world.WagesHaul,

                        stallQueueCount
                    ));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[CSV] Failed to write Aggregate.csv: " + ex.Message);
            }
        }
    }
}
