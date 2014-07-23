using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class MarketDepthRequest
    {
        [DataMember] internal int id;
        [DataMember] internal readonly string command = "book_offers";
        [DataMember] internal Take taker_pays;
        [DataMember] internal Take taker_gets;
        [DataMember] internal int limit = 15;
    }
}
