namespace PortTown01.Econ
{
    public enum Side { Buy, Sell }

    public class Offer
    {
        public int Id;
        public int AgentId;
        public PortTown01.Core.ItemType Item;
        public Side Side;
        public int Qty;           // remaining qty
        public int UnitPrice;     // coins per unit
        public int PostTick;
        public int ExpiryTick;    // -1 = never

        // escrow (week 1)
        public int EscrowCoins;   // for bids
        public int EscrowItems;   // for asks
    }
}
