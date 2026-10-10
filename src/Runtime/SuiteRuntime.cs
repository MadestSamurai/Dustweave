using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using BD2.LocalIpc;
namespace BD2Daily.Runtime {
 internal static class SuiteRuntime {
  const string RegistryKey="BD2.LocalIpc.Handoff.v1", OwnedKey="BD2.LocalIpc.SuiteModules.v1";
  static readonly Dictionary<string,Type> modules=new Dictionary<string,Type>();
  static readonly Dictionary<string,string> failures=new Dictionary<string,string>();
  static SuiteStatus status=new SuiteStatus();
  static SuiteCommand request;
  static RuntimeChannel channel;
  static string active="", lastRequest="", loadingId="";
  static DateTime began, idle;
  static int ownerPid;static long ownerStart;
  static bool initialized, paused, hasBundle;
  static List<Dictionary<string,object>> Registry {get{return AppDomain.CurrentDomain.GetData(RegistryKey) as List<Dictionary<string,object>>??new List<Dictionary<string,object>>();}}
  internal static bool ToolActive {get{return active!=""&&active!="daily"&&!active.EndsWith("-daily")||request!=null;}}
  internal static void Initialize(){
   if(initialized)return;initialized=true;
   var self=typeof(SuiteRuntime).Assembly;
   using(var stream=self.GetManifestResourceStream("Suite.manifest")){
    if(stream==null)return;hasBundle=true;
    var owned=AppDomain.CurrentDomain.GetData(OwnedKey) as HashSet<string>??new HashSet<string>();
    AppDomain.CurrentDomain.SetData(OwnedKey,owned);owned.Add(self.FullName);
    using(var reader=new StreamReader(stream))foreach(var line in reader.ReadToEnd().Split('\n')){
     if(string.IsNullOrWhiteSpace(line))continue;var parts=line.Trim().Split('|');
     if(parts[0].StartsWith("!")){failures[parts[0].Substring(1)]=System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));continue;}
     try{using(var bytes=self.GetManifestResourceStream("Suite."+parts[0]+".dll"))using(var memory=new MemoryStream()){
      bytes.CopyTo(memory);var assembly=Assembly.Load(memory.ToArray());
      modules.Add(parts[0],assembly.GetType(parts[1],true));
     }}catch(Exception e){failures[parts[0]]=e.GetBaseException().Message;}
    }
   }
   status.Available=modules.Keys.OrderBy(x=>x).ToArray();
   status.Fingerprints=modules.Select(x=>x.Key+"|"+(x.Key=="fiend-hunter"?x.Value.Module.ModuleVersionId.ToString("N"):(string)x.Value.Assembly.GetType("BD2.LocalIpc.Build").GetField("Fingerprint").GetRawConstantValue())).ToArray();
   
  }
  // Only the authenticated host's requested module is loaded. Compilation never runs on the game thread.
  static string Register(SuiteCommand command){
   if(SuiteRules.ModuleEntry(command.Tool)=="")return "module-unavailable";
   if(modules.ContainsKey(command.Tool))return "";
   if(string.IsNullOrEmpty(command.ModulePayload))return "module-not-prepared";
   try{
    if(command.ModulePayload.Length>12*1024*1024)return "module-payload-too-large";
    var bytes=Convert.FromBase64String(command.ModulePayload);
    if(bytes.Length==0||bytes.Length>8*1024*1024)return "module-payload-too-large";
    string hash;using(var sha=System.Security.Cryptography.SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");
    if(!string.Equals(hash,command.ModuleHash,StringComparison.OrdinalIgnoreCase))return "module-hash-mismatch";
    var assembly=Assembly.Load(bytes);
    var loader=assembly.GetType(SuiteRules.ModuleEntry(command.Tool),true);
    foreach(var method in new[]{"Load","Unload"})if(loader.GetMethod(method,BindingFlags.Public|BindingFlags.Static,null,Type.EmptyTypes,null)==null)throw new InvalidDataException("Missing module lifecycle: "+method);
    string fingerprint=command.Tool=="fiend-hunter"?loader.Module.ModuleVersionId.ToString("N"):(string)assembly.GetType("BD2.LocalIpc.Build",true).GetField("Fingerprint").GetRawConstantValue();
    if(string.IsNullOrEmpty(fingerprint))throw new InvalidDataException("Missing module fingerprint");
    modules.Add(command.Tool,loader);
    status.Available=modules.Keys.OrderBy(x=>x).ToArray();
    status.Fingerprints=status.Fingerprints.Where(x=>!x.StartsWith(command.Tool+"|",StringComparison.Ordinal)).Concat(new[]{command.Tool+"|"+fingerprint}).ToArray();
    failures.Remove(command.Tool);
    return "";
   }catch(Exception e){return "module-prepare-failed: "+e.GetBaseException().Message;}
  }
  internal static void Start(){
   Initialize();if(!hasBundle)return;
   ((HashSet<string>)AppDomain.CurrentDomain.GetData(OwnedKey)).Add(typeof(SuiteRuntime).Assembly.FullName);
   status=new SuiteStatus{Available=status.Available,Fingerprints=status.Fingerprints};
   active="";lastRequest="";loadingId="";request=null;ownerPid=0;ownerStart=0;
   channel=PipeBroker.Register(Path.Combine(LocalStorage.Root,"suite"),Build.Fingerprint,"command.json|status.json","",false);channel.Activate();
  }
  static Dictionary<string,object> Entry(string id){
   Type type;if(!modules.TryGetValue(id,out type))return null;var owned=AppDomain.CurrentDomain.GetData(OwnedKey) as HashSet<string>;if(owned==null||!owned.Contains(type.Assembly.FullName))return null;
   return Registry.FirstOrDefault(e=>(string)e["module"]==type.Assembly.FullName&&(bool)e["active"]);
  }
  static void Call(string id,string method){if(method=="Load")((HashSet<string>)AppDomain.CurrentDomain.GetData(OwnedKey)).Add(modules[id].Assembly.FullName);modules[id].GetMethod(method,BindingFlags.Public|BindingFlags.Static).Invoke(null,null);}
  static bool KeepDaily(string id){return request!=null&&request.Tool.EndsWith("-daily")&&id=="daily";}
  static void Retire(string id){
   var entry=Entry(id);if(entry==null)return;
   entry["paused"]=true;((Action)entry["pause"])();
   var reason=((Func<string>)entry["busy"])();if(reason.Length>0)throw new InvalidOperationException(reason);
   ((Action)entry["stop"])();entry["active"]=false;((HashSet<string>)AppDomain.CurrentDomain.GetData(OwnedKey)).Remove(modules[id].Assembly.FullName);
  }
  internal static void Stop(){
   if(loadingId!=""&&Entry(loadingId)==null){Call(loadingId,"Unload");loadingId="";}
   foreach(var id in modules.Keys){var entry=Entry(id);if(entry==null)continue;entry["paused"]=true;((Action)entry["pause"])();}
  }
  internal static string Busy(){
   foreach(var id in modules.Keys){var entry=Entry(id);if(entry!=null){var reason=((Func<string>)entry["busy"])();if(reason.Length>0)return id+": "+reason;}}
   return "";
  }
  internal static void Dispose(){Stop();if(Busy()!="")throw new InvalidOperationException(Busy());foreach(var id in modules.Keys)Retire(id);active="";var owned=AppDomain.CurrentDomain.GetData(OwnedKey) as HashSet<string>;if(owned!=null)owned.Remove(typeof(SuiteRuntime).Assembly.FullName);if(channel!=null)channel.Revoke();}
  static T Read<T>(byte[] bytes)where T:class{if(bytes==null)return null;using(var memory=new MemoryStream(bytes))return(T)new DataContractJsonSerializer(typeof(T)).ReadObject(memory);}
  static void Save(){using(var memory=new MemoryStream()){new DataContractJsonSerializer(typeof(SuiteStatus)).WriteObject(memory,status);channel.Write("status.json",memory.ToArray());}}
  internal static void Tick(DailySnapshot identity){
   Initialize();if(channel==null)return;
   var now=DateTime.UtcNow;
   try{
    var incoming=Read<SuiteCommand>(channel.Take("command.json"));
    if(incoming!=null&&incoming.Id!=lastRequest){
     lastRequest=incoming.Id;
     var error=SuiteRules.Validate(incoming,identity,now.Ticks);

     if(request!=null)error="switch-already-pending";
     if(error=="")error=Register(incoming);
     incoming.ModulePayload=""; // Release payload before waiting for the current operation.
     if(error!=""){status.RejectedRequest=incoming.Id;status.RejectedReason=error;if(request==null){status.Error=error;status.Request=incoming.Id;status.State="error";}}
     else{
      request=incoming;status.Request=incoming.Id;status.Owner=incoming.Owner;status.Account=incoming.Account;status.Player=incoming.Player;
      ownerPid=incoming.OwnerProcessId;ownerStart=incoming.OwnerStartTicks;
      status.State="switching";status.Error="";began=now;idle=default(DateTime);loadingId="";paused=false;
     }
    }
    if(request==null&&active!=""&&(Entry(active)==null||Entry(active).ContainsKey("paused")&&(bool)Entry(active)["paused"])) {Stop();if(status.State!="paused"&&status.State!="error"){status.State="paused";status.Error="suite.control-changed";}if(Busy()==""){foreach(var id in modules.Keys)Retire(id);active="";}}
    status.IdentityState=identity.State;status.OwnerCheck=SuiteOwnerProbe.Read(ownerPid,ownerStart);status.OwnerProblem=status.Owner==""?"":status.OwnerCheck.Problem;
    string revokeReason=status.Owner==""?"":SuiteRules.Revocation(status.OwnerProblem,status,identity);
    bool revoked=revokeReason!="";
    if(revoked&&(active!=""||request!=null)){
     Stop();request=null;status.State="paused";status.Error=revokeReason;
     if(Busy()==""){foreach(var id in modules.Keys)Retire(id);active="";}
    }else if(request!=null){
     if(now-began>TimeSpan.FromSeconds(30))throw new TimeoutException("等待当前操作结束超时；保持暂停，不重复执行。");
     if(loadingId==""){
      if(!paused){foreach(var id in modules.Keys){if(KeepDaily(id))continue;var entry=Entry(id);if(entry!=null){entry["paused"]=true;((Action)entry["pause"])();}}paused=true;}
      var reason=Busy();
      if(reason!=""){status.Error=reason;idle=default(DateTime);}
      else if(idle==default(DateTime))idle=now;
      else if(now-idle>=TimeSpan.FromMilliseconds(200)){
       foreach(var id in modules.Keys)if(!KeepDaily(id))Retire(id);
       if(request.Tool.EndsWith("-daily")&&Entry("daily")==null)throw new InvalidOperationException("Daily engine is not active");
       active="";loadingId=request.Tool;Call(request.Tool,"Load");
      }
     }else{
      var entry=Entry(request.Tool);
      if(entry!=null&&(!entry.ContainsKey("paused")||!(bool)entry["paused"])){
       active=request.Tool;status.Tool=active;status.State="ready";status.Error="";request=null;loadingId="";
      }
     }
    }
   }catch(Exception e){Stop();request=null;status.State="error";status.Error=e.GetBaseException().Message;}
   status.At=now.Ticks;status.Tool=active;status.Pending=Busy();Save();
  }
 }
}
