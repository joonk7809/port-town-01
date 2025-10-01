using System.Linq;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Per-second PI controller for Food and Crate prices with anti-windup.
    // Targets an inventory "cover" (seconds of stock at current sell-through rate).
    // No external dependencies beyond World state; tune constants below to match your econ.
    public sealed class PricePIControllerSystem : ISimSystem
    {
        public string Name => "PricePI";

        // --- Tunables (safe defaults; adjust in code or lift into World/EconDefs later) ---

        // Target inventory cover (seconds of supply)
        private const float FOOD_TARGET_COVER_SEC  = 120f;
        private const float CRATE_TARGET_COVER_SEC = 120f;

        // PI gains (per second). Keep small to avoid oscillations.
        private const float KP = 0.010f;
        private const float KI = 0.002f;

        // EMA smoothing for sell-through rate (alpha per 1 Hz)
        private const float SELL_RATE_EMA_ALPHA = 0.25f;

        // Hard bands to avoid runaway (fall back if you don't have band fields elsewhere)
        private const int FOOD_PRICE_MIN  = 3;
        private const int FOOD_PRICE_MAX  = 15;
        private const int CRATE_PRICE_MIN = 5;
        private const int CRATE_PRICE_MAX = 100;

        // Dead-band to ignore tiny errors around target cover (seconds)
        private const float COVER_DEADBAND_SEC = 5f;

        // --- Internal state ---
        private float _accum; // 1 Hz cadence

        private int   _prevFoodSold;
        private int   _prevCratesSold;
        private float _foodSellEma;    // units/sec
        private float _crateSellEma;   // units/sec

        private float _iFood;          // integral accumulator
        private float _iCrate;

        public void Tick(World world, int tick, float dt)
        {
            _accum += dt;
            if (!SimTicks.Every1Hz(tick)) return;

            // ----------- FOOD -----------
            var vendor   = world.Agents.FirstOrDefault(a => a.IsVendor);
            int foodSold = world.FoodSold;
            int dFood    = Mathf.Max(0, foodSold - _prevFoodSold);
            _prevFoodSold = foodSold;

            // EMA of sell-through (units/sec)
            _foodSellEma = (_foodSellEma <= 0f)
                ? dFood
                : Mathf.Lerp(_foodSellEma, dFood, SELL_RATE_EMA_ALPHA);

            // Available for sale = on-hand + escrowed asks by vendor
            int venInv  = vendor != null ? vendor.Carry.Get(ItemType.Food) : 0;
            int venEsc  = (vendor != null && world.FoodBook != null)
                ? world.FoodBook.Asks.Where(o => o.AgentId == vendor.Id && o.Qty > 0).Sum(o => o.EscrowItems)
                : 0;
            int forSale = venInv + venEsc;

            float cover = ComputeCover(forSale, _foodSellEma);
            float e     = ClampDeadband(FOOD_TARGET_COVER_SEC - cover, COVER_DEADBAND_SEC);

            // PI control effort
            float u = KP * e + KI * _iFood;

            // Propose multiplicative update (exponential map keeps positivity)
            int p0 = Mathf.Max(FOOD_PRICE_MIN, world.FoodPrice);
            int p1 = Mathf.Clamp(Mathf.RoundToInt(p0 * Mathf.Exp(u)), FOOD_PRICE_MIN, FOOD_PRICE_MAX);

            // Anti-windup: if clamped and control would push further out of band, freeze integral
            bool clampedMin = p1 <= FOOD_PRICE_MIN && e < 0f; // asking to go lower but at min
            bool clampedMax = p1 >= FOOD_PRICE_MAX && e > 0f; // asking to go higher but at max
            if (!clampedMin && !clampedMax)
                _iFood += e; // integrate only when not saturating

            world.FoodPrice = p1;

            // ----------- CRATES -----------
            // Use mill stock as "for sale" proxy; haulers drain it to the dock.
            var mill     = world.Buildings.FirstOrDefault(b => b.Type == BuildingType.Mill);
            int crateInv = mill != null ? mill.Storage.Get(ItemType.Crate) : 0;

            int cratesSold = world.CratesSold;
            int dCrates    = Mathf.Max(0, cratesSold - _prevCratesSold);
            _prevCratesSold = cratesSold;

            _crateSellEma = (_crateSellEma <= 0f)
                ? dCrates
                : Mathf.Lerp(_crateSellEma, dCrates, SELL_RATE_EMA_ALPHA);

            float coverCr = ComputeCover(crateInv, _crateSellEma);
            float eCr     = ClampDeadband(CRATE_TARGET_COVER_SEC - coverCr, COVER_DEADBAND_SEC);

            float uCr = KP * eCr + KI * _iCrate;

            int pc0 = Mathf.Max(CRATE_PRICE_MIN, world.CratePrice);
            int pc1 = Mathf.Clamp(Mathf.RoundToInt(pc0 * Mathf.Exp(uCr)), CRATE_PRICE_MIN, CRATE_PRICE_MAX);

            bool clampedMinCr = pc1 <= CRATE_PRICE_MIN && eCr < 0f;
            bool clampedMaxCr = pc1 >= CRATE_PRICE_MAX && eCr > 0f;
            if (!clampedMinCr && !clampedMaxCr)
                _iCrate += eCr;

            world.CratePrice = pc1;
        }

        private static float ComputeCover(int forSaleUnits, float sellPerSec)
        {
            if (sellPerSec <= 0.0001f) return float.PositiveInfinity; // no demand -> infinite cover
            return Mathf.Clamp(forSaleUnits / sellPerSec, 0f, 3600f);
        }

        private static float ClampDeadband(float value, float deadband)
        {
            if (Mathf.Abs(value) <= deadband) return 0f;
            return value > 0 ? value - deadband : value + deadband;
        }
    }
}
