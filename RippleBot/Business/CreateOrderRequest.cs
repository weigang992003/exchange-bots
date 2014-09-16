using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class CreateBuyOrderRequest
    {
        [DataMember] internal readonly string command = "submit";
        [DataMember] internal CrOR_TxJson tx_json;
        [DataMember] internal string secret;
    }

    [DataContract]
    internal class CrOR_TxJson
    {
        [DataMember] internal readonly string TransactionType = "OfferCreate";
        [DataMember] internal string Account;
        [DataMember] internal string TakerPays;
        [DataMember] internal Take TakerGets;
    }




    [DataContract]
    internal class CreateSellOrderRequest
    {
        [DataMember] internal readonly string command = "submit";
        [DataMember] internal CSOR_TxJson tx_json;
        [DataMember] internal string secret;
    }

    [DataContract]
    internal class CSOR_TxJson
    {
        [DataMember] internal readonly string TransactionType = "OfferCreate";
        [DataMember] internal string Account;
        [DataMember] internal Take TakerPays;
        [DataMember] internal string TakerGets;

        [DataMember] internal readonly uint Flags = 2147483648;
    }
}
