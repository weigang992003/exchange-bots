using System.Runtime.Serialization;


namespace BtceBot.Business
{
    [DataContract]
    internal class Funds
    {
        [DataMember] internal double usd { get; set; }
        [DataMember] internal double btc { get; set; }
        [DataMember] internal double ltc { get; set; }
        [DataMember] internal double nmc { get; set; }
        [DataMember] internal double rur { get; set; }
        [DataMember] internal double eur { get; set; }
        [DataMember] internal double nvc { get; set; }
        [DataMember] internal double trc { get; set; }
        [DataMember] internal double ppc { get; set; }
        [DataMember] internal double ftc { get; set; }
        [DataMember] internal double xpm { get; set; }
        [DataMember] internal double cnh { get; set; }
        [DataMember] internal double gbp { get; set; }
    }

    [DataContract]
    internal class AccountInfo
    {
        [DataMember] internal Funds funds { get; set; }
        [DataMember] internal object rights { get; set; }
        [DataMember] internal int transaction_count { get; set; }
        [DataMember] internal int open_orders { get; set; }
        [DataMember] internal int server_time { get; set; }
    }

    [DataContract]
    internal class AccountInfoResponse
    {
        [DataMember] internal int success { get; set; }
        [DataMember] internal AccountInfo @return { get; set; }
    }
}
