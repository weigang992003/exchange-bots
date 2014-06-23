using System.Runtime.Serialization;


namespace BtcChinaBot.Business
{
    [DataContract]
    internal class SellOrderResponse
    {
        [DataMember] internal int result { get; set; }
        [DataMember] internal string id { get; set; }
    }
}
