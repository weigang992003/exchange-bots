using System.Collections.Generic;
using System.Runtime.Serialization;
using Common.Business;


namespace BitfinexBot.Business
{
    [DataContract]
    internal class MarketDepthResponse
    {
        [DataMember] internal List<Order> bids { get; set; }
        [DataMember] internal List<Order> asks { get; set; }
    }

    [DataContract]
    internal class Order : IMarketOrder
    {
        [DataMember] internal string price { get; set; }
        [DataMember] internal string amount { get; set; }
        [DataMember] internal string timestamp { get; set; }


        #region IMarketOrder implementations

        public double Price
        {
            get { return double.Parse(price); }
        }

        public double Amount
        {
            get { return double.Parse(amount); }
        }
        #endregion
    }
}
