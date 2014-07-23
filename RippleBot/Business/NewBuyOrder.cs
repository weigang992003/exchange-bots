using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class NewBuyOrderResponse
    {
        [DataMember] internal BuyOrderData result { get; set; }
        [DataMember] internal string status { get; set; }
        [DataMember] internal string type { get; set; }
    }

    [DataContract]
    internal class BuyOrderData
    {
        [DataMember] internal string engine_result { get; set; }
        [DataMember] internal int engine_result_code { get; set; }
        [DataMember] internal string engine_result_message { get; set; }
        [DataMember] internal string tx_blob { get; set; }
        [DataMember] internal NBOR_TxJson tx_json { get; set; }
    }

    [DataContract]
    internal class NBOR_TxJson
    {
        [DataMember] internal string Account { get; set; }
        [DataMember] internal string Fee { get; set; }
        [DataMember] internal long Flags { get; set; }
        [DataMember] internal int Sequence { get; set; }
        [DataMember] internal string SigningPubKey { get; set; }
        [DataMember] internal Take TakerGets { get; set; }
        [DataMember] internal string TakerPays { get; set; }
        [DataMember] internal string TransactionType { get; set; }
        [DataMember] internal string TxnSignature { get; set; }
        [DataMember] internal string hash { get; set; }
    }
}
