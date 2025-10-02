using System.Linq;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    /// <summary>
    /// 1 Hz console telemetry with derived W/E/S/L intent counts.
    /// Does not rely on any Planner-owned intent field.
    /// Heuristics (precedence: Eat > Sleep > Work > Leisure):
    ///   Eat   : within queue radius of Trading stall.
    ///   Sleep : DayPhase.Sleep OR near home.
    ///   Work  : DayPhase.Work OR near role-appropriate worksite.
    ///   Leisure: otherwise.
    /// </summary>
    public sealed class TelemetrySystem : ISimSystem
    {
        public string Name => "Telemetry";

        private const float QUEUE_RADIUS_M   = 2.5f; // who counts as "in line"
        private const float NEAR_HOME_M      = 2.0f;
        private const float NEAR_WORK_M      = 6.0f;

        public void Tick(World world, int tick, float dt)
        {
            if (!SimTicks.Every1Hz(tick)) return;

            var stall   = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.Trading);
            var forest  = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.Logging);
            var mill    = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.Milling);
            var dock    = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.DockLoading);

            // Derived W/E/S/L counters (skip pure infrastructure actors for W/E/S/L)
            int w = 0, e = 0, s = 0, l = 0;

            foreach (var a in world.Agents)
            {
                bool infra = a.IsVendor || a.IsEmployer;

                // Eat: highest precedence
                bool isEat = false;
                if (stall != null)
                {
                    if (Vector3.Distance(a.Pos, stall.StationPos) <= QUEUE_RADIUS_M)
                        isEat = true;
                }

                // Sleep: next precedence
                bool isSleep = (!isEat) && (a.Phase == DayPhase.Sleep ||
                                            Vector3.Distance(a.Pos, a.HomePos) <= NEAR_HOME_M);

                // Work: role-aware, only if not eating or sleeping
                bool isWork = false;
                if (!isEat && !isSleep && !infra)
                {
                    Vector3? workPos = null;
                    switch (a.Role)
                    {
                        case JobRole.Logger: workPos = forest?.StationPos; break;
                        case JobRole.Miller: workPos = mill?.StationPos;   break;
                        case JobRole.Hauler:
                            // if carrying crates, dock; else mill
                            int crates = a.Carry.Get(ItemType.Crate);
                            workPos = crates > 0 ? dock?.StationPos : mill?.StationPos; break;
                        default:
                            // fall back to work phase heuristic
                            break;
                    }

                    if (workPos.HasValue)
                        isWork = Vector3.Distance(a.Pos, workPos.Value) <= NEAR_WORK_M;
                    else
                        isWork = (a.Phase == DayPhase.Work);
                }

                // Leisure: remainder
                bool isLeisure = !isEat && !isSleep && !isWork;

                if (!infra)
                {
                    if (isEat) e++;
                    else if (isSleep) s++;
                    else if (isWork) w++;
                    else l++;
                }
            }

            // Context (prices, stocks, queue)
            int foodPrice  = Mathf.Max(1, world.FoodPrice);
            int cratePrice = Mathf.Max(1, world.CratePrice);

            int stallQueueCount = 0;
            if (stall != null)
            {
                var sPos = stall.StationPos;
                stallQueueCount = world.Agents.Count(a => Vector3.Distance(a.Pos, sPos) <= QUEUE_RADIUS_M);
            }

            // Money integrity quick line (parity with HUD/CSV)
            int agentCoins  = world.Agents.Sum(a => a.Coins);
            int escrowCoins = world.FoodBook?.Bids.Where(b => b.Qty > 0).Sum(b => b.EscrowCoins) ?? 0;
            int cityCoins   = world.CityBudget;
            long totalMoney = (long)agentCoins + escrowCoins + cityCoins;

            Debug.Log(
                $"[TEL] t={world.SimTime:F0}s | W={w} E={e} S={s} L={l} | " +
                $"queue~{stallQueueCount} | P(food)={foodPrice} P(crate)={cratePrice} | " +
                $"$ total={totalMoney} (agents={agentCoins}, escrow={escrowCoins}, city={cityCoins})");
        }
    }
}
