using System.Runtime.Serialization;

namespace BtcChinaBot.Business
{
    [DataContract]
    internal class CancelOrderResponse
    {
        [DataMember] internal bool result { get; set; }
        [DataMember] internal string id { get; set; }
    }
}
