using System.Runtime.Serialization;


namespace BtceBot.Business
{
    [DataContract]
    internal class NewOrderResponse
    {
        [DataMember] internal int success { get; set; }
        [DataMember] internal NewOrderInfo @return { get; set; }
    }

    [DataContract]
    internal class NewOrderInfo
    {
        [DataMember] internal double received { get; set; }
        [DataMember] internal double remains { get; set; }
        [DataMember] internal int order_id { get; set; }
        [DataMember] internal Funds funds { get; set; }
    }
}
