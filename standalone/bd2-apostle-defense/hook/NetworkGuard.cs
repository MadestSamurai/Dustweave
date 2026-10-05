using System;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Concurrent;
using HarmonyLib;
using B=BD2ApostleDefense.Runtime.Bindings;
using Network.TCP;
namespace BD2ApostleDefense.Runtime
{
 // Only changes reactions to the external connectivity probe for RandomDefense.
 // Real transport errors, close/rejection packets and game settlement run unchanged.
 internal static class NetworkGuard
 {
  private const string PatchId="bd2.apostle-defense.network.v1";
  private static readonly object sync=new object();
  private static FieldInfo defense,mode,monitorInstance,timeout;
  private static readonly ConcurrentQueue<NetworkEvidence> events=new ConcurrentQueue<NetworkEvidence>();
  private static NetworkRecoveryPolicy policy=new NetworkRecoveryPolicy();
  private static object current;private static object client,monitor;
  private static Harmony patch;private static bool bypass,active;private static int originalTimeout;
  private static string room="",state="inactive";private static long lastPacketUtc;
  internal static long Now {get{return (long)(Stopwatch.GetTimestamp()*(double)TimeSpan.TicksPerSecond/Stopwatch.Frequency);}}
  internal static void Install()
  {
   if(patch!=null)return;
   defense=(FieldInfo)B.Api("Net.Defense");mode=(FieldInfo)B.Api("Net.Mode");monitorInstance=(FieldInfo)B.Api("Net.Monitor");timeout=(FieldInfo)B.Api("Net.Timeout");
   if(defense==null||mode==null||monitorInstance==null||timeout==null)throw new MissingMemberException("Network resilience client contract changed");
   patch=new Harmony(PatchId);
   try{
    patch.Patch((MethodInfo)B.Api("Net.Lost"),prefix:new HarmonyMethod(typeof(NetworkGuard),nameof(Lost)));
    patch.Patch((MethodInfo)B.Api("Net.Restored"),prefix:new HarmonyMethod(typeof(NetworkGuard),nameof(Restored)));
    patch.Patch((MethodInfo)B.Api("Net.Receive"),prefix:new HarmonyMethod(typeof(NetworkGuard),nameof(Received)));
   }catch{Remove();throw;}
  }
  internal static void Remove()
  {
   if(patch!=null){foreach(string name in new[]{"OnNetworkLost","OnNetworkRestored"})patch.Unpatch((MethodInfo)B.Api(name=="OnNetworkLost"?"Net.Lost":"Net.Restored"),HarmonyPatchType.Prefix,PatchId);patch.Unpatch((MethodInfo)B.Api("Net.Receive"),HarmonyPatchType.Prefix,PatchId);patch=null;}
   lock(sync){RestoreTimeout();current=null;client=null;active=false;policy=new NetworkRecoveryPolicy();state="inactive";room="";}
  }
  private static bool IsDefense(object value){return !ReferenceEquals(value,null)&&ReferenceEquals(defense.GetValue(null),value)&&Convert.ToString(mode.GetValue(value))=="RandomDefense";}
  private static bool Connected(object value){try{return (bool)B.InvokeOn("Net.Connected",value,B.EnumObject("NetworkKind","RandomDefense"));}catch{return false;}}
  private static void Bind(object value)
  {
   var next=B.Read("Net.Client",value);
   if(!ReferenceEquals(current,value)){current=value;client=next;policy=new NetworkRecoveryPolicy();lastPacketUtc=0;room="";}
   else if(!ReferenceEquals(client,next)){policy.NewConnection(true);client=next;lastPacketUtc=0;Record("connection-changed");}
   active=true;
  }
  private static void Record(string kind)
  {
   events.Enqueue(new NetworkEvidence{At=DateTime.UtcNow.Ticks,Kind=kind,Room=room,State=state,LastPacketAt=lastPacketUtc,ProbeFailures=policy.ProbeFailures,AvoidedDisconnects=policy.AvoidedDisconnects,NativeDisconnects=policy.NativeDisconnects});
   NetworkEvidence ignored;while(events.Count>256)events.TryDequeue(out ignored);
  }
  private static bool Lost(object __instance)
  {
   if(bypass||!IsDefense(__instance))return true;
   lock(sync){Bind(__instance);bool allow=policy.Lost(Now,Connected(__instance));Record(allow?"probe-lost-native":"probe-lost-deferred");return allow;}
  }
  private static bool Restored(object __instance)
  {
   if(bypass||!IsDefense(__instance))return true;
   lock(sync){Bind(__instance);bool allow=policy.Restored(Now,Connected(__instance));Record(allow?"probe-restored-native":"probe-restored-kept-session");return allow;}
  }
  // ReadPacket calls this only after a complete header/body has arrived. Includes native
  // CSPingResponse (consumed below PublishReceivedPacket) and excludes other/retired sockets.
  private static void Received(object __instance,object __0)
  {
   var tcp=B.Read("Net.Owner",__instance);
   if(!IsDefense(tcp)||!ReferenceEquals(B.Read("Net.Client",tcp),__instance))return;
   lock(sync){if(!ReferenceEquals(B.Read("Net.Client",tcp),__instance))return;Bind(tcp);if(!ReferenceEquals(client,__instance))return;
    if((ENetMsg)Convert.ToInt32(B.Read("Net.Message",__0))==ENetMsg.SCConnectCloseRequest){policy.ClosedByServer();Record("server-close-native");return;}
    policy.Packet(Now,(ENetMsg)Convert.ToInt32(B.Read("Net.Message",__0))==ENetMsg.CSDefenseReconnectResponse||(ENetMsg)Convert.ToInt32(B.Read("Net.Message",__0))==ENetMsg.SCDefenseGameStartRequest);lastPacketUtc=DateTime.UtcNow.Ticks;
   }
  }
  private static void RestoreTimeout()
  {
   if(monitor!=null&&Convert.ToInt32(timeout.GetValue(monitor))==3000)timeout.SetValue(monitor,originalTimeout);
   monitor=null;
  }
  private static void ConfigureProbe(object tcp)
  {
   var found=monitorInstance.GetValue(null);
   var callback=found==null?null:B.Read("Net.LostCallback",found) as Delegate;
   if(found==null||callback==null||!ReferenceEquals(callback.Target,tcp)){RestoreTimeout();return;}
   if(!ReferenceEquals(monitor,found)){RestoreTimeout();monitor=found;originalTimeout=Convert.ToInt32(timeout.GetValue(found));if(originalTimeout<3000)timeout.SetValue(found,3000);}
  }
  internal static void Pump(Snapshot s)
  {
   object invoke=null;
   lock(sync){var tcp=defense.GetValue(null) as object;
    if(!IsDefense(tcp)){RestoreTimeout();active=false;state="inactive";return;}
    Bind(tcp);if(!GameFlow.NeedsBattleConnection(s)){RestoreTimeout();return;}ConfigureProbe(tcp);
    if(policy.Tick(Now,Connected(tcp))){invoke=tcp;Record("probe-grace-expired-native");}
   }
   if(invoke!=null){try{bypass=true;B.InvokeOn("Net.Lost",invoke);}finally{bypass=false;}}
  }
  internal static void Apply(Snapshot s)
  {
   lock(sync){
    // The connection may remain allocated after returning to the lobby. Never block lobby flow.
    bool inRound=active&&GameFlow.NeedsBattleConnection(s);
    if(s.Room.Length>0&&room!=s.Room){if(room.Length>0)policy.NewConnection(false);room=s.Room;}
    bool hold=inRound&&policy.Hold(Connected(current));
    string next=!inRound?"inactive":policy.ServerClosed?"server-closed":hold&&policy.AwaitingRound?"waiting-room":hold?"recovering":policy.Deferred?"probe-degraded":"healthy";
    if(next!=state){state=next;Record("state");}
    s.NetworkHold=hold;s.NetworkState=state;s.NetworkLastPacketAt=lastPacketUtc;
    s.NetworkProbeFailures=policy.ProbeFailures;s.NetworkAvoidedDisconnects=policy.AvoidedDisconnects;s.NetworkNativeDisconnects=policy.NativeDisconnects;
    s.NetworkMessage=hold?"网络恢复中，保留任务并等待服务器与盘面同步":state=="probe-degraded"?"外部探测不稳定，游戏通信仍正常":"";
   }
  }
  internal static void Flush(){NetworkEvidence entry;while(events.TryPeek(out entry)){Storage.AppendNetwork(entry);events.TryDequeue(out entry);}}
 }
}
