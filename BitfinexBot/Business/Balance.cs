using System.Runtime.Serialization;


namespace BitfinexBot.Business
{
    [DataContract]
    internal class Balance
    {
        [DataMember] internal string type { get; set; }
        [DataMember] internal string currency { get; set; }
        [DataMember] internal string amount { get; set; }
        [DataMember] internal string available { get; set; }

        internal double AvailableLtc
        {
            get { return double.Parse(available); }
        }
    }
}
