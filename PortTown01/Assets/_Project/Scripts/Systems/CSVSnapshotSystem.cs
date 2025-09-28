using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Writes 1 CSV line per second with key run metrics to Application.persistentDataPath/Snapshots
    public class CSVSnapshotSystem : ISimSystem
    {
        public string Name => "CSVSnapshot";

        const float SAMPLE_EVERY_SEC = 1f;
        const float DAY_SECONDS = 600f;
        const float START_HOUR  = 9f; // keep in sync with DayPlan

        private float _accum = 0f;
        private string _filePath;
        private bool _wroteHeader = false;

        private int _prevForest = int.MinValue;
        private int _prevMillLogs = int.MinValue;
        private int _prevMillPlanks = int.MinValue;

        public void Tick(World world, int _, float dt)
        {
            _accum += dt;
            if (_accum < SAMPLE_EVERY_SEC) return;
            _accum = 0f;

            EnsureFile();

            // --- metrics ---
            float sim = world.SimTime;
            float daySec = (float)((sim + (START_HOUR/24f)*DAY_SECONDS) % DAY_SECONDS);
            int hh = Mathf.FloorToInt(daySec / DAY_SECONDS * 24f);
            int mm = Mathf.FloorToInt(((daySec / DAY_SECONDS * 24f) - hh) * 60f);

            int agents = world.Agents.Count;
            float avgFood = agents > 0 ? world.Agents.Average(a => a.Food) : 0f;
            float avgRest = agents > 0 ? world.Agents.Average(a => a.Rest) : 0f;

            int forestStock = world.ResourceNodes.Count > 0 ? world.ResourceNodes[0].Stock : 0;
            int millLogs    = world.Buildings.Count    > 0 ? world.Buildings[0].Storage.Get(ItemType.Log)   : 0;
            int millPlanks  = world.Buildings.Count    > 0 ? world.Buildings[0].Storage.Get(ItemType.Plank) : 0;

            int dForest   = (_prevForest == int.MinValue)    ? 0 : forestStock - _prevForest;
            int dMillLogs = (_prevMillLogs == int.MinValue)  ? 0 : millLogs - _prevMillLogs;
            int dPlanks   = (_prevMillPlanks == int.MinValue)? 0 : millPlanks - _prevMillPlanks;
            _prevForest = forestStock; _prevMillLogs = millLogs; _prevMillPlanks = millPlanks;

            int bids = world.FoodBook.Bids.Count(o => o.Qty > 0);
            int asks = world.FoodBook.Asks.Count(o => o.Qty > 0);
            int bestBid = world.FoodBook.Bids.Where(o=>o.Qty>0).Select(o=>o.UnitPrice).DefaultIfEmpty(0).Max();
            int bestAsk = world.FoodBook.Asks.Where(o=>o.Qty>0).Select(o=>o.UnitPrice).DefaultIfEmpty(0).Min();

            var vendor = world.Agents.FirstOrDefault(a => a.IsVendor);
            int vendorInv   = vendor != null ? vendor.Carry.Get(ItemType.Food) : 0;
            int vendorEsc   = (vendor != null) ? world.FoodBook.Asks.Where(o=>o.AgentId==vendor.Id && o.Item==ItemType.Food && o.Qty>0).Sum(o=>o.EscrowItems) : 0;
            int vendorCoins = vendor != null ? vendor.Coins : 0;
            int vendorForSale = vendorInv + vendorEsc;

            var boss = world.Agents.Where(a => !a.IsVendor).OrderByDescending(a => a.Coins).FirstOrDefault();
            int bossCoins = boss != null ? boss.Coins : 0;

            int agentCoins = world.Agents.Sum(a => a.Coins);
            int bidEscrow  = world.FoodBook.Bids.Where(b=>b.Qty>0).Sum(b => b.EscrowCoins);
            int totalCoins = agentCoins + bidEscrow;

            int loggers = world.Agents.Count(a => a.Role == JobRole.Logger);
            int workingLoggers = world.Agents.Count(a => a.Role == JobRole.Logger && a.Phase == DayPhase.Work);

            if (!_wroteHeader)
            {
                var header = "tick,sim_s,tod,agents,avgFood,avgRest," +
                             "forestStock,dForest,millLogs,dMillLogs,millPlanks,dPlanks," +
                             "bids,asks,bestBid,bestAsk," +
                             "vendorInv,vendorEscrow,vendorForSale,vendorCoins," +
                             "bossCoins,totalCoins,loggers,workingLoggers";
                System.IO.File.AppendAllText(_filePath, header + "\n", Encoding.UTF8);
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
                bids.ToString(inv),
                asks.ToString(inv),
                bestBid.ToString(inv),
                bestAsk.ToString(inv),
                vendorInv.ToString(inv),
                vendorEsc.ToString(inv),
                vendorForSale.ToString(inv),
                vendorCoins.ToString(inv),
                bossCoins.ToString(inv),
                totalCoins.ToString(inv),
                loggers.ToString(inv),
                workingLoggers.ToString(inv)
            );

            System.IO.File.AppendAllText(_filePath, line + "\n", Encoding.UTF8);
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

// for file access to CSV enter into terminal:
// open -R ~/Library/Application\ Support/DefaultCompany/PortTown01/Snapshots/ 
