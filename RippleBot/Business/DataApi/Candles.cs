using System;
using System.Collections.Generic;
using System.Runtime.Serialization;


namespace RippleBot.Business.DataApi
{
    [DataContract]
    internal class CandlesRequest
    {
        [DataMember] internal Base @base { get; set; }
        [DataMember] internal Counter counter { get; set; }
        [DataMember] internal string startTime { get; set; }
        [DataMember] internal string endTime { get; set; }
        [DataMember] internal string timeIncrement { get; set; }
        [DataMember] internal int timeMultiple { get; set; }
        [DataMember] internal string format { get; set; }
    }

    [DataContract]
    internal class CandlesResponse
    {
        [DataMember] internal string startTime { get; set; }
        [DataMember] internal string endTime { get; set; }
        [DataMember] internal Base @base { get; set; }
        [DataMember] internal Counter counter { get; set; }
        [DataMember] internal string timeIncrement { get; set; }
        [DataMember] internal int timeMultiple { get; set; }
        [DataMember] internal List<Candle> results { get; set; }
    }

    [DataContract]
    internal class Base
    {
        [DataMember] internal string currency { get; set; }
    }

    [DataContract]
    internal class Counter
    {
        [DataMember] internal string currency { get; set; }
        [DataMember] internal string issuer { get; set; }
    }

    [DataContract]
    internal class Candle
    {
        [DataMember] internal string startTime { get; set; }
        [DataMember] internal string openTime { get; set; }
        [DataMember] internal string closeTime { get; set; }
        [DataMember] internal double baseVolume { get; set; }
        [DataMember] internal double counterVolume { get; set; }
        [DataMember] internal int count { get; set; }
        [DataMember] internal double open { get; set; }
        [DataMember] internal double high { get; set; }
        [DataMember] internal double low { get; set; }
        [DataMember] internal double close { get; set; }
        [DataMember] internal double vwap { get; set; }
        [DataMember] internal bool partial { get; set; }

        internal DateTime StartTime
        {
            get { return DateTime.Parse(startTime); }
        }
    }
}
