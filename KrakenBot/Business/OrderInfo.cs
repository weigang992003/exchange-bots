using System;
using System.Collections.Generic;
using System.Runtime.Serialization;


namespace KrakenBot.Business
{
    [DataContract]
    internal class OrderInfoResponse
    {
        [DataMember] internal List<object> error { get; set; }
        [DataMember] internal OrderInfoResult result { get; set; }
    }

    [DataContract]
    internal class OrderInfoResult
    {
        [DataMember] internal OrderData orderData { get; set; }
    }

    [DataContract]
    internal class OrderData
    {
        [DataMember] internal object refid { get; set; }
        [DataMember] internal object userref { get; set; }
        [DataMember] internal string status { get; set; }
        [DataMember] internal object reason { get; set; }
        [DataMember] internal double opentm { get; set; }
        [DataMember] internal double closetm { get; set; }
        [DataMember] internal string starttm { get; set; }
        [DataMember] internal string expiretm { get; set; }
        [DataMember] internal Descr descr { get; set; }
        [DataMember] internal string vol { get; set; }
        [DataMember] internal string vol_exec { get; set; }
        [DataMember] internal string cost { get; set; }
        [DataMember] internal string fee { get; set; }
        [DataMember] internal string price { get; set; }
        [DataMember] internal string misc { get; set; }
        [DataMember] internal string oflags { get; set; }
        [DataMember] internal List<string> trades { get; set; }

        /// <summary>Remaining amount of this order</summary>
        internal double Amount
        {
            get
            {
                double executed = String.IsNullOrEmpty(vol_exec)
                    ? 0.0
                    : double.Parse(vol_exec);
                return double.Parse(vol) - executed;
            }
        }

        internal double Price
        {
            get { return double.Parse(price); }
        }

        internal OrderStatus Status
        {
            get
            {
                switch (status.ToLower())
                {
                    case "open":
                        return OrderStatus.Open;
                    case "closed":
                        return OrderStatus.Closed;
                    case "pending":
                        return OrderStatus.Pending;
                    case "cancelled":
                    case "canceled":
                        return OrderStatus.Cancelled;
                    case "expired":
                        return OrderStatus.Expired;
                    default:
                        throw new Exception("Unrecognized order status " + status);
                }
            }
        }
    }

    [DataContract]
    internal class Descr
    {
        [DataMember] internal string pair { get; set; }
        [DataMember] internal string type { get; set; }
        [DataMember] internal string ordertype { get; set; }
        [DataMember] internal string price { get; set; }
        [DataMember] internal string price2 { get; set; }
        [DataMember] internal string leverage { get; set; }
        [DataMember] internal string order { get; set; }
    }

    internal enum OrderStatus
    {
        Open,
        Closed,
        Pending,
        Cancelled,
        Expired
    }
}
