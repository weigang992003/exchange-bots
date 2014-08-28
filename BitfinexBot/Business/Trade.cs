using System;
using System.Runtime.Serialization;
using Common;


namespace BitfinexBot.Business
{
    [DataContract]
    internal class Trade
    {
        [DataMember] internal int timestamp { get; set; }
        [DataMember] internal int tid { get; set; }

        private double _price;

        [DataMember]
        internal string price
        {
            get { return _price.ToString(); }
            set { _price = double.Parse(value); }
        }

        private double _amount;

        [DataMember]
        internal string amount
        {
            get { return _amount.ToString(); }
            set { _amount = double.Parse(value); }
        }

        [DataMember] internal string exchange { get; set; }
        [DataMember] internal string type { get; set; }

        internal DateTime Time
        {
            get { return new DateTime(1970, 1, 1).AddSeconds(timestamp); }
        }

        internal double Price
        {
            get { return _price; }
            set { _price = value; }
        }

        internal double Amount
        {
            get { return _amount; }
            set { _amount = value; }
        }

        internal TradeType Type
        {
            get
            {
                if ("sell" == type)
                    return TradeType.SELL;
                if ("buy" == type)
                    return TradeType.BUY;
                throw new InvalidOperationException("Unknown trade type " + type);
            }
        }


        internal Trade()
        {
            //For serialization purposes
        }

        internal Trade(double price, double amount, TradeType type)
        {
            this.price = price.ToString();
            this.amount = amount.ToString();
            this.type = type.ToString().ToLower();
        }
    }
}
