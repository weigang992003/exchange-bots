using System.Runtime.Serialization;
using Common;
using System.Collections.Generic;


namespace RippleBot.Business
{
    [DataContract]
    internal class OffersResponse
    {
        [DataMember] internal int id { get; set; }
        [DataMember] internal Result result { get; set; }
        [DataMember] internal string status { get; set; }
        [DataMember] internal string type { get; set; }
    }

    [DataContract]
    internal class Result
    {
        [DataMember] internal string account { get; set; }
        [DataMember] internal List<Offer> offers { get; set; }
    }

    [DataContract]
    internal class Offer
    {
        [DataMember] internal int flags { get; set; }
        [DataMember] internal int seq { get; set; }
        [DataMember] internal object taker_gets { get; set; }
        [DataMember] internal object taker_pays { get; set; }

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
                    string value = OrderType.Buy == Type
                        ? (string)taker_pays
                        : (string)taker_gets;
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
                    string value = OrderType.Buy == Type
                        ? (string)taker_pays
                        : (string)taker_gets;
                    var valTaker = Helpers.DeserializeJSON<NonXrpTake>(value);
                    var valNumber = ulong.Parse(valTaker.value);
                    _amountUsd = (double)valNumber / 1000000.0;
                }

                return _amountUsd;
            }
        }
    }

    [DataContract]
    internal class NonXrpTake
    {
        [DataMember] internal string currency { get; set; }
        [DataMember] internal string issuer { get; set; }
        [DataMember] internal string value { get; set; }
    }


    internal enum OrderType
    {
        Buy = 1,
        Sell = 2
    }
}
