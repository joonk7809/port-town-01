using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    public class CSVSnapshotSystem : ISimSystem
    {
        public string Name => "CSVSnapshot";

        const float SAMPLE_EVERY_SEC = 1f;
        const float DAY_SECONDS = 600f;
        const float START_HOUR  = 9f;

        private float _accum = 0f;
        private string _filePath;
        private bool _wroteHeader = false;

        private int _prevForest = int.MinValue;
        private int _prevMillLogs = int.MinValue;
        private int _prevMillPlanks = int.MinValue;
        private int _prevMillCrates = int.MinValue;
        private int _prevDockCoins = int.MinValue;

        public void Tick(World world, int _, float dt)
        {
            _accum += dt;
            if (_accum < SAMPLE_EVERY_SEC) return;
            _accum = 0f;

            EnsureFile();

            float sim = world.SimTime;
            float daySec = (float)((sim + (START_HOUR/24f)*DAY_SECONDS) % DAY_SECONDS);
            int hh = Mathf.FloorToInt(daySec / DAY_SECONDS * 24f);
            int mm = Mathf.FloorToInt(((daySec / DAY_SECONDS * 24f) - hh) * 60f);

            int agents = world.Agents.Count;
            float avgFood = agents > 0 ? world.Agents.Average(a => a.Food) : 0f;
            float avgRest = agents > 0 ? world.Agents.Average(a => a.Rest) : 0f;

            int forestStock = world.ResourceNodes.FirstOrDefault()?.Stock ?? 0;
            var mill = world.Buildings.FirstOrDefault(b => b.Type == BuildingType.Mill);
            int millLogs   = mill?.Storage.Get(ItemType.Log)   ?? 0;
            int millPlanks = mill?.Storage.Get(ItemType.Plank) ?? 0;
            int millCrates = mill?.Storage.Get(ItemType.Crate) ?? 0;

            int dForest   = (_prevForest   == int.MinValue) ? 0 : forestStock - _prevForest;
            int dMillLogs = (_prevMillLogs == int.MinValue) ? 0 : millLogs    - _prevMillLogs;
            int dPlanks   = (_prevMillPlanks==int.MinValue) ? 0 : millPlanks  - _prevMillPlanks;
            int dCrates   = (_prevMillCrates==int.MinValue) ? 0 : millCrates  - _prevMillCrates;
            _prevForest = forestStock; _prevMillLogs = millLogs; _prevMillPlanks = millPlanks; _prevMillCrates = millCrates;

            int bids = world.FoodBook.Bids.Count(o => o.Qty > 0);
            int asks = world.FoodBook.Asks.Count(o => o.Qty > 0);
            int bestBid = world.FoodBook.Bids.Where(o=>o.Qty>0).Select(o=>o.UnitPrice).DefaultIfEmpty(0).Max();
            int bestAsk = world.FoodBook.Asks.Where(o=>o.Qty>0).Select(o=>o.UnitPrice).DefaultIfEmpty(0).Min();

            var vendor = world.Agents.FirstOrDefault(a => a.IsVendor);
            int vendorInv   = vendor != null ? vendor.Carry.Get(ItemType.Food) : 0;
            int vendorEsc   = vendor != null ? world.FoodBook.Asks.Where(o=>o.AgentId==vendor.Id && o.Qty>0).Sum(o=>o.EscrowItems) : 0;
            int vendorCoins = vendor?.Coins ?? 0;
            int vendorForSale = vendorInv + vendorEsc;

            var boss = world.Agents.Where(a => !a.IsVendor && a.IsEmployer).FirstOrDefault()
                       ?? world.Agents.Where(a => !a.IsVendor).OrderByDescending(a => a.Coins).FirstOrDefault();
            int bossCoins = boss?.Coins ?? 0;

            var dock = world.Buildings.FirstOrDefault(b => b.Type == BuildingType.Dock);
            var dockBuyer = world.Agents
                .Where(a => !a.IsVendor && a.SpeedMps == 0f)
                .OrderBy(a => dock == null ? 9999f : Vector3.Distance(a.Pos, dock.Pos))
                .FirstOrDefault();
            int dockCoins = dockBuyer?.Coins ?? 0;

            // money
            int agentCoins = world.Agents.Sum(a => a.Coins);
            int bidEscrow  = world.FoodBook.Bids.Where(b=>b.Qty>0).Sum(b => b.EscrowCoins);
            int totalCoins = agentCoins + bidEscrow;

            // estimate shipped crates by dock coin outflow
            int coinsOutDock = (_prevDockCoins == int.MinValue) ? 0 : (_prevDockCoins - dockCoins);
            _prevDockCoins = dockCoins;
            int cratesShipped = EconDefs.CRATE_PRICE > 0 ? coinsOutDock / EconDefs.CRATE_PRICE : 0;

            int loggers = world.Agents.Count(a => a.Role == JobRole.Logger);
            int workingLoggers = world.Agents.Count(a => a.Role == JobRole.Logger && a.Phase == DayPhase.Work);
            int haulers = world.Agents.Count(a => a.Role == JobRole.Hauler);
            int workingHaulers = world.Agents.Count(a => a.Role == JobRole.Hauler && a.Phase == DayPhase.Work);

            int cratesSold = world.CratesSold;
            int revDock    = world.RevenueDock;
            int wagesHaul  = world.WagesHaul;
            int profit     = revDock - wagesHaul;

            int foodPrice  = world.FoodPrice;
            int cratePrice = world.CratePrice;  



            if (!_wroteHeader)
            {
                var header = "tick,sim_s,tod,agents,avgFood,avgRest," +
                             "forestStock,dForest,millLogs,dMillLogs,millPlanks,dPlanks,millCrates,dCrates," +
                             "bids,asks,bestBid,bestAsk," +
                             "vendorInv,vendorEscrow,vendorForSale,vendorCoins," +
                             "bossCoins,dockCoins,totalCoins," +
                             "cratesShipped,loggers,workingLoggers,haulers,workingHaulers" + 
                             "cratesSold,revDock,wagesHaul,profit" +
                             "foodPrice,cratePrice";
                File.AppendAllText(_filePath, header + "\n", Encoding.UTF8);
                Debug.Log($"[CSV] Snapshotting to: {_filePath}");
                _wroteHeader = true;
            }

            var inv = CultureInfo.InvariantCulture;
            string tod = $"{hh:D2}:{mm:D2}";

            var line = string.Join(",",
                world.Tick.ToString(inv),
                sim.ToString("F1", inv),
                tod,
                agents.ToString(inv),
                avgFood.ToString("F2", inv),
                avgRest.ToString("F2", inv),
                forestStock.ToString(inv),
                dForest.ToString(inv),
                millLogs.ToString(inv),
                dMillLogs.ToString(inv),
                millPlanks.ToString(inv),
                dPlanks.ToString(inv),
                millCrates.ToString(inv),
                dCrates.ToString(inv),
                bids.ToString(inv),
                asks.ToString(inv),
                bestBid.ToString(inv),
                bestAsk.ToString(inv),
                vendorInv.ToString(inv),
                vendorEsc.ToString(inv),
                vendorForSale.ToString(inv),
                vendorCoins.ToString(inv),
                bossCoins.ToString(inv),
                dockCoins.ToString(inv),
                totalCoins.ToString(inv),
                cratesShipped.ToString(inv),
                loggers.ToString(inv),
                workingLoggers.ToString(inv),
                haulers.ToString(inv),
                workingHaulers.ToString(inv),
                cratesSold.ToString(inv),
                revDock.ToString(inv),
                wagesHaul.ToString(inv),
                profit.ToString(inv)
            );
            File.AppendAllText(_filePath, line + "\n", Encoding.UTF8);
        }

        private void EnsureFile()
        {
            if (!string.IsNullOrEmpty(_filePath)) return;
            string dir = Path.Combine(Application.persistentDataPath, "Snapshots");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string ts = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _filePath = Path.Combine(dir, $"run_{ts}.csv");
        }
    }
}
