using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class CancelOrderRequest
    {
        [DataMember] internal readonly string command = "submit";
        [DataMember] internal CaOR_TxJson tx_json;
        [DataMember] internal string secret;
    }

    [DataContract]
    internal class CaOR_TxJson
    {
        [DataMember] internal readonly string TransactionType = "OfferCancel";
        [DataMember] internal string Account;
        [DataMember] internal string OfferSequence;
    }
}
