using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Common.Business;


namespace BtceBot.Business
{
    [DataContract]
    internal class MarketDepthResponse : IMarketDepthResponse<MarketOrder>
    {
        [DataMember] internal List<List<double>> asks { get; set; }
        [DataMember] internal List<List<double>> bids { get; set; }


        private const int LIST_LIMIT = 15;
        private List<MarketOrder> _asks;
        private List<MarketOrder> _bids;


        public List<MarketOrder> Asks
        {
            get
            {
                if (null == _asks)
                {
                    _asks = new List<MarketOrder>();
                    foreach (var ask in asks.Take(LIST_LIMIT))
                        _asks.Add(new MarketOrder{ Price = ask[0], Amount = ask[1] });
                }

                return _asks;
            }
        }

        public List<MarketOrder> Bids
        {
            get
            {
                if (null == _bids)
                {
                    _bids = new List<MarketOrder>();
                    foreach (var bid in bids.Take(LIST_LIMIT))
                        _bids.Add(new MarketOrder{ Price = bid[0], Amount = bid[1] });
                }

                return _bids;
            }
        }
    }

    internal class MarketOrder : IMarketOrder
    {
        public double Amount { get; set; }
        public double Price { get; set; }
    }
}
