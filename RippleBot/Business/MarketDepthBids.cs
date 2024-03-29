﻿using System.Collections.Generic;
using System.Runtime.Serialization;
using Common.Business;


namespace RippleBot.Business
{
    [DataContract]
    internal class MarketDepthBidsResponse
    {
        [DataMember] internal int id { get; set; }
        [DataMember] internal Bids result { get; set; }
        [DataMember] internal string status { get; set; }
        [DataMember] internal string type { get; set; }
    }

    [DataContract]
    internal class Bids
    {
        [DataMember] internal int ledger_current_index { get; set; }
        [DataMember] internal List<Bid> offers { get; set; }
        [DataMember] internal bool validated { get; set; }
    }

    [DataContract]
    public class Bid : IMarketOrder
    {
        [DataMember] internal string Account { get; set; }
        [DataMember] internal string BookDirectory { get; set; }
        [DataMember] internal string BookNode { get; set; }
        [DataMember] internal int Flags { get; set; }
        [DataMember] internal string LedgerEntryType { get; set; }
        [DataMember] internal string OwnerNode { get; set; }
        [DataMember] internal string PreviousTxnID { get; set; }
        [DataMember] internal int PreviousTxnLgrSeq { get; set; }
        [DataMember] internal int Sequence { get; set; }
        [DataMember] internal Take TakerGets { get; set; }
        [DataMember] internal string TakerPays { get; set; }
        [DataMember] internal string index { get; set; }
        [DataMember] internal string quality { get; set; }
        [DataMember] internal int? Expiration { get; set; }
        [DataMember] internal Take taker_gets_funded { get; set; }
        [DataMember] internal string taker_pays_funded { get; set; }

        /// <summary>Amount of XRP to buy</summary>
        public double Amount
        {
            get
            {
                return double.Parse(TakerPays) / Const.DROPS_IN_XRP;
            }
        }

        /// <summary>Price of one XRP in USD</summary>
        public double Price
        {
            get
            {
                var dollars = double.Parse(TakerGets.value);
                return dollars / Amount;
            }
        }
    }
}
