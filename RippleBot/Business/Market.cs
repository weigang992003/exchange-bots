using System.Collections.Generic;
using Common.Business;


namespace RippleBot.Business
{
    public class Market //todo : IMarketDepthResponse
    {
        public List<Ask> Asks { get; set; }
        public List<Bid> Bids { get; set; }
    }
}
