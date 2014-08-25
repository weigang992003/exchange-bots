using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Common;


namespace HuobiBot.Business
{
    [DataContract]
    internal class MarketDepthResponse
    {
        [DataMember] internal List<List<object>> asks { get; set; }
        [DataMember] internal List<List<object>> bids { get; set; }

        private const int ORDER_LIST_LENGTH = 15;

        private List<Order> _asks;

        /// <summary>Typed ASKs</summary>
        internal List<Order> Asks
        {
            get
            {
                if (null == _asks)
                {
                    _asks = new List<Order>();

                    foreach (var ask in asks.TakeLast(ORDER_LIST_LENGTH).Reverse())
                        _asks.Add(new Order {Price = Convert.ToDouble(ask[0]), Amount = Convert.ToDouble(ask[1])});
                }

                return _asks;
            }
        }


        private List<Order> _bids;

        /// <summary>Typed BIDss</summary>
        internal List<Order> Bids
        {
            get
            {
                if (null == _bids)
                {
                    _bids = new List<Order>();

                    foreach (var bid in bids.Take(ORDER_LIST_LENGTH))
                        _bids.Add(new Order {Price = Convert.ToDouble(bid[0]), Amount = Convert.ToDouble(bid[1])});
                }

                return _bids;
            }
        }

        /// <summary>
        /// True if this market object contains enough of data to base market analysis and decisions on
        /// </summary>
        internal bool IsValid
        {
            get
            {
                return null != asks && null != bids && asks.Count >= ORDER_LIST_LENGTH && bids.Count >= ORDER_LIST_LENGTH;
            }
        }
    }


    internal class Order
    {
        internal double Price { get; set; }
        internal double Amount { get; set; }
    }
}
