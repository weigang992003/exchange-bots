using System;
using System.Collections.Generic;
using System.Runtime.Serialization;


namespace KrakenBot.Business
{
    [DataContract]
    internal class TimeResponse
    {
        [DataMember] internal List<object> error { get; set; }
        [DataMember] internal TimeResult result { get; set; }
    }

    [DataContract]
    internal class TimeResult
    {
        [DataMember] internal int unixtime { get; set; }
        [DataMember] internal string rfc1123 { get; set; }


        internal DateTime TimeTyped
        {
            get
            {
                //TODO: the shift from CEST is -2hrs. Check if we need to consider it
                return new DateTime(1970, 1, 1).AddSeconds(unixtime);
            }
        }
    }
}
