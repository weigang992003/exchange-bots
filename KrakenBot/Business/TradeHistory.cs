using System;
using System.Collections.Generic;
using System.Runtime.Serialization;


namespace KrakenBot.Business
{
    [DataContract]
    internal class TradeHistoryResponse
    {
        [DataMember] internal List<object> error { get; set; }
        [DataMember] internal TradesHistory result { get; set; }
    }

    [DataContract]
    internal class TradesHistory
    {
        [DataMember] internal List<List<object>> XXBTZEUR { get; set; }
        [DataMember] internal string last { get; set; }

        private List<Trade> _trades;

        internal List<Trade> Trades
        {
            get
            {
                if (null == _trades)
                {
                    _trades = new List<Trade>();
                    foreach (var trade in XXBTZEUR)
                    {
                        _trades.Add(new Trade(
                            Convert.ToDouble((string)trade[0]),
                            Convert.ToDouble((string)trade[1]),
                            new DateTime(1970, 1, 1).AddSeconds((double)(decimal)trade[2]),
                            ((string)trade[3]) == "b" ? TradeType.BUY : TradeType.SELL
                            ));
                    }
                }

                return _trades;
            }
        }
    }

    internal class Trade
    {
        /// <summary>Price in EUR</summary>
        internal double Price;
        /// <summary>Amount of traded BTC</summary>
        internal double Amount;

        internal DateTime Time;
        internal TradeType Type;

        internal Trade(double price, double amount, DateTime time, TradeType type)
        {
            Price = price;
            Amount = amount;
            Type = type;
            Time = time;
        }
    }

    internal enum TradeType
    {
        BUY,
        SELL
    }
}
