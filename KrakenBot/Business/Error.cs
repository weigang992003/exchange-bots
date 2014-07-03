using System.Collections.Generic;
using System.Runtime.Serialization;


namespace KrakenBot.Business
{
    [DataContract]
    internal class ErrorResponse
    {
        [DataMember] internal List<string> error { get; set; }
    }
}
