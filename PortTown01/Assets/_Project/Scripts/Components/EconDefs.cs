namespace PortTown01.Core
{
    public static class EconDefs
    {
        // Food (vendor asks / agent bids)
        public const int FOOD_PRICE_BASE = 5;
        public const int FOOD_PRICE_MIN  = 3;
        public const int FOOD_PRICE_MAX  = 12;

        // Crates (dock pays company)
        public const int CRATE_PRICE_BASE = 25;
        public const int CRATE_PRICE_MIN  = 15;
        public const int CRATE_PRICE_MAX  = 40;

        public const int WAGE_PER_LOG = 2;           // coins per delivered log
        public const int HAUL_PAY_PER_CRATE = 3;     // coins per crate hauled to dock
    }
} 