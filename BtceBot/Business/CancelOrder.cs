using System.Runtime.Serialization;


namespace BtceBot.Business
{
    [DataContract]
    internal class CancelResponse
    {
        [DataMember] internal int success { get; set; }
        [DataMember] internal CancelData @return { get; set; }
    }

    [DataContract]
    internal class CancelData
    {
        [DataMember] internal int order_id { get; set; }
        [DataMember] internal Funds funds { get; set; }
    }
}
