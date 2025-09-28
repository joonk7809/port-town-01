using System.Linq;
using UnityEngine;
using PortTown01.Core;
using PortTown01.Econ;

namespace PortTown01.Systems
{
    // Last-pass corrector: recompute Kg from items, clamp negatives, basic escrow sanity.
    public class GuardrailsSystem : ISimSystem
    {
        public string Name => "Guards";

        public void Tick(World world, int _, float dt)
        {
            // Agents
            foreach (var a in world.Agents)
            {
                if (a.Coins < 0) a.Coins = 0;

                // recompute carried kg from items
                float expected = 0f;
                foreach (var kv in a.Carry.Items)
                {
                    if (kv.Value < 0) a.Carry.Items[kv.Key] = 0;
                    expected += ItemDefs.KgPerUnit(kv.Key) * Mathf.Max(0, kv.Value);
                }
                a.Carry.Kg = expected; // authoritative

                // (optional) warn if overweight; we don't forcibly drop items
                if (a.Carry.Kg > a.CapacityKg + 1e-3f)
                    Debug.LogWarning($"[GUARD] Agent#{a.Id} overweight {a.Carry.Kg:F1}>{a.CapacityKg:F1}");
            }

            // Buildings
            foreach (var b in world.Buildings)
            {
                if (b.Storage.Kg < 0) b.Storage.Kg = 0;
                float expected = 0f;
                foreach (var kv in b.Storage.Items.ToList())
                {
                    if (kv.Value < 0) b.Storage.Items[kv.Key] = 0;
                    expected += ItemDefs.KgPerUnit(kv.Key) * Mathf.Max(0, kv.Value);
                }
                b.Storage.Kg = expected;
            }

            // Order book sanity
            foreach (var bid in world.FoodBook.Bids.Where(o=>o.Qty>0))
                if (bid.EscrowCoins < 0) bid.EscrowCoins = 0;
            foreach (var ask in world.FoodBook.Asks.Where(o=>o.Qty>0))
                if (ask.EscrowItems < 0) ask.EscrowItems = 0;
        }
    }
}
