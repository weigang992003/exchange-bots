using System.Collections.Generic;
using System.Runtime.Serialization;


namespace HuobiBot.Business
{
    [DataContract]
    internal class ErrorResponse
    {
        private static readonly Dictionary<int, string> _errorCodes = new Dictionary<int, string>
        {
            {1, "Server error"},
            {2, "Insufficient CNY"},
            {3, "Restarting failed"},
            {4, "Transaction is over"},
            {10, "Insufficient BTC"},
            {26, "Order is not existed"},
            {41, "Unable to modify due to filled order"},
            {42, "Order has been cancelled, unable to modify"},
            {44, "Price is too low"},
            {45, "Price is too high"},
            {46, "Minimum amount is 0.001"},
            {47, "Exceed the limit amount"},
            {55, "105% higher than current price, not allowed"},
            {56, "95% lower than current price, not allowed"},
            {64, "Invalid request"},
            {65, "Invalid method"},
            {66, "Invalid access key"},
            {67, "Invalid private key"},
            {68, "Invalid price"},
            {69, "Invalid amount"},
            {70, "Invalid submitting time"},
            {71, "Too many requests"},
            {87, "Buying price cannot exceed 101% of last price when transaction amount is less than 0.1 BTC"},
            {88, "Selling price cannot below 99% of last price when transaction amount is less than 0.1 BTC"}
        };

        [DataMember] internal int code { get; set; }
        [DataMember] internal string msg { get; set; }
        [DataMember] internal int time { get; set; }

        internal string Description
        {
            get { return _errorCodes.ContainsKey(code) ? _errorCodes[code] : null; }
        }
    }
}
