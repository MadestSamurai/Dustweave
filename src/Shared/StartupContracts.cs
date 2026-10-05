#nullable disable
using System;
using System.Runtime.Serialization;
namespace BD2Daily {
 [DataContract] public sealed class StartupObservation {
  [DataMember] public bool Supported;
  [DataMember] public bool Visible;
  [DataMember] public bool Ready;
  [DataMember] public bool ClickEnabled;
  [DataMember] public int TitleInstanceId;
  [DataMember] public string Stage="";
  [DataMember] public string BlockReason="";
  [DataMember] public string Action="";
  [DataMember] public bool Attempted;
  [DataMember] public bool DownloadVisible,DownloadReady,DownloadAttempted,DownloadInProgress;
  [DataMember] public int DownloadInstanceId;
 }
 // Granted only while the explicit connect/login-check operation is alive.
 [DataContract] public sealed class StartupPermit {
  [DataMember] public string Owner="";
  [DataMember] public string AccountKey="";
  [DataMember] public string InstanceId="";
  [DataMember] public int ProcessId;
  [DataMember] public long ProcessStartTicks;
  [DataMember] public long ExpiresUtcTicks;
 }
 [DataContract] public sealed class StartupRecord {
  [DataMember] public string Owner="";[DataMember] public long AtUtcTicks;[DataMember] public DailySnapshot Before;[DataMember] public string Decision="";
 }
 public static class StartupPolicy {
  public static bool Fresh(DailySnapshot s,long now){return s!=null&&s.Runtime==DailyIdentity.RuntimeName&&s.Sequence>0&&!string.IsNullOrEmpty(s.InstanceId)&&s.FrameUtcTicks>=now-TimeSpan.FromSeconds(5).Ticks&&s.FrameUtcTicks<=now+TimeSpan.FromSeconds(2).Ticks;}
  public static string Decide(DailySnapshot s,StartupPermit p,long now){
   if(!Fresh(s,now))return "wait_frame";
   if(!string.IsNullOrEmpty(s.ErrorCode))return "read_error";
   var t=s.Startup;if(t==null||!t.Supported)return "unsupported";
   if(!t.Visible)return "no_title";
   if(p==null||string.IsNullOrEmpty(p.Owner)||p.ProcessId!=s.ProcessId||p.ProcessStartTicks!=s.ProcessStartTicks||p.InstanceId!=s.InstanceId||p.ExpiresUtcTicks<=now||p.ExpiresUtcTicks>now+TimeSpan.FromSeconds(15).Ticks)return "not_authorized";
   if(t.DownloadVisible){
    if(!string.IsNullOrEmpty(s.AccountKey)&&p.AccountKey!=s.AccountKey)return "wrong_account";
    if(string.IsNullOrEmpty(p.AccountKey))return "not_authorized";
    if(t.DownloadAttempted)return "download_already_clicked";
    if(!string.IsNullOrEmpty(t.BlockReason))return "blocked";
    if(!t.DownloadReady||t.DownloadInstanceId==0)return "wait_download";
    return "click_download";
   }
   if(string.IsNullOrEmpty(s.AccountKey))return "wait_sdk";
   if(p.AccountKey!=s.AccountKey)return "wrong_account";
   if(t.Attempted)return "already_clicked";
   if(!string.IsNullOrEmpty(t.BlockReason))return "blocked";
   if(!t.Ready||!t.ClickEnabled||t.TitleInstanceId==0)return "wait_title";
   return "click_start";
  }
 }
}
