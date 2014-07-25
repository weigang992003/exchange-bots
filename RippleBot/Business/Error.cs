﻿using System.Runtime.Serialization;


namespace RippleBot.Business
{
    [DataContract]
    internal class ErrorResponse
    {
        [DataMember] internal string error { get; set; }
        [DataMember] internal int? error_code { get; set; }
        [DataMember] internal string error_message { get; set; }
        [DataMember] internal int? id { get; set; }
        [DataMember] internal object request { get; set; }
        [DataMember] internal string status { get; set; }
        [DataMember] internal string type { get; set; }
    }
}