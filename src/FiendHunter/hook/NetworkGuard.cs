using System;
using System.Diagnostics;
using System.Reflection;
using System.Collections.Concurrent;
using HarmonyLib;
using BDNetwork;
using Network.TCP;
using BD2FiendHunter.Shared;

namespace BD2FiendHunterRuntimePublic1 {
// Adapted from the tested Apostle network policy. Only the Action TCP instance is
// owned. Packet health comes from the current socket's receive queue, never sends.
internal static class NetworkGuard {
 const string Id="bd2.fiend-hunter.network.v1";
 static readonly object sync=new object();
 static readonly ConcurrentQueue<Tuple<string,object>> events=new ConcurrentQueue<Tuple<string,object>>();
 static NetworkRecoveryPolicy policy=new NetworkRecoveryPolicy();
 static object socket;static Harmony patch;static bool bypass,active;static Action<string,object> log;
 static NetworkConnectivityMonitor monitor;static int originalTimeout;
 static readonly FieldInfo timeout=AccessTools.Field(typeof(NetworkConnectivityMonitor),"connectionTimeout");
 static NetworkTCPManager Tcp=>ὫὨὪὠὢὨὮὤὩὤὮ.ὡὨὥὡὬὧὯὢὯὥὩ;
 static long Now=>(long)(Stopwatch.GetTimestamp()*(double)TimeSpan.TicksPerSecond/Stopwatch.Frequency);
 static bool Connected=>Tcp!=null&&Tcp.IsConnect(ὮὩὯὡὯὨὥὪὮὮὦ.Action);
 static bool Own(object value)=>ReferenceEquals(value,Tcp)&&value!=null;
 static void Bind(){var next=Tcp?.ὨὬὥὪὬὣὯὦὭὣὦ;if(!ReferenceEquals(next,socket)){policy.NewConnection(socket!=null);socket=next;Record("socket-changed");}}
 static void Record(string kind){events.Enqueue(Tuple.Create(kind,(object)new{policy.ProbeFailures,policy.AvoidedDisconnects,policy.NativeDisconnects,policy.ServerClosed,policy.AwaitingRound,policy.Deferred}));while(events.Count>128){Tuple<string,object> drop;events.TryDequeue(out drop);}}
 internal static void Install(Action<string,object> append){log=append;patch=new Harmony(Id);try{
  patch.Patch(AccessTools.Method(typeof(NetworkTCPManager),"OnNetworkLost"),prefix:new HarmonyMethod(typeof(NetworkGuard),nameof(Lost)));
  patch.Patch(AccessTools.Method(typeof(NetworkTCPManager),"OnNetworkRestored"),prefix:new HarmonyMethod(typeof(NetworkGuard),nameof(Restored)));
  patch.Patch(AccessTools.Method(typeof(ὮὯὮὦὥὤὣὣὦὠὨ),"ὭὮὤὢὯὭὢὭὤὩὥ"),prefix:new HarmonyMethod(typeof(NetworkGuard),nameof(Receive)));
 }catch{Remove();throw;}}
 static bool Lost(object __instance){if(bypass||!Own(__instance))return true;lock(sync){Bind();if(!active)return true;bool allow=policy.Lost(Now,Connected);Record(allow?"probe-disconnect-native":"probe-disconnect-deferred");return allow;}}
 static bool Restored(object __instance){if(bypass||!Own(__instance))return true;lock(sync){Bind();if(!active)return true;bool allow=policy.Restored(Now,Connected);Record(allow?"probe-restore-native":"probe-restore-kept-session");return allow;}}
 static void Receive(ὮὯὮὦὥὤὣὣὦὠὨ __instance,ὣὬὩὩὩὯὦὪὨὩὡ __0){
  if(!Own(__instance.ὭὫὦὯὥὦὢὬὦὭὯ)||!ReferenceEquals(Tcp.ὨὬὥὪὬὣὯὦὭὣὦ,__instance))return;
  lock(sync){Bind();if(!ReferenceEquals(socket,__instance))return;var kind=(ENetMsg)__0.ὩὪὦὬὧὥὦὡὠὩὡ;
   if(kind==ENetMsg.SCConnectCloseRequest){policy.ClosedByServer();Record("server-close-native");}
   else policy.Packet(Now,kind==ENetMsg.CSActionReconnectResponse||kind==ENetMsg.SCActionGameStartRequest);
  }
 }
 static void RestoreTimeout(){if(monitor!=null&&Convert.ToInt32(timeout.GetValue(monitor))==3000)timeout.SetValue(monitor,originalTimeout);monitor=null;}
 internal static void Pump(bool inRound){bool disconnect=false;lock(sync){active=inRound;Bind();
  if(!active){RestoreTimeout();}else{
   var found=NetworkConnectivityMonitor.ὪὫὢὨὯὭὦὪὦὨὣ;
   if(found.OnNetworkLost==null||!Own(found.OnNetworkLost.Target))RestoreTimeout();
   else if(!ReferenceEquals(monitor,found)){RestoreTimeout();monitor=found;originalTimeout=Convert.ToInt32(timeout.GetValue(found));if(originalTimeout<3000)timeout.SetValue(found,3000);}
   disconnect=policy.Tick(Now,Connected);if(disconnect)Record("probe-grace-expired-native");
  }
 }
 if(disconnect){try{bypass=true;Tcp.OnNetworkLost();}finally{bypass=false;}}
 Tuple<string,object> entry;while(events.TryDequeue(out entry))log(entry.Item1,entry.Item2);
 }
 internal static bool Hold{get{lock(sync)return active&&policy.Hold(Connected);}}
 internal static object State{get{lock(sync)return new{Active=active,Hold=active&&policy.Hold(Connected),policy.ProbeFailures,policy.AvoidedDisconnects,policy.NativeDisconnects,policy.ServerClosed,policy.AwaitingRound};}}
 internal static void Remove(){patch?.UnpatchAll(Id);patch=null;lock(sync){RestoreTimeout();active=false;socket=null;policy=new NetworkRecoveryPolicy();}}
}
}
