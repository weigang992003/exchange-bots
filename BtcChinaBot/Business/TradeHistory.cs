using System;
using System.Runtime.Serialization;


namespace BtcChinaBot.Business
{
    [DataContract]
    internal class TradeResponse
    {
        private string _date;

        /// <summary>Seconds since 01/01/1970, chinese time</summary>
        [DataMember]
        internal string date
        {
            get { return _date; }
            set
            {
                _date = value;
                var seconds = Double.Parse(value);
                DateTyped = new DateTime(1970, 1, 1).AddSeconds(seconds).AddHours(2);
            }
        }

        /// <summary>
        /// this.<see cref="date"/> converted to <see cref="DateTime"/>
        /// </summary>
        internal DateTime DateTyped { get; private set; }

        [DataMember] internal double price { get; set; }
        [DataMember] internal double amount { get; set; }
        [DataMember] internal string tid { get; set; }
        [DataMember] internal string type { get; set; }
    }
}
