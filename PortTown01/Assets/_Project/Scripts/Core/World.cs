using System.Collections.Generic;
using PortTown01.Econ;  
using UnityEngine;

namespace PortTown01.Core
{
    // sim state holder
    public class World
    {
        public int Tick{ get; private set; } = 0;
        public float SimTime { get; private set; } = 0f; // seconds

        public readonly List<Agent> Agents = new();
        public readonly List<ResourceNode> ResourceNodes = new();
        public readonly List<Building> Buildings = new();
        public readonly List<Worksite> Worksites = new();

        public readonly OrderBook FoodBook = new();

        public readonly List<Contract> Contracts = new();
        public int NextContractId = 1;

        // --- Simple ledger (cumulative) ---
        public int CratesSold = 0;         // total crates sold at dock
        public int RevenueDock = 0;        // coins paid by dock buyer to company (boss)
        public int WagesHaul = 0;          // coins paid out to haulers for delivery

        // --- Live prices (mutable) ---
        public int FoodPrice  = PortTown01.Core.EconDefs.FOOD_PRICE_BASE;
        public int CratePrice = PortTown01.Core.EconDefs.CRATE_PRICE_BASE;

        // --- Food sales ledger (cumulative) ---
        public int FoodSold       = 0;  // total Food units sold via order book
        public int VendorRevenue  = 0;  // coins received by vendor from Food sales

        // --- City wholesaler (true coin sink for vendor restock) ---
        public int CityWholesalerCoins;            // sink accumulator (for audits/telemetry)

        // --- City dock buyer budget ---
        public float CityBudget;                   // current spendable budget (coins)
        public float CityBudgetDailyTopUp = 1200f; // τ per in-game day
        public float CityBudgetDecayKappa = 0.95f; // κ roll-over decay per day (applied continuously)
        public float CityBudgetSecTopUp => CityBudgetDailyTopUp / 600f; // day=600s

        // --- Dock demand (crates/sec) ---
        public float DemandAlpha = 8f;             // α
        public float DemandBeta  = 0.05f;          // β
        public float DemandRho   = 0.8f;           // ρ
        public float DemandSigma = 0.4f;           // σ
        public float DemandShock;                  // ε_t state

        // --- Vendor restock policy (s, S, L), batch size Q, wholesale price ---
        public int   Food_s           = 40;        // reorder point (will be recalculated on boot)
        public int   Food_S           = 100;       // order-up-to level (recalc on boot)
        public int   Food_Q           = 10;        // batch size
        public float FoodWholesale    = 3f;        // c_food_wholesale
        public float FoodLeadTimeSec  = 30f;       // L_food
        // pending deliveries: (dueTick, qty, totalCost)
        public List<(int dueTick, int qty, int cost)> PendingFoodDeliveries = new();

        // --- Price controller targets (inventory cover in seconds) ---
        public float FoodTargetCoverSec  = 120f;   // T_inv
        public float CrateTargetCoverSec = 120f;

        // PI gains (choose small)
        public float Kp = 0.0035f;
        public float Ki = 0.0005f;
        public float FoodCoverErrorInt;            // ∑e for Food
        public float CrateCoverErrorInt;           // ∑e for Crates

        // EMAs for sell rates (items/sec) for cover calc
        public float FoodSellRateEma;
        public float CrateSellRateEma;
        public float SellRateAlpha = 0.2f;         // 1Hz updates

        // --- KPIs / statistics ---
        public float StarvedAgentSeconds;          // Food < critical
        public float StallWaitSecondsP95Estimate;  // maintained via sampling
        public int   StallWaitSamples;
        public float StallWaitSum, StallWaitSumSq;
        public int   UnemployedTicks;
        public int   PossibleWorkerTicks;
        public float PriceVarCpiLike;              // running variance proxy
        public int   PriceVarSamples;
        public int   MoneyResidualAbsMax;          // track worst audit residual
        public int   ItemResidualAbsMax;


        public void Advance(int tickInc, float dt)
        {
            Tick += tickInc;
            SimTime += dt;
        }
    }
}