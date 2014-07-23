using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class OrderInfoRequest
    {
        [DataMember] internal int id;
        [DataMember] internal readonly string command = "account_offers";
        [DataMember] internal string account;
    }
}
