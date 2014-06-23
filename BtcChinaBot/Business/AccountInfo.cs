using System.Runtime.Serialization;


namespace BtcChinaBot.Business
{
    [DataContract]
    internal class AccountInfoResponse
    {
        [DataMember] internal AccountInfoResult result;
        [DataMember] internal string id;
    }

    [DataContract]
    internal class AccountInfoResult
    {
        [DataMember] internal Profile profile;
        [DataMember] internal Assets balance;
        [DataMember] internal Assets frozen;
    }

    [DataContract]
    internal class Profile
    {
        [DataMember] internal string username;
        [DataMember] internal bool trade_password_enabled;
        [DataMember] internal bool otp_enabled;
        [DataMember] internal int trade_fee;
        [DataMember] internal int trade_fee_cnyltc;
        [DataMember] internal int trade_fee_btcltc;
        [DataMember] internal int daily_btc_limit;
        [DataMember] internal int daily_ltc_limit;
        [DataMember] internal string btc_deposit_address;
        [DataMember] internal string btc_withdrawal_address;
        [DataMember] internal string ltc_deposit_address;
        [DataMember] internal string ltc_withdrawal_address;
        [DataMember] internal int api_key_permission;
    }

    [DataContract]
    internal class Assets
    {
        [DataMember] internal Amount btc;
        [DataMember] internal Amount ltc;
        [DataMember] internal Amount cny;
    }

    [DataContract]
    internal class Amount
    {
        [DataMember] internal string currency;
        [DataMember] internal string symbol;
        [DataMember] internal string amount;
        [DataMember] internal string amount_integer;
        [DataMember] internal int amount_decimal;
    }
}
