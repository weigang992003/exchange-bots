using System.Collections.Generic;
using System.Runtime.Serialization;
using Common.Business;


namespace RippleBot.Business
{
    [DataContract]
    internal class MarketDepthAsksResponse
    {
        [DataMember] internal int id { get; set; }
        [DataMember] internal Asks result { get; set; }
        [DataMember] internal string status { get; set; }
        [DataMember] internal string type { get; set; }
    }

    [DataContract]
    internal class Asks
    {
        [DataMember] internal int ledger_current_index { get; set; }
        [DataMember] internal List<Ask> offers { get; set; }
        [DataMember] internal bool validated { get; set; }
    }

    [DataContract]
    public class Ask : IMarketOrder
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
        [DataMember] internal string TakerGets { get; set; }
        [DataMember] internal Take TakerPays { get; set; }
        [DataMember] internal string index { get; set; }
        [DataMember] internal string quality { get; set; }
        [DataMember] internal int? Expiration { get; set; }
        [DataMember] internal string taker_gets_funded { get; set; }
        [DataMember] internal Take taker_pays_funded { get; set; }

        /// <summary>Amount of XRP to sell</summary>
        public double Amount
        {
            get
            {
                return double.Parse(TakerGets) / 1000000.0;
            }
        }

        public double Price
        {
            get
            {
                var dollars = double.Parse(TakerPays.value);
                return dollars / Amount;
            }
        }
    }
}
