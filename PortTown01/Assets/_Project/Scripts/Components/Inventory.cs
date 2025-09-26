using System.Collections.Generic;

namespace PortTown01.Core
{
    // Integer counts per item; track carried kg for movement later.
    public class Inventory
    {
        public readonly Dictionary<ItemType, int> Items = new();
        public float Kg = 0f;

        public int Get(ItemType t) => Items.TryGetValue(t, out var q) ? q : 0;

        public void Add(ItemType t, int qty)
        {
            if (qty <= 0) return;
            if (!Items.ContainsKey(t)) Items[t] = 0;
            Items[t] += qty;
        }

        public bool TryRemove(ItemType t, int qty)
        {
            if (qty <= 0) return true;
            int have = Get(t);
            if (have < qty) return false;
            Items[t] = have - qty;
            if (Items[t] == 0) Items.Remove(t);
            return true;
        }
    }
}
