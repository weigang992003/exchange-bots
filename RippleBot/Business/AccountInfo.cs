using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class AccountInfoRequest
    {
        [DataMember] internal readonly int id = 4;
        [DataMember] internal readonly string command = "account_info";
        [DataMember] internal string account { get; set; }
    }


    [DataContract]
    internal class AccountInfoResponse
    {
        [DataMember] internal int id;
        [DataMember] internal string status;
        [DataMember] internal string type;
        [DataMember] internal AccountResult result;
    }

    [DataContract]
    internal class AccountData
    {
        [DataMember] internal string Account;
        [DataMember] internal string Balance;
        [DataMember] internal int Flags;
        [DataMember] internal string LedgerEntryType;
        [DataMember] internal int OwnerCount;
        [DataMember] internal string PreviousTxnID;
        [DataMember] internal int PreviousTxnLgrSeq;
        [DataMember] internal int Sequence;
        [DataMember] internal string index;

        internal double BalanceXrp
        {
            get
            {
                ulong drops = ulong.Parse(Balance);
                return (double)drops / 1000000.0;
            }
        }
    }

    [DataContract]
    internal class AccountResult
    {
        [DataMember] internal AccountData account_data;
        [DataMember] internal int ledger_current_index;
        [DataMember] internal bool validated;
    }    
}
