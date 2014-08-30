using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Common;


namespace BtceBot.Business
{
    [DataContract]
    internal class TradeHistoryResponse
    {
        [DataMember] internal List<Trade> trades;
    }

    [DataContract]
    internal class Trade
    {
        [DataMember] internal int date { get; set; }
        [DataMember] internal double price { get; set; }
        [DataMember] internal double amount { get; set; }
        [DataMember] internal int tid { get; set; }
        [DataMember] internal string price_currency { get; set; }
        [DataMember] internal string item { get; set; }
        [DataMember] internal string trade_type { get; set; }


        internal DateTime Time
        {
            get { return new DateTime(1970, 1, 1).AddSeconds(date); }
        }

        internal TradeType Type
        {
            get
            {
                if ("ask" == trade_type)
                    return TradeType.SELL;
                if ("bid" == trade_type)
                    return TradeType.BUY;
                throw new InvalidOperationException("Unknown trade type " + trade_type);
            }
        }

        internal Trade()
        {
            //For serialization purposes
        }

        internal Trade(double price, double amount, TradeType type)
        {
            this.price = price;
            this.amount = amount;
            this.trade_type = type.ToString().ToLower();
        }
    }
}
