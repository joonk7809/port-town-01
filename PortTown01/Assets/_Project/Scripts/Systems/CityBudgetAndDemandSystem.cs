using System.Linq;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Manages city budget (top-up & decay), AR(1) demand shock,
    // and allocates coins to the dock buyer each second.
    public class CityBudgetAndDemandSystem : ISimSystem
    {
        public string Name => "CityBudget";

        public void Tick(World world, int tick, float dt)
        {
            if (!SimTicks.Every1Hz(tick)) return; // cadence aligned with Audit

            // --- Budget: top-up (int) ---
            int top = world.CityBudgetSecTopUp; // e.g., 2 coins/sec
            if (top > 0)
            {
                world.CityBudget          += top;
                world.CoinsExternalInflow += top; // external "mint"
            }

            // --- Budget: decay with fractional residue bucket ---
            float perSecKappa = Mathf.Pow(world.CityBudgetDecayKappa, 1f / 600f);
            float decFloat    = world.CityBudget * (1f - perSecKappa);
            world.CityBudgetDecayResid += decFloat;
            int decInt = Mathf.FloorToInt(world.CityBudgetDecayResid);
            if (decInt > 0)
            {
                int decApplied = Mathf.Min(decInt, world.CityBudget);
                world.CityBudget           -= decApplied;
                world.CoinsExternalOutflow += decApplied; // external "burn"
                world.CityBudgetDecayResid -= decApplied;
            }

            // --- Demand: AR(1) shock ε_t = ρ ε_{t-1} + η_t, η~U[-σ,σ] (simple) ---
            float eta = Random.Range(-world.DemandSigma, world.DemandSigma);
            world.DemandShock = world.DemandRho * world.DemandShock + eta;

            // Desired purchases per second D_t = max(0, α − β P_t + ε_t)
            float P = Mathf.Max(1, world.CratePrice);
            float desired = Mathf.Max(0f, world.DemandAlpha - world.DemandBeta * P + world.DemandShock);

            // --- Find dock buyer agent (stationary, near dock loading site/building) ---
            var dockSite = world.Worksites.FirstOrDefault(ws => ws.Type == WorkType.DockLoading);
            Vector3 anchor = dockSite != null
                ? dockSite.StationPos
                : (world.Buildings.FirstOrDefault(b => b.Type == BuildingType.Dock)?.Pos ?? Vector3.zero);

            var dockBuyer = world.Agents
                .OrderByDescending(a => a.Coins)
                .FirstOrDefault(a => !a.IsVendor && !a.IsEmployer &&
                                     a.SpeedMps <= 0.01f &&
                                     Vector3.Distance(a.Pos, anchor) < 6f);
            if (dockBuyer == null) return;

            // --- Allocate city coins to buyer for ~1 second of demand at current price ---
            int alloc = Mathf.Min(world.CityBudget, Mathf.FloorToInt(desired * world.CratePrice));
            if (alloc > 0)
            {
                dockBuyer.Coins += alloc;
                world.CityBudget -= alloc; // transfer inside model (not external)
            }
        }
    }
}
