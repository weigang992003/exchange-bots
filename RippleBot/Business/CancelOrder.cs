using System.Collections.Generic;
using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class CancelResult
    {
        [DataMember] internal string engine_result { get; set; }
        [DataMember] internal int engine_result_code { get; set; }
        [DataMember] internal string engine_result_message { get; set; }
        [DataMember] internal string tx_blob { get; set; }
        [DataMember] internal object tx_json { get; set; }

        /// <summary>Response indicated success</summary>
        internal bool ResultOK
        {
            get { return Const.OkResultCodes.Contains(engine_result); }
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
