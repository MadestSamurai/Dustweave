#nullable disable
using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace BD2Daily
{
    public static class DailyIdentity
    {
        public const string LiveEntries="snapshot.json|runtime.json|lease.json|startup-permit.json|guild-command.json";
        public const string RuntimeName="BD2Daily.Runtime4";
        public const string Version="0.7.27";
        public static string DataRoot { get { return Environment.GetEnvironmentVariable("BD2_DAILY_DATA_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2DailyAssistant"); } }
        public static string Hash(string value)
        {using(var sha=SHA256.Create()){var bytes=sha.ComputeHash(Encoding.UTF8.GetBytes(value));return BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();}}
        public static string MemberKey(string member)
        {long id;return long.TryParse(member,NumberStyles.None,CultureInfo.InvariantCulture,out id)&&id>0?Hash("bd2-member-v1|"+id.ToString(CultureInfo.InvariantCulture)):"";}
        public static string PlayerKey(string memberKey,long owner)
        {return string.IsNullOrEmpty(memberKey)||owner<=0?"":Hash("bd2-player-v1|"+memberKey+"|"+owner.ToString(CultureInfo.InvariantCulture));}
    }
    [DataContract] public sealed class DailySnapshot
    {
        [DataMember] public string Runtime=DailyIdentity.RuntimeName;
        [DataMember] public int ProcessId;
        [DataMember] public long ProcessStartTicks;
        [DataMember] public string InstanceId="";
        [DataMember] public long Sequence;
        [DataMember] public long FrameUtcTicks;
        [DataMember] public string AccountKey="";
        [DataMember] public string PlayerKey="";
        [DataMember] public string PlayerName="";
        [DataMember] public string Scene="";
        [DataMember] public string State="waiting_frame";
        [DataMember] public string ErrorCode="";
        [DataMember] public string LeaseOwner="";
        [DataMember] public GuildObservation Guild;
        [DataMember] public StartupObservation Startup;
        [DataMember] public string[] Capabilities=new[]{"account.identity"};
    }
    [DataContract] public sealed class DailyRuntimeStatus
    {
        [DataMember] public string Runtime=DailyIdentity.RuntimeName;
        [DataMember] public int ProcessId;
        [DataMember] public long ProcessStartTicks;
        [DataMember] public long AtUtcTicks;
        [DataMember] public string State="starting";
        [DataMember] public string Error="";
    }
    [DataContract] public sealed class DailyLease
    {
        [DataMember] public string Owner="";
        [DataMember] public int ProcessId;
        [DataMember] public long ProcessStartTicks;
        [DataMember] public string AccountKey="";
        [DataMember] public long ExpiresUtcTicks;
        public bool Matches(DailySnapshot s,long now)
        {return !string.IsNullOrEmpty(Owner)&&ProcessId==s.ProcessId&&ProcessStartTicks==s.ProcessStartTicks&&AccountKey==s.AccountKey&&!string.IsNullOrEmpty(AccountKey)&&ExpiresUtcTicks>now&&ExpiresUtcTicks<=now+TimeSpan.FromSeconds(15).Ticks;}
    }
}
