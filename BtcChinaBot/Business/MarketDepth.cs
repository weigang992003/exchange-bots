using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Common.Business;


namespace BtcChinaBot.Business
{
    [DataContract]
    internal class MarketDepthResponse
    {
        [DataMember] internal MarketDepthResult result { get; set; }
        [DataMember] internal string id { get; set; }
    }

    [DataContract]
    internal class MarketDepthResult
    {
        [DataMember] internal MarketDepth market_depth { get; set; }
    }

    [DataContract]
    internal class MarketDepth
    {
        [DataMember] internal List<MarketOrder> bid { get; set; }
        [DataMember] internal List<MarketOrder> ask { get; set; }
        [DataMember] internal int date { get; set; }

        internal DateTime ServerTime
        {
            //2hrs offset
            get { return new DateTime(1970, 1, 1).AddSeconds(date).AddHours(2); }
        }
    }

    [DataContract]
    internal class MarketOrder : IMarketOrder
    {
        [DataMember] internal double price { get; set; }
        [DataMember] internal double amount { get; set; }

        #region IMarketOrder implementations
        public double Price { get { return price; } }
        public double Amount { get { return amount; } }
        #endregion
    }
}
