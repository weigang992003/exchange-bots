using System.Runtime.Serialization;


namespace BtcChinaBot.Business
{
    [DataContract]
    internal class OrderInfoResponse
    {
        [DataMember] internal OrderResult result { get; set; }
        [DataMember] internal string id { get; set; }
    }

    [DataContract]
    internal class OrderResult
    {
        [DataMember] internal Order order { get; set; }
    }

    [DataContract]
    internal class Order
    {
        [DataMember] internal int id { get; set; }
        [DataMember] internal string type { get; set; }
        [DataMember] internal double price { get; set; }
        [DataMember] internal string currency { get; set; }
        [DataMember] internal double amount { get; set; }
        [DataMember] internal double amount_original { get; set; }
        [DataMember] internal int date { get; set; }
        [DataMember] internal string status { get; set; }
    }

    /// <summary>Order statuses</summary>
    internal static class Status
    {
        internal const string OPEN = "open";
        internal const string CLOSED = "closed";
        internal const string CANCELLED = "cancelled";
        internal const string PENDING = "pending";
        internal const string ERROR = "error";
        internal const string INSUFFICIENT_BALANCE = "insufficient_balance";
    }
}
