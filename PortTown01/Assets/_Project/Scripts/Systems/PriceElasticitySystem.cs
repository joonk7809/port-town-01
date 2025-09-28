using System.Linq;
using UnityEngine;
using PortTown01.Core;
using PortTown01.Econ;

namespace PortTown01.Systems
{
    // Adjusts FoodPrice & CratePrice each second using elastic responses to:
    //  • stock (supply), recent sales/shipments (demand), and order-book imbalance (Food).
    // Smoothing + hysteresis avoid flapping. Clamped within EconDefs min/max bands.
    public class PriceElasticitySystem : ISimSystem
    {
        public string Name => "PriceElasticity";

        const float TICK_SEC = 1f;

        // EMA smoothing for signals
        const float ALPHA = 0.25f;
        float emaFoodStock = -1f, emaFoodSales = -1f, emaFoodImb = -1f;
        float emaCrateStock = -1f, emaCrateShip = -1f;

        // Prev counters for deltas
        int prevFoodSold = 0;
        int prevCratesSold = 0;

        // Targets (tune)
        const float FOOD_STOCK_TARGET   = 40f;   // vendor units “for sale”
        const float FOOD_SALES_TARGET   = 1.2f;  // ~units/sec
        const float CRATE_STOCK_TARGET  = 6f;    // crates at mill
        const float CRATE_SHIP_TARGET   = 0.5f;  // crates/sec

        // Elasticity (coeffs → price Δ per 1.0 normalized error)
        const float K_FOOD_SUPPLY   = 0.8f;
        const float K_FOOD_DEMAND   = 0.9f;
        const float K_FOOD_IMBAL    = 0.6f;   // book imbalance contribution
        const int   FOOD_MAX_STEP   = 2;      // coins/sec limit
        const float FOOD_DEAD_BAND  = 0.20f;  // hysteresis (skip very small signals)

        const float K_CRATE_SUPPLY  = 1.0f;
        const float K_CRATE_DEMAND  = 1.0f;
        const int   CRATE_MAX_STEP  = 2;
        const float CRATE_DEAD_BAND = 0.20f;

        public void Tick(World world, int tick, float dt)
        {
            if (!SimTicks.Every1Hz(tick)) return;

            // ---------------- FOOD ----------------
            var vendor = world.Agents.FirstOrDefault(a => a.IsVendor);
            int vendorInv = vendor?.Carry.Get(ItemType.Food) ?? 0;
            int vendorEsc = (vendor != null)
                ? world.FoodBook.Asks.Where(o => o.AgentId == vendor.Id && o.Qty > 0).Sum(o => o.EscrowItems)
                : 0;
            int foodForSale = vendorInv + vendorEsc;

            int soldDelta = world.FoodSold - prevFoodSold; // units in last second
            prevFoodSold = world.FoodSold;

            // Order book imbalance near current price
            int p = world.FoodPrice;
            int bidQtyNear = world.FoodBook.Bids.Where(o => o.Qty > 0 && o.UnitPrice >= p - 1).Sum(o => o.Qty);
            int askQtyNear = world.FoodBook.Asks.Where(o => o.Qty > 0 && o.UnitPrice <= p + 1).Sum(o => o.Qty);
            float imb = ((bidQtyNear + 1f) / (askQtyNear + 1f)) - 1f; // >0 → upward pressure

            // Smooth signals
            emaFoodStock = emaFoodStock < 0 ? foodForSale : Mathf.Lerp(emaFoodStock, foodForSale, ALPHA);
            emaFoodSales = emaFoodSales < 0 ? soldDelta   : Mathf.Lerp(emaFoodSales, soldDelta,   ALPHA);
            emaFoodImb   = emaFoodImb   < 0 ? imb         : Mathf.Lerp(emaFoodImb,   imb,         ALPHA);

            // Normalize errors (positive → price up)
            float eSupply = (FOOD_STOCK_TARGET - emaFoodStock) / Mathf.Max(1f, FOOD_STOCK_TARGET);
            float eDemand = (emaFoodSales    - FOOD_SALES_TARGET) / Mathf.Max(0.5f, FOOD_SALES_TARGET);
            float signal  = K_FOOD_SUPPLY * eSupply + K_FOOD_DEMAND * eDemand + K_FOOD_IMBAL * emaFoodImb;

            int step = Mathf.Abs(signal) < FOOD_DEAD_BAND ? 0 : Mathf.Clamp(Mathf.RoundToInt(signal), -FOOD_MAX_STEP, FOOD_MAX_STEP);
            if (step != 0)
                world.FoodPrice = Mathf.Clamp(world.FoodPrice + step, EconDefs.FOOD_PRICE_MIN, EconDefs.FOOD_PRICE_MAX);

            // ---------------- CRATES ----------------
            var mill = world.Buildings.FirstOrDefault(b => b.Type == BuildingType.Mill);
            int millCrates = mill?.Storage.Get(ItemType.Crate) ?? 0;

            int shipDelta = world.CratesSold - prevCratesSold;
            prevCratesSold = world.CratesSold;

            emaCrateStock = emaCrateStock < 0 ? millCrates : Mathf.Lerp(emaCrateStock, millCrates, ALPHA);
            emaCrateShip  = emaCrateShip  < 0 ? shipDelta  : Mathf.Lerp(emaCrateShip,  shipDelta,  ALPHA);

            float eCSupply = (CRATE_STOCK_TARGET - emaCrateStock) / Mathf.Max(1f, CRATE_STOCK_TARGET);
            float eCDemand = (emaCrateShip - CRATE_SHIP_TARGET)   / Mathf.Max(0.3f, CRATE_SHIP_TARGET);
            float cSignal  = K_CRATE_SUPPLY * eCSupply + K_CRATE_DEMAND * eCDemand;

            int cStep = Mathf.Abs(cSignal) < CRATE_DEAD_BAND ? 0 : Mathf.Clamp(Mathf.RoundToInt(cSignal), -CRATE_MAX_STEP, CRATE_MAX_STEP);
            if (cStep != 0)
                world.CratePrice = Mathf.Clamp(world.CratePrice + cStep, EconDefs.CRATE_PRICE_MIN, EconDefs.CRATE_PRICE_MAX);
        }
    }
}
