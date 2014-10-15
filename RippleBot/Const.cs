using System.Collections.Generic;


namespace RippleBot
{
    internal static class Const
    {
        /// <summary>Number of drops in one XRP</summary>
        internal const double DROPS_IN_XRP = 1000000.0;

        /// <summary>
        /// Maximum amount of XRP in drops that this bot is willing to pay to the server for any operation
        /// </summary>
        internal const int MAX_FEE = 10000;  //0.01 XRP

        /// <summary>
        /// Result codes denoting successfull response or non-critical error (usually it's enough just to request again)
        /// </summary>
        internal static readonly HashSet<string> OkResultCodes = new HashSet<string>
        {
            "tesSUCCESS",
            "telINSUF_FEE_P",       //Message "Fee insufficient", need to let the server cool down and try later
            "tefPAST_SEQ",          //Message "This sequence number has already past", no idea what it means
            "terPRE_SEQ",           //Message "Missing/inapplicable prior transaction"
            "temBAD_SEQUENCE",      //Message "Malformed: Sequence is not in the past."
        };
    }
}
