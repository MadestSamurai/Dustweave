#nullable disable
using System;
using System.Runtime.Serialization;
namespace BD2Daily {
 [DataContract] public sealed class GuildObservation {
  [DataMember] public bool Supported;
  [DataMember] public string Error="";
  [DataMember] public string ServerKey="";
  [DataMember] public string GuildKey="";
  [DataMember] public string GuildName="";
  [DataMember] public string CycleKey="";
  [DataMember] public long ServerTicks;
  [DataMember] public long ResetTicks;
  [DataMember] public bool InGuild;
  [DataMember] public bool MenuVisible;
  [DataMember] public bool GuildVisible;
  [DataMember] public bool AttendancePopup;
  [DataMember] public string BlockReason="";
  [DataMember] public string[] ActiveUis=new string[0];
  [DataMember] public string ClientMvid="";
 }
 [DataContract] public sealed class GuildCommand {
  [DataMember] public string Id="";
  [DataMember] public string Owner="";
  [DataMember] public string AccountKey="";
  [DataMember] public string PlayerKey="";
  [DataMember] public string GuildKey="";
  [DataMember] public string ServerKey="";
  [DataMember] public string CycleKey="";
  [DataMember] public int ProcessId;
  [DataMember] public long ProcessStartTicks;
  [DataMember] public string InstanceId="";
  [DataMember] public long ExpiresUtcTicks;
 }
 [DataContract] public sealed class GuildReceipt {
  [DataMember] public string Id="";
  [DataMember] public string AccountKey="";
  [DataMember] public string PlayerKey="";
  [DataMember] public string GuildKey="";
  [DataMember] public string ServerKey="";
  [DataMember] public string CycleKey="";
  [DataMember] public string ClientMvid="";
  [DataMember] public string State="prepared";
  [DataMember] public string Message="";
  [DataMember] public long AtUtcTicks;
  [DataMember] public long StartedUtcTicks;
  [DataMember] public bool MayHaveDispatched;
  [DataMember] public int RequestCount;
  [DataMember] public bool ResponseSeen;
  [DataMember] public bool CallbackMatched;
  [DataMember] public int ErrorCode;
  [DataMember] public bool AttendanceGranted;
  [DataMember] public bool MemberMatched;
  [DataMember] public bool CacheMatched;
  [DataMember] public bool ReturnedHome;
  [DataMember] public bool Passive;
 }
 [DataContract] public sealed class GuildTrace {
  [DataMember] public string Origin="runtime";
  [DataMember] public string Event="";
  [DataMember] public DailySnapshot Snapshot;
  [DataMember] public GuildReceipt Receipt;
  [DataMember] public string Decision="";
  [DataMember] public long NowUtcTicks;
  [DataMember] public long ResponseAtTicks;
  [DataMember] public bool HasControl;
  [DataMember] public bool PopupClicked;
  [DataMember] public bool BackClicked;
  [DataMember] public bool ObservedGuild;
 }
 // Shared by production and offline replay. No Unity references, no I/O or clock reads.
 public static class GuildPolicy {
  public static bool Terminal(string state){return state=="completed"||state=="checked_no_grant"||state=="skipped"||state=="rejected"||state=="unknown"||state=="confirmed_cleanup_pending";}
  public static string Gate(DailySnapshot s,GuildReceipt prior,long now){
   if(s==null||string.IsNullOrEmpty(s.AccountKey)||string.IsNullOrEmpty(s.PlayerKey)||s.Runtime!=DailyIdentity.RuntimeName||s.State!="identified"||s.FrameUtcTicks<now-TimeSpan.FromSeconds(5).Ticks||s.FrameUtcTicks>now+TimeSpan.FromSeconds(2).Ticks)return "等待新鲜的游戏身份快照";
   var g=s.Guild;if(g==null||!g.Supported)return "公会接口不可用";
   if(!g.InGuild)return "当前角色未加入公会";
   if(string.IsNullOrEmpty(g.ServerKey)||string.IsNullOrEmpty(g.CycleKey)||string.IsNullOrEmpty(g.GuildKey)||g.ResetTicks<=g.ServerTicks)return "等待游戏重置时间与公会身份";
   if(g.ResetTicks-g.ServerTicks<TimeSpan.FromSeconds(60).Ticks)return "即将跨日，等待游戏刷新后再执行";
   if(prior!=null){if(prior.State=="completed"||prior.State=="confirmed_cleanup_pending")return "本周期签到已确认，跳过";if(prior.State=="checked_no_grant")return "本周期已检查，服务器未新增签到；不重复请求";if(prior.MayHaveDispatched||prior.State=="prepared")return "存在未确认操作，先核对记录；不重复请求";}
   if(!string.IsNullOrEmpty(g.BlockReason))return g.BlockReason;
   if(!g.MenuVisible||g.GuildVisible)return "请打开游戏主菜单，显示公会入口";
   return "";
  }
  public static bool Matches(GuildCommand c,DailySnapshot s,DailyLease lease,long now){return c!=null&&s!=null&&s.Guild!=null&&lease!=null&&lease.Matches(s,now)&&lease.Owner==c.Owner&&c.AccountKey==s.AccountKey&&c.PlayerKey==s.PlayerKey&&c.ProcessId==s.ProcessId&&c.ProcessStartTicks==s.ProcessStartTicks&&c.InstanceId==s.InstanceId&&c.GuildKey==s.Guild.GuildKey&&c.ServerKey==s.Guild.ServerKey&&c.CycleKey==s.Guild.CycleKey&&c.ExpiresUtcTicks>now&&c.ExpiresUtcTicks<=now+TimeSpan.FromSeconds(20).Ticks;}
  public static bool SameIdentity(DailySnapshot s,GuildReceipt r){return r!=null&&s!=null&&s.Guild!=null&&s.State=="identified"&&r.AccountKey==s.AccountKey&&r.PlayerKey==s.PlayerKey&&r.ServerKey==s.Guild.ServerKey&&r.GuildKey==s.Guild.GuildKey&&r.CycleKey==s.Guild.CycleKey;}
  public static string Next(GuildTrace t){
   var s=t.Snapshot;var r=t.Receipt;
   if(!SameIdentity(s,r))return "identity_changed";
   if(!r.ResponseSeen)return t.NowUtcTicks-r.StartedUtcTicks>TimeSpan.FromSeconds(25).Ticks?"timeout":"wait_response";
   if(Outcome(r)=="unknown")return "unknown";
   if(!t.HasControl)return "release";
   var g=s.Guild;
   if(g.MenuVisible&&!g.GuildVisible&&!g.AttendancePopup&&t.BackClicked&&t.ObservedGuild)return "finish";
   if(t.NowUtcTicks-t.ResponseAtTicks>TimeSpan.FromSeconds(20).Ticks)return "cleanup_timeout";
   if(!string.IsNullOrEmpty(g.BlockReason))return "wait_ui";
   if(g.AttendancePopup&&!t.PopupClicked)return "close_attendance";
   if(g.GuildVisible&&!g.AttendancePopup&&!t.BackClicked&&t.NowUtcTicks-t.ResponseAtTicks>TimeSpan.FromSeconds(2).Ticks)return "return_menu";
   return "wait_ui";
  }
  public static string Outcome(GuildReceipt r){if(r.RequestCount!=1||!r.ResponseSeen||!r.CallbackMatched||r.ErrorCode!=0||!r.MemberMatched||!r.CacheMatched)return "unknown";return r.AttendanceGranted?"completed":"checked_no_grant";}
 }
}
