using System.Linq;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Manages city budget (top-up & decay) and writes coins into the dock buyer agent.
    // Also computes a "desired purchases per second" D_t; the existing dock buyer
    // behavior already caps by its coins when paying the boss at unload.
    public class CityBudgetAndDemandSystem : ISimSystem
    {
        public string Name => "CityBudget";

        private float _accum;

        public void Tick(World world, int tick, float dt)
        {
            _accum += dt;
            if (_accum < 1f) return;
            _accum -= 1f;

            // --- Budget dynamics per second ---
            // continuous top-up (τ/day distributed over 600s)
            world.CityBudget += world.CityBudgetSecTopUp;
            // simple decay of unspent budget toward 0 at kappa per day ~ apply fractional per second
            float perSecKappa = Mathf.Pow(world.CityBudgetDecayKappa, 1f / 600f);
            world.CityBudget *= perSecKappa;

            // --- Demand AR(1) shock update (ε_t = ρ ε_{t-1} + η) ---
            float eta = Random.Range(-world.DemandSigma, world.DemandSigma); // simple bounded noise (ok for sim)
            world.DemandShock = world.DemandRho * world.DemandShock + eta;

            // Indicative crate price (use live)
            float P = Mathf.Max(1, world.CratePrice);
            float desired = Mathf.Max(0f, world.DemandAlpha - world.DemandBeta * P + world.DemandShock);

            // Put the city's budget as COINS into the dock buyer agent up to the budget
            var dockBuyer = world.Agents.FirstOrDefault(a =>
                !a.IsVendor && !a.IsEmployer && Vector3.Distance(a.Pos, new Vector3(40f,0f,0f)) < 5f); // your spawn matches this

            if (dockBuyer != null)
            {
                // allocate a spend envelope for the next second: min(budget, desired * price)
                int alloc = Mathf.FloorToInt(Mathf.Min(world.CityBudget, desired * world.CratePrice));
                if (alloc > 0)
                {
                    dockBuyer.Coins += alloc;
                    world.CityBudget -= alloc;
                }
            }

            // Optional: keep a world-level stat for desired purchases (for telemetry)
            world.CratesSold += 0; // no-op to remind this ties to OrderMatching/Haul unload
        }
    }
}
