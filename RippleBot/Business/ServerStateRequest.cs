using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class ServerStateRequest
    {
        [DataMember] internal readonly string command = "server_state";
    }
}
