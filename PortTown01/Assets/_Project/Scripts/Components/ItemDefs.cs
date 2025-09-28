namespace PortTown01.Core
{
    public static class ItemDefs
    {
        public static float KgPerUnit(ItemType t)
        {
            switch (t)
            {
                case ItemType.Log:   return 5f;
                case ItemType.Plank: return 2f;
                case ItemType.Crate: return 10f;
                case ItemType.Food:  return 0f;   // weightless for now to avoid carry conflicts at stall
                case ItemType.Coin:  return 0f;
                default:             return 1f;
            }
        }
    }
}
