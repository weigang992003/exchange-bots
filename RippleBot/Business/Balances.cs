using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class BalancesResponse
    {
        [DataMember] internal bool success { get; set; }
        [DataMember] internal List<Balance> balances { get; set; }

        internal double AvailableXrp
        {
            get
            {
                return balances.First(bal => "XRP" == bal.currency).Available;
            }
        }

        internal double AvailableUsd
        {
            get
            {
                return balances.First(bal => "USD" == bal.currency).Available;
            }
        }
    }

    [DataContract]
    internal class Balance
    {
        [DataMember] internal string value { get; set; }
        [DataMember] internal string currency { get; set; }
        [DataMember] internal string counterparty { get; set; }


        internal double Available
        {
            get
            {
                if (String.IsNullOrEmpty(value))
                    return 0.0;
                return double.Parse(value);
            }
        }
    }
}
