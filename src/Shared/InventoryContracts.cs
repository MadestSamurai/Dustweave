#nullable disable
using System.Runtime.Serialization;
namespace BD2Daily
{
    [DataContract] public sealed class ToolInventoryRequest
    {
        [DataMember] public string Id="";
        [DataMember] public int ProcessId;
        [DataMember] public long ProcessStartTicks;
        [DataMember] public string InstanceId="",AccountKey="",PlayerKey="";
        [DataMember] public long ExpiresUtcTicks;
    }
    [DataContract] public sealed class ToolInventoryResponse
    {
        [DataMember] public string Id="",AccountKey="",PlayerKey="",InstanceId="",PlayerName="";
        [DataMember] public int ProcessId;
        [DataMember] public long ProcessStartTicks,AtUtcTicks;
        [DataMember] public string Json="",Error="";
    }
}
