using System.Runtime.Serialization;


namespace HuobiBot.Business
{
    [DataContract]
    internal class AccountInfoResponse
    {
        [DataMember] internal string total { get; set; }
        [DataMember] internal string net_asset { get; set; }
        [DataMember] internal string available_cny_display { get; set; }
        [DataMember] internal string available_btc_display { get; set; }
        [DataMember] internal string frozen_cny_display { get; set; }
        [DataMember] internal string frozen_btc_display { get; set; }
        [DataMember] internal string loan_cny_display { get; set; }
        [DataMember] internal string loan_btc_display { get; set; }

        internal double AvailableBtc
        {
            get { return double.Parse(available_btc_display); }
        }

        /// <summary>Available balance in Chinese Yuan</summary>
        internal double AvailableCny
        {
            get { return double.Parse(available_cny_display); }
        }
    }
}
