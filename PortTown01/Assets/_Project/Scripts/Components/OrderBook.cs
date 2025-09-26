using System.Collections.Generic;

namespace PortTown01.Econ
{
    public class OrderBook
    {
        public readonly List<Offer> Bids = new();
        public readonly List<Offer> Asks = new();
        public int NextOfferId = 1;
    }
}
