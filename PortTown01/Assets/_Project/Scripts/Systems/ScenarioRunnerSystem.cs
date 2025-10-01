using System;
using System.Linq;
using UnityEngine;
using PortTown01.Core;
using PortTown01.Econ; // for Ledger.BurnFromCity

namespace PortTown01.Systems
{
    /// <summary>
    /// Stage A regression scenarios without mutating read-only policy fields.
    /// Select via CLI: --scenario=baseline|dock_broke|vendor_broke|restock_delay
    /// or env var PT_SCENARIO. Defaults to "baseline".
    ///
    /// NOTE: We simulate "no funding" by draining City/agent coins (external outflow),
    /// not by changing CityBudgetSecTopUp. Audits remain exact (ΔA+ΔE+ΔC+dIn−dOut=0).
    /// </summary>
    public sealed class ScenarioRunnerSystem : ISimSystem
    {
        public string Name => "Scenario";

        private bool   _init;
        private string _scenario = "baseline";

        // Baselines we may temporarily alter (only writable fields)
        private float _baselineFoodLeadTimeSec;

        public void Tick(World world, int tick, float dt)
        {
            if (!_init)
            {
                _init = true;
                _scenario = ResolveScenario();
                _baselineFoodLeadTimeSec = world.FoodLeadTimeSec;

#if UNITY_EDITOR
                Debug.Log($"[SCENARIO] Using scenario='{_scenario}'");
#endif
            }

            // 1 Hz cadence is enough and keeps ordering simple
            if (!SimTicks.Every1Hz(tick)) return;

            double t = world.SimTime;

            switch (_scenario)
            {
                case "baseline":
                    // No changes
                    break;

                case "dock_broke":
                {
                    // Window: [30s, 150s)
                    if (t >= 30 && t < 150)
                    {
                        // 1) Drain City budget to zero each second (external sink)
                        int cityNow = world.CityBudget;
                        if (cityNow > 0)
                        {
                            Ledger.BurnFromCity(world, cityNow, LedgerWriter.BurnExternal, "scenario:dock_broke drain city");
                        }

                        // 2) Ensure the dock buyer cannot buy: drain their wallet to sink
                        var dockBuyer = FindDockBuyer(world);
                        if (dockBuyer != null && dockBuyer.Coins > 0)
                        {
                            int amt = dockBuyer.Coins;
                            dockBuyer.Coins = 0;
                            world.CoinsExternalOutflow += amt; // account as external sink
#if UNITY_EDITOR
                            Debug.Log($"[SCENARIO] dock_broke drained dock buyer {amt} coins @t={t:F0}s");
#endif
                        }
                    }
                    break;
                }

                case "vendor_broke":
                {
                    // Window: [60s, 120s). Drain vendor coins so they cannot restock.
                    var vendor = world.Agents.FirstOrDefault(a => a.IsVendor);
                    if (vendor != null && t >= 60 && t < 120 && vendor.Coins > 0)
                    {
                        int amt = vendor.Coins;
                        vendor.Coins = 0;
                        world.CoinsExternalOutflow += amt; // external sink to keep audits exact
#if UNITY_EDITOR
                        Debug.Log($"[SCENARIO] vendor_broke drained vendor {amt} coins @t={t:F0}s");
#endif
                    }
                    break;
                }

                case "restock_delay":
                {
                    // Increase food lead time to create a supply shock; restore later
                    if (t >= 90 && t < 180)
                        world.FoodLeadTimeSec = Mathf.Max(45f, _baselineFoodLeadTimeSec);
                    else if (t >= 180)
                        world.FoodLeadTimeSec = _baselineFoodLeadTimeSec;
                    break;
                }

                default:
#if UNITY_EDITOR
                    Debug.LogWarning($"[SCENARIO] Unknown scenario '{_scenario}', running baseline.");
#endif
                    break;
            }
        }

        private static Agent FindDockBuyer(World world)
        {
            var dockSite = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.DockLoading);
            if (dockSite == null) return null;

            // Heuristic: stationary agent near dock, not a vendor/employer
            return world.Agents
                .Where(a => !a.IsVendor && !a.IsEmployer)
                .OrderBy(a => Vector3.Distance(a.Pos, dockSite.StationPos))
                .FirstOrDefault(a => a.SpeedMps == 0f && Vector3.Distance(a.Pos, dockSite.StationPos) < 6f);
        }

        private static string ResolveScenario()
        {
            // CLI: --scenario=foo or --scenario foo
            try
            {
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    var a = args[i];
                    if (a.StartsWith("--scenario=", StringComparison.OrdinalIgnoreCase))
                        return a.Substring("--scenario=".Length).Trim().ToLowerInvariant();
                    if (string.Equals(a, "--scenario", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                        return args[i + 1].Trim().ToLowerInvariant();
                }
            }
            catch { /* ignore */ }

            // Env var fallback
            var env = Environment.GetEnvironmentVariable("PT_SCENARIO");
            if (!string.IsNullOrEmpty(env)) return env.Trim().ToLowerInvariant();

            return "baseline";
        }
    }
}
