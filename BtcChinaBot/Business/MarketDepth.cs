﻿using System;
using System.Collections.Generic;
using System.Runtime.Serialization;


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
        [DataMember] internal List<Bid> bid { get; set; }
        [DataMember] internal List<Ask> ask { get; set; }
        [DataMember] internal int date { get; set; }

        internal DateTime ServerTime
        {
            //2hrs offset
            get { return new DateTime(1970, 1, 1).AddSeconds(date).AddHours(2); }
        }
    }

    [DataContract]
    internal class Bid
    {
        [DataMember] internal double price { get; set; }
        [DataMember] internal double amount { get; set; }
    }

    [DataContract]
    internal class Ask
    {
        [DataMember] internal double price { get; set; }
        [DataMember] internal double amount { get; set; }
    }
}
