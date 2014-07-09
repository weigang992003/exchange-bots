using System;
using System.Runtime.Serialization;


namespace BitfinexBot.Business
{
    [DataContract]
    internal class TickerResponse
    {
        [DataMember] internal string mid { get; set; }
        [DataMember] internal string bid { get; set; }
        [DataMember] internal string ask { get; set; }
        [DataMember] internal string last_price { get; set; }
        [DataMember] internal string low { get; set; }
        [DataMember] internal string high { get; set; }
        [DataMember] internal string volume { get; set; }
        [DataMember] internal string timestamp { get; set; }

        /// <summary>The difference to CEST use to be -2 hrs</summary>
        internal DateTime ServerTime
        {
            get
            {
                var seconds = double.Parse(timestamp);
                return new DateTime(1970, 1, 1).AddSeconds(seconds);
            }
        }
    }
}
