using System.Collections.Generic;
using System.Runtime.Serialization;


namespace KrakenBot.Business
{
    [DataContract]
    internal class CancelResult
    {
        [DataMember] internal int count { get; set; }
    }

    [DataContract]
    internal class CancelOrderResponse
    {
        [DataMember] internal List<object> error { get; set; }
        [DataMember] internal CancelResult result { get; set; }
    }
}
