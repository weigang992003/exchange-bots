using System.Runtime.Serialization;


namespace BtceBot.Business
{
    [DataContract]
    internal class ErrorResponse
    {
        [DataMember] internal int success { get; set; }
        [DataMember] internal string error { get; set; }

        /// <summary>If true, critical error occured and trading should be interrupted asap</summary>
        internal bool IsCritical
        {
            get { return true; }   //TODO
        }
    }
}
