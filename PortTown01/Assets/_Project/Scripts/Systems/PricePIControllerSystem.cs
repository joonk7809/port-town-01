using System.Linq;
using UnityEngine;
using PortTown01.Core;
using PortTown01.Econ;

namespace PortTown01.Systems
{
    // Replaces/augments smoothing: update prices once per second via PI on inventory cover.
    // Uses EMA of sell rates from ledgers to compute cover = inv / sell_rate_ema.
    public class PricePIControllerSystem : ISimSystem
    {
        public string Name => "PricePI";

        public void Tick(World world, int tick, float dt)
        {
            if (!SimTicks.Every1Hz(tick)) return;

            // --- Update sell-rate EMAs (items/sec) from last-second deltas ---
            // You already maintain FoodSold and CratesSold deltas in Telemetry; we recompute here
            int dFood   = world.FoodSold - _prevFoodSold;
            int dCrates = world.CratesSold - _prevCratesSold;
            _prevFoodSold   = world.FoodSold;
            _prevCratesSold = world.CratesSold;

            float alpha = Mathf.Clamp01(world.SellRateAlpha);
            world.FoodSellRateEma   = Mathf.Lerp(world.FoodSellRateEma,   Mathf.Max(0.01f, dFood),   alpha);
            world.CrateSellRateEma  = Mathf.Lerp(world.CrateSellRateEma,  Mathf.Max(0.01f, dCrates), alpha);

            // --- Compute inventory on hand ---
            int vendorInv = world.Agents.FirstOrDefault(a => a.IsVendor)?.Carry.Get(ItemType.Food) ?? 0;
            int vendorEsc = world.FoodBook?.Asks.Sum(o => o.EscrowItems) ?? 0;
            float foodInv = vendorInv + vendorEsc;

            var mill = world.Buildings.FirstOrDefault(b => b.Type == BuildingType.Mill);
            int crateStock = (mill != null) ? mill.Storage.Get(ItemType.Crate) : 0;

            // --- Covers ---
            float foodCoverSec  = foodInv  / Mathf.Max(0.01f, world.FoodSellRateEma);
            float crateCoverSec = crateStock / Mathf.Max(0.01f, world.CrateSellRateEma);

            // --- Errors ---
            float eFood  = foodCoverSec  - world.FoodTargetCoverSec;
            float eCrate = crateCoverSec - world.CrateTargetCoverSec;

            world.FoodCoverErrorInt  = Mathf.Clamp(world.FoodCoverErrorInt + eFood,  -10000f, 10000f);
            world.CrateCoverErrorInt = Mathf.Clamp(world.CrateCoverErrorInt + eCrate,-10000f, 10000f);

            // --- PI updates in log-space (multiplicative price move) ---
            float dLogFood  = world.Kp * eFood  + world.Ki * world.FoodCoverErrorInt;
            float dLogCrate = world.Kp * eCrate + world.Ki * world.CrateCoverErrorInt;

            int newFood  = Mathf.RoundToInt(Mathf.Clamp(world.FoodPrice  * Mathf.Exp(dLogFood),  EconDefs.FOOD_PRICE_MIN,  EconDefs.FOOD_PRICE_MAX));
            int newCrate = Mathf.RoundToInt(Mathf.Clamp(world.CratePrice * Mathf.Exp(dLogCrate), EconDefs.CRATE_PRICE_MIN, EconDefs.CRATE_PRICE_MAX));

            // Step-limit to <5% per second
            newFood  = StepLimit(world.FoodPrice,  newFood,  0.05f);
            newCrate = StepLimit(world.CratePrice, newCrate, 0.05f);

            world.FoodPrice  = newFood;
            world.CratePrice = newCrate;

            // KPIs: price variance proxy (CPI-lite)
            world.PriceVarSamples++;
            float cpi = 0.5f * world.FoodPrice + 0.5f * (world.CratePrice / 5f); // arbitrary basket; stable scaling
            world.PriceVarCpiLike += cpi * cpi; // accumulate sum of squares for later normalization
        }

        private int _prevFoodSold = 0;
        private int _prevCratesSold = 0;

        private int StepLimit(int oldP, int newP, float frac)
        {
            int up   = Mathf.RoundToInt(oldP * (1f + frac));
            int down = Mathf.RoundToInt(oldP * (1f - frac));
            return Mathf.Clamp(newP, Mathf.Min(oldP, down), Mathf.Max(oldP, up));
        }
    }
}
