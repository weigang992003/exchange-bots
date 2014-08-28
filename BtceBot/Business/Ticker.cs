using System;
using System.Runtime.Serialization;


namespace BtceBot.Business
{
    [DataContract]
    internal class TickerResponse
    {
        [DataMember] internal Ticker ticker { get; set; }
    }
    
    [DataContract]
    internal class Ticker
    {
        [DataMember] internal double high { get; set; }
        [DataMember] internal double low { get; set; }
        [DataMember] internal double avg { get; set; }
        [DataMember] internal double vol { get; set; }
        [DataMember] internal double vol_cur { get; set; }
        [DataMember] internal double last { get; set; }
        [DataMember] internal double buy { get; set; }
        [DataMember] internal double sell { get; set; }
        [DataMember] internal int updated { get; set; }
        [DataMember] internal int server_time { get; set; }


        internal DateTime ServerTime
        {
            get
            {
                return new DateTime(1970, 1, 1).AddSeconds(server_time);
            }
        }
    }
}
