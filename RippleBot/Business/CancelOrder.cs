using System.Collections.Generic;
using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class CancelResult
    {
        private static readonly HashSet<string> _okResultTokens = new HashSet<string>
        {
            "tesSUCCESS",
            "telINSUF_FEE_P",       //Message "Fee insufficient", no matter how it's possible while cancelling
            "tefPAST_SEQ",          //Message "This sequence number has already past", no idea what it means
        };

        [DataMember] internal string engine_result { get; set; }
        [DataMember] internal int engine_result_code { get; set; }
        [DataMember] internal string engine_result_message { get; set; }
        [DataMember] internal string tx_blob { get; set; }
        [DataMember] internal object tx_json { get; set; }

        /// <summary>Response indicated success</summary>
        internal bool ResultOK
        {
            get { return _okResultTokens.Contains(engine_result); }
        }
    }

    [DataContract]
    internal class CancelOrderResponse
    {
        [DataMember] internal CancelResult result { get; set; }
        [DataMember] internal string status { get; set; }
        [DataMember] internal string type { get; set; }
    }
}
