using System.Linq;
using UnityEngine;
using PortTown01.Core;
using PortTown01.Econ; // Ledger & LedgerWriter

namespace PortTown01.Systems
{
    // City budget top-up/decay and a simple dock-buyer allocation envelope.
    // All coin movements are integer and routed through the Ledger.
    public class CityBudgetAndDemandSystem : ISimSystem
    {
        public string Name => "CityBudget";

        // 1 Hz cadence for budget/demand updates
        private float _accumSec;

        // Carry fractional top-up so we mint exact long-run totals even with int coins
        private float _topUpFracCarry;

        public void Tick(World world, int tick, float dt)
        {
            _accumSec += dt;
            if (_accumSec < 1f) return;
            _accumSec -= 1f;

            // -------- 1) Budget dynamics (all integer via Ledger) --------

            // Top-up per second: discretize to int with fractional carry
            float topUpF = world.CityBudgetSecTopUp + _topUpFracCarry;  // e.g. 1200/day -> 2.0/s
            int   topUp  = Mathf.FloorToInt(topUpF);
            _topUpFracCarry = topUpF - topUp;
            if (topUp > 0)
            {
                // External source -> City + inflow
                Ledger.MintToCity(world, topUp, LedgerWriter.MintExternal, "city top-up");
            }

            // Rollover decay (kappa per day → per-second). Burn exact integer delta
            // NOTE: we compute ideal post-decay, then burn the difference as an external sink.
            float perSecKappa = Mathf.Pow(world.CityBudgetDecayKappa, 1f / 600f); // 600s per day
            int   idealPost   = Mathf.FloorToInt(world.CityBudget * perSecKappa);
            int   burn        = world.CityBudget - idealPost;
            if (burn > 0)
            {
                // External sink -> City - outflow
                Ledger.BurnFromCity(world, burn, LedgerWriter.BurnExternal, "city decay");
            }

            // -------- 2) Demand shock & desired purchases --------

            // AR(1) demand shock: ε_t = ρ ε_{t-1} + η, bounded white noise ~U(-σ,σ)
            float eta = Random.Range(-world.DemandSigma, world.DemandSigma);
            world.DemandShock = world.DemandRho * world.DemandShock + eta;

            // Linear demand with price sensitivity; clamp to 0
            int   P       = Mathf.Max(1, world.CratePrice);
            float desired = Mathf.Max(0f, world.DemandAlpha - world.DemandBeta * P + world.DemandShock);

            // -------- 3) Allocate a one-second spend envelope to the dock buyer --------

            // Heuristic: fund up to desired * P, capped by city budget (integer coins)
            int wantCoins = Mathf.FloorToInt(desired * P);
            int give      = Mathf.Clamp(wantCoins, 0, world.CityBudget);

            if (give > 0)
            {
                // Find the dock buyer (your scene spawns it at ~ (40,0,0) and it's not vendor/employer)
                var dockBuyer = world.Agents.FirstOrDefault(a =>
                    !a.IsVendor && !a.IsEmployer &&
                    Vector3.Distance(a.Pos, new Vector3(40f, 0f, 0f)) < 6f);

                if (dockBuyer != null)
                {
                    // Internal transfer: City → dockBuyer
                    Ledger.Transfer(world, ref world.CityBudget, ref dockBuyer.Coins, give,
                        LedgerWriter.CityAllocation, $"alloc for desired={desired:F2} P={P}");
                }
                else
                {
                    // No buyer found; do nothing this tick (coins remain in city pool)
#if UNITY_EDITOR
                    Debug.LogWarning("[CityBudget] No dock buyer found near dock; skipped allocation.");
#endif
                }
            }
        }
    }
}
