using System.Linq;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    public class TelemetrySystem : ISimSystem
    {
        public string Name => "Telemetry";

        private float _accum = 0f;
        private const float EVERY = 1f;

        // for deltas
        private int _prevMillCrates = int.MinValue;
        private int _prevDockCoins  = int.MinValue;

        public void Tick(World world, int _, float dt)
        {
            _accum += dt;
            if (_accum < EVERY) return;
            _accum = 0f;

            int n = world.Agents.Count;
            float avgFood = n > 0 ? world.Agents.Average(a => a.Food) : 0f;
            float avgRest = n > 0 ? world.Agents.Average(a => a.Rest) : 0f;

            // Forest & Mill
            int forestStock = world.ResourceNodes.FirstOrDefault()?.Stock ?? 0;
            var mill = world.Buildings.FirstOrDefault(b => b.Type == BuildingType.Mill);
            int millLogs   = mill?.Storage.Get(ItemType.Log)   ?? 0;
            int millPlanks = mill?.Storage.Get(ItemType.Plank) ?? 0;
            int millCrates = mill?.Storage.Get(ItemType.Crate) ?? 0;

            // Vendor & order book
            var vendor = world.Agents.FirstOrDefault(a => a.IsVendor);
            int vendorInv   = vendor != null ? vendor.Carry.Get(ItemType.Food) : 0;
            int vendorEsc   = vendor != null ? world.FoodBook.Asks.Where(o => o.AgentId == vendor.Id && o.Qty > 0).Sum(o => o.EscrowItems) : 0;
            int vendorCoins = vendor?.Coins ?? 0;
            int bids = world.FoodBook.Bids.Count(o => o.Qty > 0);
            int asks = world.FoodBook.Asks.Count(o => o.Qty > 0);
            int bestBid = world.FoodBook.Bids.Where(o => o.Qty > 0).Select(o => o.UnitPrice).DefaultIfEmpty(0).Max();
            int bestAsk = world.FoodBook.Asks.Where(o => o.Qty > 0).Select(o => o.UnitPrice).DefaultIfEmpty(0).Min();

            // Boss & Dock buyer (heuristics)
            var boss = world.Agents.Where(a => !a.IsVendor && a.IsEmployer).FirstOrDefault()
                       ?? world.Agents.Where(a => !a.IsVendor).OrderByDescending(a => a.Coins).FirstOrDefault();
            int bossCoins = boss?.Coins ?? 0;

            var dock = world.Buildings.FirstOrDefault(b => b.Type == BuildingType.Dock);
            // "dock buyer" = stationary non-vendor near dock
            var dockBuyer = world.Agents
                .Where(a => !a.IsVendor && a.SpeedMps == 0f)
                .OrderBy(a => dock == null ? 9999f : Vector3.Distance(a.Pos, dock.Pos))
                .FirstOrDefault();
            int dockCoins = dockBuyer?.Coins ?? 0;

            // workforce
            int loggers = world.Agents.Count(a => a.Role == JobRole.Logger);
            int workingLoggers = world.Agents.Count(a => a.Role == JobRole.Logger && a.Phase == DayPhase.Work);
            int haulers = world.Agents.Count(a => a.Role == JobRole.Hauler);
            int workingHaulers = world.Agents.Count(a => a.Role == JobRole.Hauler && a.Phase == DayPhase.Work);

            // money conservation (agents + bid escrow)
            int agentCoins = world.Agents.Sum(a => a.Coins);
            int bidEscrow  = world.FoodBook.Bids.Where(b => b.Qty > 0).Sum(b => b.EscrowCoins);
            int totalCoins = agentCoins + bidEscrow;

            // deltas
            int dCrates = (_prevMillCrates == int.MinValue) ? 0 : millCrates - _prevMillCrates;
            _prevMillCrates = millCrates;

            int coinsOutDock = (_prevDockCoins == int.MinValue) ? 0 : (_prevDockCoins - dockCoins);
            _prevDockCoins = dockCoins;
            int shippedCrates = EconDefs.CRATE_PRICE > 0 ? coinsOutDock / EconDefs.CRATE_PRICE : 0;

            // Cumulative ledger from World
            int cratesSold = world.CratesSold;
            int revDock    = world.RevenueDock;
            int wagesHaul  = world.WagesHaul;
            int profit     = revDock - wagesHaul; // simple profit (no other costs yet)
            
            int foodPrice  = world.FoodPrice;
            int cratePrice = world.CratePrice;


            Debug.Log(
                $"[TEL] t={world.SimTime:F1}s tick={world.Tick} agents={n} " +
                $"avgFood={avgFood:F1} avgRest={avgRest:F1} items={millLogs + millPlanks} " +
                $"forestStock={forestStock} millLogs={millLogs} millPlanks={millPlanks} " +
                $"millCrates={millCrates}(d={dCrates}) " +
                $"vendorInv={vendorInv} vendorEscrow={vendorEsc} vendorCoins={vendorCoins} " +
                $"bids={bids} asks={asks} bestBid={bestBid} bestAsk={bestAsk} " +
                $"bossCoins={bossCoins} dockCoins={dockCoins} totalCoins={totalCoins} " +
                $"loggers={loggers}/{workingLoggers} haulers={haulers}/{workingHaulers} " +
                $"cratesShipped~={shippedCrates}" + 
                $" cratesSold={cratesSold} revDock={revDock} wagesHaul={wagesHaul} profit={profit}" +
                $" foodPrice={foodPrice} cratePrice={cratePrice}"
            );
        }
    }
}
