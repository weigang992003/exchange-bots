using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class MarketDepthRequest
    {
        [DataMember] internal int id { get; set; }
        [DataMember] internal string command { get; set; }
        [DataMember] internal Take taker_pays { get; set; }
        [DataMember] internal Take taker_gets { get; set; }
        [DataMember] internal int limit { get; set; }
    }
}
