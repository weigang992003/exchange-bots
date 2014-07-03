using System;
using System.Collections.Generic;
using System.Runtime.Serialization;


namespace KrakenBot.Business
{
    [DataContract]
    internal class MarketDepthResponse
    {
        [DataMember] internal List<object> error { get; set; }
        [DataMember] internal MarketDepth result { get; set; }
    }

    [DataContract]
    internal class MarketDepth
    {
        [DataMember] internal OrderList XXBTZEUR { get; set; }
    }

    [DataContract]
    internal class OrderList
    {
        [DataMember] internal List<List<object>> asks { get; set; }
        [DataMember] internal List<List<object>> bids { get; set; }

        private List<Order> _asks;

        internal List<Order> Asks
        {
            get
            {
                if (null == _asks)
                {
                    _asks = new List<Order>();
                    foreach (var ask in asks)
                        _asks.Add(new Order { Price = Convert.ToDouble((string)ask[0]), Amount = Convert.ToDouble((string)ask[1]) });
                }

                return _asks;
            }
        }

        private List<Order> _bids;

        internal List<Order> Bids
        {
            get
            {
                if (null == _bids)
                {
                    _bids = new List<Order>();
                    foreach (var bid in bids)
                        _bids.Add(new Order { Price = Convert.ToDouble((string)bid[0]), Amount = Convert.ToDouble((string)bid[1]) });
                }

                return _bids;
            }
        }
    }

    internal struct Order
    {
        internal double Price;
        internal double Amount;
    }
}
