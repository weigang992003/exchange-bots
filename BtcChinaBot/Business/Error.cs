using System.Runtime.Serialization;


namespace BtcChinaBot.Business
{
    [DataContract]
    internal class ErrorResponse
    {
        [DataMember] internal Error error { get; set; }
        [DataMember] internal string id { get; set; }
    }

    [DataContract]
    internal class Error
    {
        [DataMember] internal int code { get; set; }
        [DataMember] internal string message { get; set; }
        [DataMember] internal Data data { get; set; }
    }

    [DataContract]
    internal class Data
    {
        //TODO: when some returned, update this
    }
}
