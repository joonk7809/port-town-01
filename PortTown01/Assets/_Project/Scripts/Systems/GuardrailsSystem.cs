using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    // Last-pass correctness: recompute Kg from items, surface negatives,
    // and repair bid-escrow underflows in a money-conserving way.
    public class GuardrailsSystem : ISimSystem
    {
        public string Name => "Guards";

        public void Tick(World world, int tick, float dt)
        {
            // ---------- Agents ----------
            foreach (var a in world.Agents)
            {
                // DO NOT silently clamp coins; surface the error and let Audit catch it.
                if (a.Coins < 0)
                {
                    Debug.LogError($"[GUARD][COINS] Agent#{a.Id} has negative coins: {a.Coins}.");
                    // Intentionally NOT clamping to avoid hiding the source.
                }

                // Recompute carried kg from items (authoritative mass)
                float kg = 0f;
                // copy keys to avoid "collection modified" if we fix negatives below
                var keys = a.Carry.Items.Keys.ToList();
                foreach (var it in keys)
                {
                    int v = a.Carry.Items[it];
                    if (v < 0)
                    {
                        Debug.LogError($"[GUARD][ITEM] Agent#{a.Id} had negative {it}: {v}. Forcing to 0.");
                        a.Carry.Items[it] = 0;
                        v = 0;
                    }
                    kg += ItemDefs.KgPerUnit(it) * v;
                }
                a.Carry.Kg = kg;

                if (a.Carry.Kg > a.CapacityKg + 1e-3f)
                    Debug.LogWarning($"[GUARD] Agent#{a.Id} overweight {a.Carry.Kg:F1} > {a.CapacityKg:F1}");
            }

            // ---------- Buildings ----------
            foreach (var b in world.Buildings)
            {
                float kg = 0f;
                var keys = b.Storage.Items.Keys.ToList();
                foreach (var it in keys)
                {
                    int v = b.Storage.Items[it];
                    if (v < 0)
                    {
                        Debug.LogError($"[GUARD][ITEM] Building#{b.Id} had negative {it}: {v}. Forcing to 0.");
                        b.Storage.Items[it] = 0;
                        v = 0;
                    }
                    kg += ItemDefs.KgPerUnit(it) * v;
                }
                b.Storage.Kg = Mathf.Max(0f, kg); // guard against tiny negatives
            }

            // ---------- Order book escrow sanity ----------
            // Bids: if escrow coins went negative due to a bug, "reimburse" bidder to conserve money, then zero the escrow.
            foreach (var bid in world.FoodBook.Bids)
            {
                if (bid.EscrowCoins < 0)
                {
                    var bidder = world.Agents.FirstOrDefault(a => a.Id == bid.AgentId);
                    int deficit = -bid.EscrowCoins;
                    Debug.LogError($"[GUARD][ESCROW] Bid#{bid.Id} negative escrow {bid.EscrowCoins}. "
                                 + $"Reimbursing bidder#{bid.AgentId} by {deficit} and zeroing escrow.");
                    if (bidder != null) bidder.Coins += deficit; // put coins back so totals remain conserved
                    bid.EscrowCoins = 0;
                }
            }

            // Asks: if escrow items went negative, surface and zero (cannot fabricate items to 'reimburse').
            foreach (var ask in world.FoodBook.Asks)
            {
                if (ask.EscrowItems < 0)
                {
                    Debug.LogError($"[GUARD][ESCROW] Ask#{ask.Id} negative item escrow {ask.EscrowItems}. Forcing to 0.");
                    ask.EscrowItems = 0;
                }
            }
        }
    }
}
