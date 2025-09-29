using System;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Econ
{
    public enum LedgerWriter
    {
        OrderMatch      = 1,
        WagePayout      = 2,
        DockSale        = 3,
        CityAllocation  = 4,
        WholesaleBuy    = 5,
        MintExternal    = 6,
        BurnExternal    = 7
    }

    public static class Ledger
    {
        // Internal coin move (must net to zero)
        public static void Transfer(World world, ref int from, ref int to, int amount, LedgerWriter writer, string note = "")
        {
            if (amount < 0)
            {
                Debug.LogError($"[LEDGER] Negative transfer {amount} by {writer} {note}");
                return;
            }
            if (amount == 0) return;

            int beforeFrom = from, beforeTo = to;
            from -= amount;
            to   += amount;

#if UNITY_EDITOR
            if (from < 0)
                Debug.LogWarning($"[LEDGER] From-balance went negative by {writer} {note}: before={beforeFrom}, amount={amount}");
#endif
        }

        // External mint (source) → increases city + inflow
        public static void MintToCity(World world, int amount, LedgerWriter writer, string note = "")
        {
            if (amount <= 0) return;
            world.CityBudget          += amount;
            world.CoinsExternalInflow += amount;
        }

        // External burn (sink) → decreases city + outflow
        public static void BurnFromCity(World world, int amount, LedgerWriter writer, string note = "")
        {
            if (amount <= 0) return;
            world.CityBudget           -= amount;
            world.CoinsExternalOutflow += amount;
        }
    }
}
