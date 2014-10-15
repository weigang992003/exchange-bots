
namespace RippleBot
{
    internal static class Const
    {
        /// <summary>Number of drops in one XRP</summary>
        internal const double DROPS_IN_XRP = 1000000.0;

        /// <summary>
        /// Maximum amount of XRP in drops that this bot is willing to pay to the server for any operation
        /// </summary>
        internal const int MAX_FEE = 100000;  //0.1 XRP
    }
}
