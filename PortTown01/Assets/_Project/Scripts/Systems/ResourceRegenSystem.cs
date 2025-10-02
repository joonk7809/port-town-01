using System.Collections.Generic;
using UnityEngine;
using PortTown01.Core;

namespace PortTown01.Systems
{
    /// <summary>
    /// Regenerates ResourceNode.Stock based on RegenPerSec, capped by MaxStock.
    /// Uses per-node fractional carry so integer Stock can accumulate from sub-unit rates.
    /// Runs every tick (uses dt), so it stays smooth at any sim rate.
    /// </summary>
    public sealed class ResourceRegenSystem : ISimSystem
    {
        public string Name => "ResourceRegen";

        // fractional carry per node id
        private readonly Dictionary<int, float> _frac = new Dictionary<int, float>(8);

        public void Tick(World world, int tick, float dt)
        {
            var nodes = world.ResourceNodes;
            if (nodes == null || nodes.Count == 0) return;

            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (n.RegenPerSec <= 0f) { _frac[n.Id] = 0f; continue; }
                if (n.Stock >= n.MaxStock) { _frac[n.Id] = 0f; continue; }

                float carry = 0f;
                if (!_frac.TryGetValue(n.Id, out carry)) carry = 0f;

                carry += n.RegenPerSec * dt;

                int units = Mathf.FloorToInt(carry);
                if (units > 0)
                {
                    int space = n.MaxStock - n.Stock;
                    int add = Mathf.Min(units, space);
                    if (add > 0) n.Stock += add;
                    carry -= add; // keep remainder only after what actually fit
                }

                _frac[n.Id] = Mathf.Max(0f, carry);
            }
        }
    }
}
