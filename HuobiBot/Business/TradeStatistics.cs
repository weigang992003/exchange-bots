using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;
using Common;


namespace HuobiBot.Business
{
    [DataContract]
    internal class TradeStatisticsResponse
    {
        [DataMember] internal List<Sell> sells { get; set; }
        [DataMember] internal List<Buy> buys { get; set; }
        [DataMember] internal List<Trade> trades { get; set; }
        [DataMember] internal double p_new { get; set; }
        [DataMember] internal double level { get; set; }
        [DataMember] internal double amount { get; set; }
        [DataMember] internal double total { get; set; }
        [DataMember] internal int amp { get; set; }
        [DataMember] internal double p_open { get; set; }
        [DataMember] internal double p_high { get; set; }
        [DataMember] internal double p_low { get; set; }
        [DataMember] internal double p_last { get; set; }
        [DataMember] internal List<TopSell> top_sell { get; set; }
        [DataMember] internal List<TopBuy> top_buy { get; set; }
    }

    [DataContract]
    internal class Sell
    {
        [DataMember] internal string price { get; set; }
        [DataMember] internal int level { get; set; }
        [DataMember] internal double amount { get; set; }

        internal double Price
        {
            get { return double.Parse(price); }
        }
    }

    [DataContract]
    internal class Buy
    {
        [DataMember] internal string price { get; set; }
        [DataMember] internal int level { get; set; }
        [DataMember] internal double amount { get; set; }

        internal double Price
        {
            get { return double.Parse(price); }
        }
    }

    [DataContract]
    internal class Trade
    {
        private const string SELL_TYPE_CODE = "\u5356\u51fa";
        private const string BUY_TYPE_CODE = "\u4e70\u5165";

        [DataMember] internal string time { get; set; }
        [DataMember] internal double price { get; set; }
        [DataMember] internal double amount { get; set; }
        [DataMember] internal string type { get; set; }


        private DateTime? _timeTyped;
        /// <summary>Date and time of the trade</summary>
        internal DateTime TimeTyped
        {
            get
            {
                if (null == _timeTyped)
                    _timeTyped = DateTime.Today.Add(TimeSpan.ParseExact(time, "hh\\:mm\\:ss", CultureInfo.InvariantCulture));

                return _timeTyped.Value;
            }
        }


        internal TradeType Type
        {
            get
            {
                if (SELL_TYPE_CODE == type)
                    return TradeType.SELL;
                if (BUY_TYPE_CODE == type)
                    return TradeType.BUY;
                throw new InvalidOperationException("Unrecognized trade type code " + type);
            }
        }
    }

    [DataContract]
    internal class TopSell
    {
        [DataMember] internal string price { get; set; }
        [DataMember] internal int level { get; set; }
        [DataMember] internal double amount { get; set; }
        [DataMember] internal double accu { get; set; }
    }

    [DataContract]
    internal class TopBuy
    {
        [DataMember] internal string price { get; set; }
        [DataMember] internal int level { get; set; }
        [DataMember] internal double amount { get; set; }
        [DataMember] internal double accu { get; set; }
    }
}
