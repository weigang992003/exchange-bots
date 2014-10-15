using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class ServerStateResponse
    {
        [DataMember] internal Result result { get; set; }
        [DataMember] internal string status { get; set; }
        [DataMember] internal string type { get; set; }

        internal double LastFee
        {
            get
            {
                var s = result.state;
                var fee = (double)s.validated_ledger.base_fee * s.load_factor / s.load_base;

                return fee / Const.DROPS_IN_XRP;
            }
        }
    }

    [DataContract]
    internal class Result
    {
        [DataMember] internal State state { get; set; }
    }

    [DataContract]
    internal class State
    {
        [DataMember] internal string build_version { get; set; }
        [DataMember] internal string complete_ledgers { get; set; }
        [DataMember] internal int io_latency_ms { get; set; }
        [DataMember] internal LastClose last_close { get; set; }
        [DataMember] internal int load_base { get; set; }
        [DataMember] internal int load_factor { get; set; }
        [DataMember] internal int peers { get; set; }
        [DataMember] internal string pubkey_node { get; set; }
        [DataMember] internal string server_state { get; set; }
        [DataMember] internal ValidatedLedger validated_ledger { get; set; }
        [DataMember] internal int validation_quorum { get; set; }
    }

    [DataContract]
    internal class LastClose
    {
        [DataMember] internal int converge_time { get; set; }
        [DataMember] internal int proposers { get; set; }
    }

    [DataContract]
    internal class ValidatedLedger
    {
        [DataMember] internal int base_fee { get; set; }
        [DataMember] internal int close_time { get; set; }
        [DataMember] internal string hash { get; set; }
        [DataMember] internal int reserve_base { get; set; }
        [DataMember] internal int reserve_inc { get; set; }
        [DataMember] internal int seq { get; set; }
    }
}
