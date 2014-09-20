using System.Collections.Generic;
using System.Runtime.Serialization;
using Common.Business;


namespace BtceBot.Business
{
    [DataContract]
    internal class Order : IMarketOrder
    {
        [DataMember] internal int id { get; set; }
        [DataMember] internal string pair { get; set; }
        [DataMember] internal string type { get; set; }
        [DataMember] internal double amount { get; set; }
        [DataMember] internal double rate { get; set; }
        [DataMember] internal int timestamp_created { get; set; }
        [DataMember] internal int status { get; set; }

        //convenience
        public double Price { get { return rate; } }

        public double Amount { get { return amount; } }

        /// <summary>True if this order was fully filled or cancelled</summary>
        internal bool Closed { get; private set; }

        internal Order()
        {
            //Serialization purposes
        }

        internal Order(bool closed)
        {
            Closed = closed;
        }
    }

    [DataContract]
    internal class OrderResponse
    {
        [DataMember] internal int success { get; set; }
        [DataMember] internal List<Order> @return { get; set; }
    }
}
