using System.Collections.Generic;
using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class Payment
    {
        [DataMember] internal string source_account { get; set; }
        [DataMember] internal string source_tag { get; set; }
        [DataMember] internal PaymentAmount source_amount { get; set; }
        [DataMember] internal string source_slippage { get; set; }
        [DataMember] internal string destination_account { get; set; }
        [DataMember] internal string destination_tag { get; set; }
        [DataMember] internal PaymentAmount destination_amount { get; set; }
        [DataMember] internal string invoice_id { get; set; }
        [DataMember] internal string paths { get; set; }
        [DataMember] internal bool no_direct_ripple { get; set; }
        [DataMember] internal bool partial_payment { get; set; }
        [DataMember] internal string direction { get; set; }
        [DataMember] internal string state { get; set; }
        [DataMember] internal string result { get; set; }
        [DataMember] internal string ledger { get; set; }
        [DataMember] internal string hash { get; set; }
        [DataMember] internal string timestamp { get; set; }
        [DataMember] internal string fee { get; set; }
        [DataMember] internal List<AccountBalanceChange> source_balance_changes { get; set; }
        [DataMember] internal List<AccountBalanceChange> destination_balance_changes { get; set; }
    }

    [DataContract]
    internal class PaymentAmount
    {
        [DataMember] internal string value { get; set; }
        [DataMember] internal string currency { get; set; }
        [DataMember] internal string issuer { get; set; }
    }

    [DataContract]
    internal class AccountBalanceChange
    {
        [DataMember] internal string value { get; set; }
        [DataMember] internal string currency { get; set; }
        [DataMember] internal string issuer { get; set; }
    }
}
