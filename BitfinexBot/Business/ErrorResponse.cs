using System.Runtime.Serialization;


namespace BitfinexBot.Business
{
    [DataContract]
    internal class ErrorResponse
    {
        [DataMember] internal string message { get; set; }
    }
}
