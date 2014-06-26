using System.Runtime.Serialization;


namespace HuobiBot.Business
{
    [DataContract]
    internal class BasicResponse
    {
        [DataMember] internal string result { get; set; }
        [DataMember] internal int id { get; set; }
    }
}
