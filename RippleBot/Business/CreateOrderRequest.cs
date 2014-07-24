using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class CreateOrderRequest
    {
        [DataMember] internal string command { get; set; }
        [DataMember] internal CrOR_TxJson tx_json { get; set; }
        [DataMember] internal string secret { get; set; }
    }

    [DataContract]
    internal class CrOR_TxJson
    {
        [DataMember] internal string TransactionType { get; set; }
        [DataMember] internal string Account { get; set; }
        [DataMember] internal string TakerPays { get; set; }
        [DataMember] internal Take TakerGets { get; set; }
    }
}
