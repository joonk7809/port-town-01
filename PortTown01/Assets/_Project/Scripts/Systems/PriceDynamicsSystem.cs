using System.Linq;
using UnityEngine;
using PortTown01.Core;
using PortTown01.Econ;

namespace PortTown01.Systems
{
    // Nudge prices toward balance using simple signals:
    //  - Food: low vendor stock + high recent sales → price up; high stock + low sales → price down.
    //  - Crates: low mill crates + strong shipments → price up; high crates + weak shipments → price down.
    // Uses small integer steps with clamped bounds.
    public class PriceDynamicsSystem : ISimSystem
    {
        public string Name => "PriceDynamics";

        const float EVERY_SEC = 1f;

        // EMA smoothing
        const float ALPHA = 0.25f;
        float emaFoodStock = -1f, emaFoodSales = -1f;
        float emaCrateStock = -1f, emaCrateShip = -1f;

        // Previous counters for deltas
        int prevFoodSold = 0;
        int prevCratesSold = 0;

        public void Tick(World world, int tick, float dt)
        {
            if (!SimTicks.Every1Hz(tick)) return;

            // --- FOOD signals ---
            var vendor = world.Agents.FirstOrDefault(a => a.IsVendor);
            int vendorInv   = vendor != null ? vendor.Carry.Get(ItemType.Food) : 0;
            int vendorEsc   = (vendor != null)
                ? world.FoodBook.Asks.Where(o => o.AgentId == vendor.Id && o.Qty > 0).Sum(o => o.EscrowItems)
                : 0;
            int vendorForSale = vendorInv + vendorEsc;

            int soldDelta = world.FoodSold - prevFoodSold; // units since last second
            prevFoodSold = world.FoodSold;

            emaFoodStock = (emaFoodStock < 0) ? vendorForSale : Mathf.Lerp(emaFoodStock, vendorForSale, ALPHA);
            emaFoodSales = (emaFoodSales < 0) ? soldDelta     : Mathf.Lerp(emaFoodSales, soldDelta, ALPHA);

            // Targets (tune): aim for ~1–2 units/sec sold and keep ≥20 units available
            const float FOOD_SALES_TARGET = 1.2f;
            const float FOOD_STOCK_LOW    = 10f;
            const float FOOD_STOCK_HIGH   = 60f;

            int fp = world.FoodPrice;
            if (emaFoodSales > FOOD_SALES_TARGET && emaFoodStock < FOOD_STOCK_LOW) fp++;          // demand>target & scarce
            else if (emaFoodSales < 0.5f && emaFoodStock > FOOD_STOCK_HIGH) fp--;                 // slow sales & glutted
            else if (emaFoodStock < FOOD_STOCK_LOW - 5 && emaFoodSales > 0.6f) fp++;              // very scarce
            else if (emaFoodStock > FOOD_STOCK_HIGH + 15 && emaFoodSales < 0.8f) fp--;            // very glutted

            world.FoodPrice = Mathf.Clamp(fp, EconDefs.FOOD_PRICE_MIN, EconDefs.FOOD_PRICE_MAX);

            // --- CRATES signals ---
            var mill = world.Buildings.FirstOrDefault(b => b.Type == BuildingType.Mill);
            int millCrates = mill?.Storage.Get(ItemType.Crate) ?? 0;

            int shippedDelta = world.CratesSold - prevCratesSold; // crates shipped in last sec (we already track CratesSold)
            prevCratesSold = world.CratesSold;

            emaCrateStock = (emaCrateStock < 0) ? millCrates : Mathf.Lerp(emaCrateStock, millCrates, ALPHA);
            emaCrateShip  = (emaCrateShip  < 0) ? shippedDelta: Mathf.Lerp(emaCrateShip, shippedDelta, ALPHA);

            // Targets: keep a small crate buffer, but push price up if shipping stays strong while stock is low
            const float CRATE_SHIP_TARGET = 0.5f; // ~1 crate every 2 sec
            const float CRATE_STOCK_LOW   = 2f;
            const float CRATE_STOCK_HIGH  = 12f;

            int cp = world.CratePrice;
            if (emaCrateShip > CRATE_SHIP_TARGET && emaCrateStock < CRATE_STOCK_LOW) cp++;
            else if (emaCrateShip < 0.2f && emaCrateStock > CRATE_STOCK_HIGH)        cp--;
            else if (emaCrateStock < CRATE_STOCK_LOW - 1 && emaCrateShip > 0.3f)     cp++;
            else if (emaCrateStock > CRATE_STOCK_HIGH + 3 && emaCrateShip < 0.4f)    cp--;

            world.CratePrice = Mathf.Clamp(cp, EconDefs.CRATE_PRICE_MIN, EconDefs.CRATE_PRICE_MAX);
        }
    }
}
