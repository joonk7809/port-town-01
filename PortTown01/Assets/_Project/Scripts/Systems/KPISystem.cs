using System.Linq;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Prints KPIs every N seconds and at ~10-minute mark; you can extend to write CSV.
    public class KPISystem : ISimSystem
    {
        public string Name => "KPIs";

        private float _accum;
        private const float EVERY = 10f;

        public void Tick(World world, int tick, float dt)
        {
            // Collect running stats
            int workers = world.Agents.Count(a => a.Role != JobRole.None);
            int unemployed = world.Agents.Count(a => a.Role == JobRole.None && !a.IsVendor && !a.IsEmployer);
            world.PossibleWorkerTicks += workers + unemployed > 0 ? 1 : 0;
            world.UnemployedTicks += unemployed > 0 ? 1 : 0;
            world.StarvedAgentSeconds += world.Agents.Count(a => a.Food < 10f) * dt;

            _accum += dt;
            if (_accum < EVERY) return;
            _accum = 0f;

            if ((int)world.SimTime % 600 == 0) Print(world, tag: "END");
            else Print(world, tag: "KPI");
        }

        private void Print(World w, string tag)
        {
            float starvationPct = 100f * (w.StarvedAgentSeconds / Mathf.Max(1f, w.SimTime * w.Agents.Count));
            float unemploymentPct = (w.PossibleWorkerTicks > 0) ? 100f * (w.UnemployedTicks / (float)w.PossibleWorkerTicks) : 0f;

            // naive variance proxy
            float cpiVar = (w.PriceVarSamples > 0) ? (w.PriceVarCpiLike / w.PriceVarSamples) : 0f;

            Debug.Log($"[{tag}] " +
                      $"starvation%={starvationPct:F2}, " +
                      $"unemployment%={unemploymentPct:F2}, " +
                      $"stall_p95~(todo), " +
                      $"CPIvar={cpiVar:F2}, " +
                      $"bossMargin%~(todo), vendorMargin%~(todo), " +
                      $"moneyResidual<= {w.MoneyResidualAbsMax}, itemsResidual<= {w.ItemResidualAbsMax}");
        }
    }
}
