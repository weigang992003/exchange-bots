using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class ErrorResponse
    {
        [DataMember] internal string error { get; set; }
        [DataMember] internal int? error_code { get; set; }
        [DataMember] internal string error_message { get; set; }
        [DataMember] internal int? id { get; set; }
        [DataMember] internal object request { get; set; }
        [DataMember] internal string status { get; set; }
        [DataMember] internal string type { get; set; }


        internal bool IsCritical
        {
            get
            {
                if (8 == error_code)        //"tooBusy"
                    return false;
                if ("noNetwork" == error)   //"Ripple not synced to Ripple Network"
                    return false;
                if ("highFee" == error)     //"Fee of 123 exceeds the requested tx limit 100". Unexplained error, need to do some research. So far try to resolve by ignoring.
                    return false;
                if ("noCurrent" == error)   //"Current ledger is unavailable."
                    return false;

                return true;
            }
        }
    }
}
