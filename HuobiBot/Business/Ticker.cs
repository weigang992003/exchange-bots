using System;
using System.Runtime.Serialization;


namespace HuobiBot.Business
{
    [DataContract]
    internal class TickerResponse
    {
        [DataMember] internal Ticker ticker { get; set; }
        [DataMember] internal int time { get; set; }

        /// <summary>Chinese local time by Huobi server</summary>
        internal DateTime ServerTime
        {
            //For some reason ticker returns +16 hrs, so this must be corrected
            get { return new DateTime(1970, 1, 1).AddSeconds(time).AddHours(-16); }
        }
    }

    [DataContract]
    internal class Ticker
    {
        [DataMember] internal string high { get; set; }
        [DataMember] internal string low { get; set; }
        [DataMember] internal string last { get; set; }
        [DataMember] internal double vol { get; set; }
        [DataMember] internal string buy { get; set; }
        [DataMember] internal string sell { get; set; }
    }
}
