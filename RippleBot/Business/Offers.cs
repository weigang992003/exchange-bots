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
        [DataMember] internal int seq { get; set; }
        [DataMember] internal Take taker_gets { get; set; }
        [DataMember]
        internal Take taker_pays { get; set; }

        internal OrderType Type
        {
            get { return flags == 0 ? OrderType.Buy : OrderType.Sell; }
        }

        private double _amountXrp;//TODO = -1.0;
        internal double AmountXrp
        {
            get
            {
                if (_amountXrp.eq(0.0))
                {
                    var value = OrderType.Buy == Type
                        ? taker_pays.value
                        : taker_gets.value;
                    var valNumber = ulong.Parse(value);
                    _amountXrp = (double)valNumber / 1000000.0;
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
                    var value = OrderType.Buy == Type
                        ? taker_gets.value
                        : taker_pays.value;
                    _amountUsd = double.Parse(value);
                }

                return _amountUsd;
            }
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


    internal enum OrderType
    {
        Buy = 1,
        Sell = 2
    }
}
