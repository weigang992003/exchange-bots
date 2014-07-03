using System;
using System.Collections.Generic;
using System.Runtime.Serialization;


namespace KrakenBot.Business
{
    [DataContract]
    internal class BalanceResponse
    {
        [DataMember] internal List<object> error { get; set; }
        [DataMember] internal BalanceResult result { get; set; }
    }

    [DataContract]
    internal class BalanceResult
    {
        [DataMember] internal string ZEUR { get; set; }
        [DataMember] internal string XXBT { get; set; }
        [DataMember] internal string XXRP { get; set; }
        [DataMember] internal string XLTC { get; set; }

        internal double BalanceEur
        {
            get { return String.IsNullOrEmpty(ZEUR) ? 0.0 : double.Parse(ZEUR); }
        }

        internal double BalanceBtc
        {
            get { return String.IsNullOrEmpty(XXBT) ? 0.0 : double.Parse(XXBT); }
        }
    }
}
