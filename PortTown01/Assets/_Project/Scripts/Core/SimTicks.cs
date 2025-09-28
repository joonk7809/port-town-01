namespace PortTown01.Core
{
    public static class SimTicks
    {
        // With fixedDelta=0.05s (20 Hz), once per second is every 20 ticks.
        public static bool Every1Hz(int tick) => (tick % 20) == 0;
    }
}
