using System.Collections.Generic;
using UnityEngine;

namespace PortTown01.Core
{
    // Integer counts per item; auto-maintains Kg using ItemDefs weights.
    public class Inventory
    {
        public readonly Dictionary<ItemType, int> Items = new();
        public float Kg = 0f;

        public int Get(ItemType t) => Items.TryGetValue(t, out var q) ? q : 0;

        // For building/storage (no capacity) or internal moves where you already checked capacity
        public void Add(ItemType t, int qty)
        {
            if (qty <= 0) return;
            if (!Items.ContainsKey(t)) Items[t] = 0;
            Items[t] += qty;
            Kg += ItemDefs.KgPerUnit(t) * qty;
        }

        // Capacity-aware add for agents
        public bool TryAdd(ItemType t, int qty, float capacityKg)
        {
            if (qty <= 0) return true;
            float addKg = ItemDefs.KgPerUnit(t) * qty;
            if (Kg + addKg > capacityKg + 1e-6f) return false;

            if (!Items.ContainsKey(t)) Items[t] = 0;
            Items[t] += qty;
            Kg += addKg;
            return true;
        }

        public bool TryRemove(ItemType t, int qty)
        {
            if (qty <= 0) return true;
            int have = Get(t);
            if (have < qty) return false;

            Items[t] = have - qty;
            if (Items[t] == 0) Items.Remove(t);
            Kg = Mathf.Max(0f, Kg - ItemDefs.KgPerUnit(t) * qty);
            return true;
        }
    }
}
