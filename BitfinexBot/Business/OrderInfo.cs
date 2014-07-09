using System;
using System.Runtime.Serialization;


namespace BitfinexBot.Business
{
    [DataContract]
    internal class OrderInforResponse
    {
        [DataMember] internal int id { get; set; }
        [DataMember] internal string symbol { get; set; }
        [DataMember] internal object exchange { get; set; }
        /// <summary>NULL for market orders</summary>
        [DataMember] internal string price { get; set; }
        [DataMember] internal string avg_execution_price { get; set; }
        [DataMember] internal string side { get; set; }
        [DataMember] internal string type { get; set; }
        [DataMember] internal string timestamp { get; set; }
        [DataMember] internal bool is_live { get; set; }
        [DataMember] internal bool is_cancelled { get; set; }
        [DataMember] internal bool was_forced { get; set; }
        [DataMember] internal string original_amount { get; set; }
        [DataMember] internal string remaining_amount { get; set; }
        [DataMember] internal string executed_amount { get; set; }

        /// <summary>Remaining LTC amount</summary>
        internal double Amount
        {
            get
            {
                if (String.IsNullOrEmpty(remaining_amount))
                    return 0.0;
                return double.Parse(remaining_amount);
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
                if (is_live)
                    return OrderStatus.Open;
                if (is_cancelled)
                    return OrderStatus.Cancelled;
                return OrderStatus.Closed;
            }
        }
    }

    internal enum OrderStatus
    {
        Open,
        Closed,
        Cancelled
    }
}
