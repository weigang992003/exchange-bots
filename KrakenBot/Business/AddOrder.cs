using System.Collections.Generic;
using System.Runtime.Serialization;


namespace KrakenBot.Business
{
    [DataContract]
    internal class AddOrderResponse
    {
        [DataMember] internal List<object> error { get; set; }
        [DataMember] internal Result result { get; set; }
    }

    [DataContract]
    internal class Result
    {
        [DataMember] internal AddOrderData descr { get; set; }
        /// <summary>There should be always right one, the new order ID</summary>
        [DataMember] internal List<string> txid { get; set; }
    }

    [DataContract]
    internal class AddOrderData
    {
        [DataMember] internal string order { get; set; }
    }
}
