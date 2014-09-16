using System.Runtime.Serialization;
using Common;
using System.Collections.Generic;


namespace RippleBot.Business
{
    [DataContract]
    internal class OffersResponse
    {
        [DataMember] internal int id { get; set; }
        [DataMember] internal OffersResult result { get; set; }
        [DataMember] internal string status { get; set; }
        [DataMember] internal string type { get; set; }
    }

    [DataContract]
    internal class OffersResult
    {
        [DataMember] internal string account { get; set; }
        [DataMember] internal List<Offer> offers { get; set; }
    }

    [DataContract]
    internal class Offer
    {
        [DataMember] internal int flags { get; set; }
        /// <summary>The ID number of this order</summary>
        [DataMember] internal int seq { get; set; }
        [DataMember] internal Take taker_gets { get; set; }
        [DataMember] internal Take taker_pays { get; set; }

        internal TradeType Type
        {
            get { return taker_gets.currency == "XRP" ? TradeType.SELL : TradeType.BUY; }
        }

        private double _amountXrp;//TODO = -1.0;
        internal double AmountXrp
        {
            get
            {
                if (_amountXrp.eq(0.0))
                {
                    var value = TradeType.BUY == Type
                        ? taker_pays.value
                        : taker_gets.value;
                    var valNumber = double.Parse(value);
                    _amountXrp = valNumber / 1000000.0;
                }

                return _amountXrp;
            }
        }

        private double _amountUsd;//TODO = -1.0;
        internal double AmountUsd
        {
            get
            {
                if (_amountUsd.eq(0.0))
                {
                    var value = TradeType.BUY == Type
                        ? taker_gets.value
                        : taker_pays.value;
                    _amountUsd = double.Parse(value);
                }

                return _amountUsd;
            }
        }

        /// <summary>Price of one XRP in USD</summary>
        internal double Price
        {
            get { return AmountUsd / AmountXrp; }
        }

        /// <summary>Currency code for the fiat side of an offer</summary>
        internal string Currency
        {
            get
            {
                return taker_gets.currency == "XRP"
                    ? taker_pays.currency
                    : taker_gets.currency;
            }
        }

        /// <summary>True if this order was fully filled or cancelled</summary>
        internal bool Closed { get; private set; }

        internal Offer()
        {
            //Serialization purposes
        }

        internal Offer(bool closed)
        {
            Closed = closed;
        }
    }

    [DataContract]
    internal class Take
    {
        [DataMember] internal string currency { get; set; }
        [DataMember] internal string issuer { get; set; }
        [DataMember] internal string value { get; set; }

        internal Take()
        {
            //For purposes of serialization to JSON
            currency = "";
            issuer = "";
            value = "";
        }
    }
}
