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

        public void Advance(int tickInc, float dt)
        {
            Tick += tickInc;
            SimTime += dt;
        }
    }
}