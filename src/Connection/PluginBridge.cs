using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
namespace BD2Daily.Live {
 internal interface IConnectionExtension {
  int ApiVersion {get;}
  void Execute(Command command,object surface);
  object Capture(string id);
 }
 // Declared routes avoid trying an action and catching its failure after dispatch.
 internal interface IConnectionRoutes { string[] Commands {get;} string[] Captures {get;} }
 internal interface IConnectionToolExtension {
  int ApiVersion {get;} string PluginId {get;} string[] Operations {get;}
  void Begin(string operation,string payload,Action<string,string> complete);
  void Tick();
 }
 [DataContract] internal sealed class PluginToolRequest {
  [DataMember] public string Id="",PluginId="",Operation="",Payload="",AccountKey="",PlayerKey="",Instance="";
  [DataMember] public int ProcessId;
  [DataMember] public long ProcessStartTicks,ExpiresUtcTicks;
 }
 [DataContract] internal sealed class PluginToolResponse {
  [DataMember] public string Id="",Result="",Error="",AccountKey="",PlayerKey="",Instance="";
  [DataMember] public bool Complete;
 }
 internal static class PluginHost {
  static readonly IConnectionExtension[] extensions=Load<IConnectionExtension>();
  static readonly IConnectionToolExtension[] tools=Load<IConnectionToolExtension>();
  static readonly Dictionary<string,IConnectionExtension> commands=Routes(true), captures=Routes(false);
  static readonly IConnectionExtension fallback=extensions.Where(e=>!(e is IConnectionRoutes)).SingleOrDefault();
  static PluginToolRequest active;static string completed="",result,error;static long deadline;static bool returned;
  static readonly object gate=new object();
  static T[] Load<T>() where T:class {
   var values=typeof(PluginHost).Assembly.GetTypes().Where(t=>t.IsClass&&!t.IsAbstract&&typeof(T).IsAssignableFrom(t)).Select(t=>(T)Activator.CreateInstance(t,true)).ToArray();
   foreach(var value in values){var daily=value as IConnectionExtension;var tool=value as IConnectionToolExtension;
    if((daily!=null&&daily.ApiVersion!=1)||(tool!=null&&tool.ApiVersion!=1))throw new InvalidOperationException("Plugin bridge API mismatch");}
   return values;
  }
  static Dictionary<string,IConnectionExtension> Routes(bool action){
   var routes=new Dictionary<string,IConnectionExtension>(StringComparer.Ordinal);
   foreach(var extension in extensions){var declared=extension as IConnectionRoutes;if(declared==null)continue;
    foreach(string id in action?declared.Commands:declared.Captures){if(routes.ContainsKey(id))throw new InvalidOperationException("Conflicting plugin route: "+id);routes.Add(id,extension);}}
   return routes;
  }
  internal static bool Available {get{return extensions.Length>0;}}
  static IConnectionExtension Route(Dictionary<string,IConnectionExtension> routes,string id){IConnectionExtension value;return routes.TryGetValue(id,out value)?value:fallback??throw new InvalidOperationException("Plugin route is unavailable: "+id);}
  internal static void Execute(Command c,object ui){Route(commands,c.Kind).Execute(c,ui);}
  internal static object Capture(string id){return Route(captures,id).Capture(id);}
  static T Read<T>(string path) where T:class {
   var bytes=BD2.LocalIpc.RuntimeFiles.Take(path);if(bytes==null)return null;
   if(bytes.Length>512*1024)throw new InvalidDataException("Plugin request is too large");
   using(var stream=new MemoryStream(bytes))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
  }
  static void Write(string root,PluginToolResponse value){
   using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(PluginToolResponse)).WriteObject(stream,value);BD2.LocalIpc.RuntimeFiles.Write(Path.Combine(root,"plugin-tool-response.json"),stream.ToArray());}
  }
  static bool Matches(PluginToolRequest r,Frame f){return f!=null&&r.ProcessId==f.ProcessId&&r.ProcessStartTicks==f.ProcessStartTicks&&r.Instance==f.Instance&&r.AccountKey==f.AccountKey&&r.PlayerKey==f.PlayerKey&&!string.IsNullOrEmpty(f.PlayerKey);}
  static bool Changed(PluginToolRequest r,Frame f){return f!=null&&((f.ProcessId>0&&r.ProcessId!=f.ProcessId)||(f.ProcessStartTicks>0&&r.ProcessStartTicks!=f.ProcessStartTicks)||(!string.IsNullOrEmpty(f.Instance)&&r.Instance!=f.Instance)||(!string.IsNullOrEmpty(f.AccountKey)&&r.AccountKey!=f.AccountKey)||(!string.IsNullOrEmpty(f.PlayerKey)&&r.PlayerKey!=f.PlayerKey));}
  internal static void Tick(Frame frame,string root,bool otherOwner){
   if(tools.Length==0)return;
   lock(gate){long now=DateTime.UtcNow.Ticks;
    if(active!=null){
     if(Changed(active,frame)){error="Game identity changed; reread the current account before continuing.";returned=true;}
     if(!returned&&Matches(active,frame))try{tools.Single(t=>t.PluginId==active.PluginId).Tick();}catch(Exception e){error=e.GetBaseException().Message;returned=true;}
     if((returned&&(error!=null||Matches(active,frame)))||now>deadline){
      Write(root,new PluginToolResponse{Id=active.Id,Complete=true,Result=result??"",Error=returned?error??"":"The operation timed out. Read the current preset again before retrying a save.",AccountKey=active.AccountKey,PlayerKey=active.PlayerKey,Instance=active.Instance});
      completed=active.Id;active=null;
     }return;
    }
    PluginToolRequest request;
    try{request=Read<PluginToolRequest>(Path.Combine(root,"plugin-tool-request.json"));}catch(SerializationException){return;}catch(IOException){return;}catch(System.Xml.XmlException){return;}
    if(request==null||request.Id==completed)return;
    try{
     Guid parsed;if(!Guid.TryParseExact(request.Id,"N",out parsed)||request.ExpiresUtcTicks<now||request.ExpiresUtcTicks>now+TimeSpan.FromSeconds(90).Ticks||!Matches(request,frame)||otherOwner)throw new InvalidOperationException("Plugin session expired or another operation is running; reconnect and retry.");
     var tool=tools.SingleOrDefault(t=>t.PluginId==request.PluginId&&t.Operations.Contains(request.Operation));
     if(tool==null)throw new InvalidOperationException("Plugin operation is unavailable; restart Dustweave after enabling the plugin.");
     if(!BD2.LocalIpc.RuntimeFiles.TryClaim(request.Id,request.ExpiresUtcTicks))throw new InvalidOperationException("This request was already dispatched. Read the current preset before trying again.");
     active=request;deadline=now+TimeSpan.FromSeconds(65).Ticks;returned=false;result=error=null;
     tool.Begin(request.Operation,request.Payload,(output,failure)=>{lock(gate){if(active!=request||returned)return;result=output;error=failure;returned=true;}});
    }catch(Exception e){Write(root,new PluginToolResponse{Id=request.Id,Complete=true,Error=e.GetBaseException().Message,AccountKey=request.AccountKey,PlayerKey=request.PlayerKey,Instance=request.Instance});completed=request.Id;active=null;}
   }
  }
 }
}
