using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    /// <summary>
    /// Gatekeeper: emits a PASS/FAIL summary once per run after a fixed duration.
    /// Stage A scope:
    ///  - Monetary conservation (via AuditSystem worst residual).
    ///  - No negative states for coins/items.
    ///  - Service KPI: stall wait p95.
    ///  - Welfare KPI: starvation rate.
    ///  - Labor KPI: unemployment rate (approximate, distance-to-worksite heuristic).
    /// </summary>
    public sealed class GatekeeperSystem : ISimSystem
    {
        public string Name => "Gatekeeper";

        // --- Run length (sim seconds) ---
        private const float RUN_SECONDS = 600f;     // 10 minutes

        // --- Thresholds (PASS if all satisfied) ---
        private const float STARVE_MAX_PCT      = 3.0f;  // % of agent-seconds with Food < 20
        private const float UNEMPLOY_MAX_PCT    = 30.0f; // % of work-phase agent-seconds far from worksite
        private const float STALL_WAIT_P95_MAX  = 6.0f;  // seconds

        // --- Starvation thresholds (align with planner constants) ---
        private const float FOOD_STARVE_THRESH = 20f;    // Food < 20 -> starved

        // --- Distance heuristics ---
        private const float NEAR_WORK_METERS = 8f;       // within this is "at work"
        private const float QUEUE_RADIUS     = 2.5f;     // who is "in line" at stall

        // CSV output
        private const string SUMMARY_DIR  = "Snapshots";
        private const string SUMMARY_FILE = "SessionSummary.csv";

        private bool _reported;

        // Per-second sampling accumulators (updated at 1 Hz)
        private long _agentSeconds;           // denominator for starvation %
        private long _starvedAgentSeconds;    // numerator

        private long _workAgentSeconds;       // denominator for unemployment %
        private long _unemployedAgentSeconds; // numerator

        private readonly List<float> _stallWaitSamples = new List<float>(1024); // seconds

        public void Tick(World world, int tick, float dt)
        {
            // Sample KPIs once per second
            if (SimTicks.Every1Hz(tick))
            {
                SamplePerSecond(world);
            }

            if (_reported) return;
            if (!SimTicks.Every1Hz(tick)) return; // evaluate at 1 Hz cadence
            if (world.SimTime < RUN_SECONDS) return;

            // ---------- Monetary & item integrity ----------
            int agentCoins   = world.Agents.Sum(a => a.Coins);
            int escrowCoins  = world.FoodBook?.Bids.Where(b => b.Qty > 0).Sum(b => b.EscrowCoins) ?? 0;
            int cityCoins    = world.CityBudget;
            long totalMoney  = (long)agentCoins + escrowCoins + cityCoins;

            int worstMoneyResidual = world.MoneyResidualAbsMax;

            int negAgentCoins = world.Agents.Count(a => a.Coins < 0);
            int negBidEscrow  = world.FoodBook?.Bids.Count(o => o.Qty > 0 && o.EscrowCoins < 0) ?? 0;

            int negItemsAgents = 0;
            foreach (var a in world.Agents)
                foreach (var kv in a.Carry.Items)
                    if (kv.Value < 0) negItemsAgents++;

            int negItemsBuildings = 0;
            foreach (var b in world.Buildings)
                foreach (var kv in b.Storage.Items)
                    if (kv.Value < 0) negItemsBuildings++;

            int askEscrowFood = world.FoodBook?.Asks.Where(o => o.Item == ItemType.Food && o.Qty > 0)
                                                    .Sum(o => o.EscrowItems) ?? 0;
            bool negAskEscrowFood = askEscrowFood < 0;

            bool passMoney = worstMoneyResidual == 0 && negAgentCoins == 0 && negBidEscrow == 0 && cityCoins >= 0;
            bool passItems = (negItemsAgents == 0) && (negItemsBuildings == 0) && !negAskEscrowFood;

            // ---------- Service & welfare KPIs ----------
            float stallWaitP95 = Percentile(_stallWaitSamples, 0.95f);

            float starvePct   = _agentSeconds > 0
                ? (100f * _starvedAgentSeconds / Mathf.Max(1f, _agentSeconds))
                : 0f;

            float unemployPct = _workAgentSeconds > 0
                ? (100f * _unemployedAgentSeconds / Mathf.Max(1f, _workAgentSeconds))
                : 0f;

            bool passService = stallWaitP95 <= STALL_WAIT_P95_MAX;
            bool passWelfare = starvePct   <= STARVE_MAX_PCT;
            bool passLabor   = unemployPct <= UNEMPLOY_MAX_PCT;

            bool pass = passMoney && passItems && passService && passWelfare && passLabor;

            string verdict = pass ? "PASS" : "FAIL";
            Debug.LogFormat(
                "[GATE] {0} @t={1:F1}s | totalMoney={2} (agents={3}, escrow={4}, city={5}) | worstResidual={6} | " +
                "neg coins/items: agents={7}/{10}, bids={8}, buildings={11}, askFoodEscrowNeg={9} | " +
                "KPIs: stallWaitP95={12:F2}s (≤{13}), starve%={14:F2} (≤{15}), unemploy%={16:F2} (≤{17})",
                verdict, world.SimTime, totalMoney, agentCoins, escrowCoins, cityCoins, worstMoneyResidual,
                negAgentCoins, negBidEscrow, negAskEscrowFood ? 1 : 0, negItemsAgents, negItemsBuildings,
                stallWaitP95, STALL_WAIT_P95_MAX,
                starvePct,    STARVE_MAX_PCT,
                unemployPct,  UNEMPLOY_MAX_PCT
            );

            PersistCsvSummary(world, verdict, totalMoney, agentCoins, escrowCoins, cityCoins,
                              worstMoneyResidual, negAgentCoins, negBidEscrow, negItemsAgents,
                              negItemsBuildings, negAskEscrowFood, stallWaitP95, starvePct, unemployPct);

            _reported = true; // once per run
        }

        // ---------- per-second sampling ----------

        private void SamplePerSecond(World world)
        {
            // Starvation sample: count mobile, non-infrastructure agents
            int starved = 0;
            int activeAgents = 0;
            foreach (var a in world.Agents)
            {
                // treat immobile vendor/employer as infrastructure (skip starvation accounting for them)
                bool infra = a.IsVendor || a.IsEmployer;
                if (!infra) activeAgents++;
                if (!infra && a.Food < FOOD_STARVE_THRESH) starved++;
            }
            _agentSeconds        += activeAgents;
            _starvedAgentSeconds += starved;

            // Unemployment sample: work-phase agents far from a relevant worksite
            var forest = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.Logging);
            var mill   = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.Milling);
            var dock   = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.DockLoading);

            int workAgents = 0, notAtWork = 0;
            foreach (var a in world.Agents)
            {
                if (a.Phase != DayPhase.Work) continue;
                if (a.IsVendor || a.IsEmployer) continue; // not labor
                workAgents++;

                Vector3? targetPos = null;
                switch (a.Role)
                {
                    case JobRole.Logger: targetPos = forest?.StationPos; break;
                    case JobRole.Miller: targetPos = mill?.StationPos; break;
                    case JobRole.Hauler:
                        // if carrying crates, aim dock; else mill
                        int crates = a.Carry.Get(ItemType.Crate);
                        targetPos = (crates > 0 ? dock?.StationPos : mill?.StationPos);
                        break;
                    default:
                        // unknown role: approximate with nearest known worksite
                        targetPos = NearestOf(a.Pos, forest?.StationPos, mill?.StationPos, dock?.StationPos);
                        break;
                }

                float dist = targetPos.HasValue ? Vector3.Distance(a.Pos, targetPos.Value) : float.PositiveInfinity;
                if (!(dist <= NEAR_WORK_METERS)) notAtWork++;
            }
            _workAgentSeconds       += workAgents;
            _unemployedAgentSeconds += notAtWork;

            // Stall wait p95 sample proxy: others in line * service + remaining
            var stall = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.Trading);
            if (stall != null)
            {
                float service  = Mathf.Max(0.1f, stall.ServiceDurationSec);
                float remaining = Mathf.Max(0f,  stall.ServiceRemainingSec);

                int others = 0;
                Vector3 sPos = stall.StationPos;
                foreach (var ag in world.Agents)
                {
                    if (Vector3.Distance(ag.Pos, sPos) <= QUEUE_RADIUS) others++;
                }
                // If someone is at the counter, count them as being served; others-1 are waiting
                int waiting = Mathf.Max(0, others - 1);
                float eta = waiting * service + remaining;
                _stallWaitSamples.Add(eta);
            }
            else
            {
                _stallWaitSamples.Add(0f);
            }
        }

        // ---------- helpers ----------

        private static float Percentile(List<float> samples, float p)
        {
            if (samples == null || samples.Count == 0) return 0f;
            var arr = samples.ToArray();
            Array.Sort(arr);
            float pos = (arr.Length - 1) * Mathf.Clamp01(p);
            int lo = Mathf.FloorToInt(pos);
            int hi = Mathf.CeilToInt(pos);
            if (lo == hi) return arr[lo];
            float frac = pos - lo;
            return Mathf.Lerp(arr[lo], arr[hi], frac);
            // (deliberately not clearing samples; runs are short and this is end-of-run)
        }

        private static Vector3 NearestOf(Vector3 from, params Vector3?[] candidates)
        {
            float best = float.PositiveInfinity;
            Vector3 bestPos = from;
            foreach (var c in candidates)
            {
                if (!c.HasValue) continue;
                float d = Vector3.Distance(from, c.Value);
                if (d < best) { best = d; bestPos = c.Value; }
            }
            return bestPos;
        }

        private static void PersistCsvSummary(
            World world, string verdict, long totalMoney, int agentCoins, int escrowCoins, int cityCoins,
            int worstMoneyResidual, int negAgentCoins, int negBidEscrow, int negItemsAgents,
            int negItemsBuildings, bool negAskEscrowFood, float stallWaitP95, float starvePct, float unemployPct)
        {
            try
            {
                var dir = Path.Combine(Application.persistentDataPath, SUMMARY_DIR);
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, SUMMARY_FILE);

                bool writeHeader = !File.Exists(path);
                using (var sw = new StreamWriter(path, append: true, Encoding.UTF8))
                {
                    if (writeHeader)
                    {
                        sw.WriteLine(string.Join(",",
                            "timestampUtc",
                            "simSeconds",
                            "verdict",
                            // money pools
                            "totalMoney","agentsCoins","escrowCoins","cityCoins",
                            // money integrity
                            "worstMoneyResidual",
                            // negatives
                            "negAgentCoins","negBidEscrow","negAgentItems","negBuildingItems","negAskFoodEscrow",
                            // KPIs
                            "stallWaitP95_sec","starve_pct","unemploy_pct",
                            // activity context
                            "foodSold","cratesSold","vendorRevenue","dockRevenue","wagesHaul"
                        ));
                    }

                    sw.WriteLine(string.Join(",",
                        DateTime.UtcNow.ToString("o"),
                        world.SimTime.ToString("F1"),
                        verdict,
                        totalMoney, agentCoins, escrowCoins, cityCoins,
                        worstMoneyResidual,
                        negAgentCoins, negBidEscrow, negItemsAgents, negItemsBuildings, (negAskEscrowFood ? 1 : 0),
                        stallWaitP95.ToString("F2"),
                        starvePct.ToString("F2"),
                        unemployPct.ToString("F2"),
                        world.FoodSold, world.CratesSold, world.VendorRevenue, world.RevenueDock, world.WagesHaul
                    ));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[GATE] Failed to write SessionSummary.csv: " + ex.Message);
            }
        }
    }
}
