using System.Runtime.Serialization;


namespace HuobiBot.Business
{
    [DataContract]
    internal class OrderInfoResponse
    {
        [DataMember] internal int id { get; set; }
        [DataMember] internal int type
        {
            get { return (int) Type; }
            set { Type = (OrderType) value; }
        }

        [DataMember]
        internal string order_price
        {
            get { return Price.ToString(); }
            set {  Price = double.Parse(value); }
        }

        [DataMember]
        internal string order_amount
        {
            get { return Amount.ToString(); }
            set { Amount = double.Parse(value); }
        }

        [DataMember] internal string processed_amount
        {
            get { return ProcessedAmount.ToString(); }
            set { ProcessedAmount = double.Parse(value); }
        }
        [DataMember] internal string processed_price { get; set; }
        [DataMember] internal string total { get; set; }
        [DataMember] internal string fee { get; set; }
        [DataMember] internal string vot { get; set; }

        [DataMember]
        internal int status
        {
            get { return (int) Status; }
            set { Status = (OrderStatus) value; }
        }

        internal OrderType Type { get; private set; }
        internal double Price { get; private set; }
        internal double Amount { get; private set; }
        internal double ProcessedAmount { get; private set; }
        internal OrderStatus Status { get; private set; }
    }

    internal enum OrderStatus
    {
        Unfilled = 0,
        PartiallyFilled = 1,
        Finished = 2,
        Cancelled = 3
    }

    internal enum OrderType
    {
        Buy = 1,
        Sell = 2
    }
}
