using System.Collections.Generic;
using System.Runtime.Serialization;


namespace BitfinexBot.Business
{
    [DataContract]
    internal class MarketDepthResponse
    {
        [DataMember] internal List<Order> bids { get; set; }
        [DataMember] internal List<Order> asks { get; set; }
    }

    [DataContract]
    internal class Order
    {
        [DataMember] internal string price { get; set; }
        [DataMember] internal string amount { get; set; }
        [DataMember] internal string timestamp { get; set; }

        internal double Price
        {
            get { return double.Parse(price); }
        }

        internal double Amount
        {
            get { return double.Parse(amount); }
        }
    }

/*    [DataContract]
    internal class Bid
    {
        [DataMember] internal string price { get; set; }
        [DataMember] internal string amount { get; set; }
        [DataMember] internal string timestamp { get; set; }
    }

    [DataContract]
    internal class Ask
    {
        [DataMember] internal string price { get; set; }
        [DataMember] internal string amount { get; set; }
        [DataMember] internal string timestamp { get; set; }
    }*/
}
