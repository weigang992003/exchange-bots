using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class CancelResult
    {
        [DataMember] internal string engine_result { get; set; }
        [DataMember] internal int engine_result_code { get; set; }
        [DataMember] internal string engine_result_message { get; set; }
        [DataMember] internal string tx_blob { get; set; }
        [DataMember] internal object tx_json { get; set; }
    }

    [DataContract]
    internal class CancelOrderResponse
    {
        [DataMember] internal CancelResult result { get; set; }
        [DataMember] internal string status { get; set; }
        [DataMember] internal string type { get; set; }
    }
}
