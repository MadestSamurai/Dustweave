using System;
using System.Collections;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using HarmonyLib;
using Google.Protobuf;
namespace BD2Equipment.Live {
 [DataContract] public sealed class EvidenceConfig {
  [DataMember] public string Mvid="";
  [DataMember] public TapRule[] Taps=new TapRule[0];
  [DataMember] public ReadRule[] Reads=new ReadRule[0];
 }
 [DataContract] public sealed class TapRule {
  [DataMember] public string Role="",ResponseType="";
  [DataMember] public int RequestToken,ResponseToken;
  [DataMember] public int[] ResponseTokens;
  [DataMember] public string[] Paths=new string[0];
 }
 [DataContract] public sealed class ReadRule {
  [DataMember] public string Id="",Type="",StaticMember="";
  [DataMember] public string CollectionPath="";
  [DataMember] public string[] ItemPaths=new string[0];
  [DataMember] public int MaxItems=300;
  [DataMember] public string[] Paths=new string[0];
 }
 [DataContract] public sealed class Value {
  [DataMember] public string Path="",Json="",Error="";
 }
 [DataContract] public sealed class Reading {
  [DataMember] public string Id="",Error="";
  [DataMember] public int InstanceId;
  [DataMember] public Value[] Values=new Value[0];
 }
 [DataContract] public sealed class ObservationRequest {
  [DataMember] public long ExpiresUtcTicks;
  [DataMember] public string Id="";
  [DataMember] public string[] Prefixes=new string[0];
 }
 [DataContract] public sealed class EvidenceState {
  [DataMember] public long AtUtcTicks;
  [DataMember] public Frame Frame;
  [DataMember] public string Error="",Config="",ObservationRequest="";
  [DataMember] public string[] Taps=new string[0];
  [DataMember] public Reading[] Readings=new Reading[0];
 }
 [DataContract] public sealed class EvidenceEvent {
  [DataMember] public long AtUtcTicks,Sequence;
  [DataMember] public string Role="",Kind="",Error="";
  [DataMember] public Frame Frame;
  [DataMember] public int ErrorCode;
  [DataMember] public bool Accepted;
  [DataMember] public Value[] Values=new Value[0];
 }
 internal static class Evidence {
  internal static string[] Costs=new string[0];
  private static readonly Dictionary<Type,DataContractJsonSerializer> serializers=new Dictionary<Type,DataContractJsonSerializer>();
  private static DataContractJsonSerializer Serializer(Type type){DataContractJsonSerializer s;if(!serializers.TryGetValue(type,out s))serializers[type]=s=new DataContractJsonSerializer(type);return s;}
  private static ObservationRequest Request(){var bytes=BD2.LocalIpc.RuntimeFiles.Read(Path.Combine(root,"observation-request.json"));if(bytes==null)return null;using(var memory=new MemoryStream(bytes))return(ObservationRequest)new DataContractJsonSerializer(typeof(ObservationRequest)).ReadObject(memory);}
  private static readonly Dictionary<string,int> inFlight=new Dictionary<string,int>();
  internal static bool Waiting{get{lock(queue)return inFlight.Values.Any(v=>v>0);}}
  [DataContract] private sealed class EventBatch{[DataMember]public EvidenceEvent[] Events;}
  private static readonly Queue<EvidenceEvent> recent=new Queue<EvidenceEvent>();

  private static string root,last="",error="";private static long seq;
  private static Frame frame;private static Harmony harmony;private static EvidenceConfig config;
  private static readonly Dictionary<MethodBase,TapRule> requests=new Dictionary<MethodBase,TapRule>(),responses=new Dictionary<MethodBase,TapRule>();
  private static readonly Queue<EvidenceEvent> queue=new Queue<EvidenceEvent>();
  internal static void Start(string path){root=path;harmony=new Harmony("bd2.equipment.live.evidence.v1");Directory.CreateDirectory(Path.Combine(root,"events"));}
  internal static void Stop(){if(harmony!=null){foreach(var m in requests.Keys.Concat(responses.Keys))harmony.Unpatch(m,HarmonyPatchType.All,harmony.Id);}requests.Clear();responses.Clear();harmony=null;}
  private static string Scalar(object value){
   if(value==null)return "null";
   var msg=value as IMessage;if(msg!=null)return JsonFormatter.Default.Format(msg);
   if(value is DateTime)return ((DateTime)value).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
   var items=value as IEnumerable;if(items!=null&&!(value is string))return "["+string.Join(",",items.Cast<object>().Take(300).Select(Scalar).ToArray())+"]";
   if(value.GetType().IsEnum)value=value.ToString();
   using(var ms=new MemoryStream()){(Bridge.LegacyObservation?new DataContractJsonSerializer(value.GetType()):Serializer(value.GetType())).WriteObject(ms,value);return Encoding.UTF8.GetString(ms.ToArray());}
  }
  private static object Member(object instance,Type type,string name,bool isStatic=false){
   name=ClientNames.Map(name);
   var flags=BindingFlags.Public|BindingFlags.NonPublic|(isStatic?BindingFlags.Static:BindingFlags.Instance);
   if(name.EndsWith("()",StringComparison.Ordinal)){
    bool clock=name=="UnixTimeStamp()"&&type.FullName=="gamfs.Thread.TimerManager";
    bool resetClock=type.FullName=="gamfs.Thread.TimerManager"&&new[]{"Now()","GetDailyResetTime()","GetWeeklyResetTime()"}.Contains(name);
    bool instanceId=name=="GetInstanceID()"&&instance is UnityEngine.Object;
    if(isStatic||!(instanceId||clock||resetClock||name=="IsCanSettlement()"||name=="IsDisable()"||name=="IsUnlocked()"||name=="IsCanOverwhelm()"||name=="HasCanOverwhelmMonster()"||name=="IsOverwhelmActive()"))throw new InvalidOperationException("Only vetted read-only predicates allowed");
    var method=type.GetMethod(name.Substring(0,name.Length-2),flags,null,Type.EmptyTypes,null);
    if(method==null||method.ReturnType!=(instanceId?typeof(int):clock?typeof(long):resetClock?typeof(DateTime):typeof(bool)))throw new MissingMemberException(name);return method.Invoke(instance,null);
   }
   for(var t=type;t!=null;t=t.BaseType){
    var p=t.GetProperty(name,flags|BindingFlags.DeclaredOnly);if(p!=null&&p.GetIndexParameters().Length==0)return p.GetValue(instance,null);
    var f=t.GetField(name,flags|BindingFlags.DeclaredOnly);if(f!=null)return f.GetValue(instance);
   }
   throw new MissingMemberException(type.FullName,name);
  }
  private static Value[] Values(object item,string[] paths){return paths.Select(path=>{
   var result=new Value{Path=path};try{object value=item;foreach(var part in path=="$self"?new string[0]:path.Split('.')){if(value==null)break;value=Member(value,value.GetType(),part);}result.Json=Scalar(value);}catch(Exception e){result.Error=e.GetBaseException().Message;}return result;
  }).ToArray();}
  private static Value[] ReadValues(object value,ReadRule rule){
   var result=Values(value,rule.Paths).ToList();
   if(!string.IsNullOrEmpty(rule.CollectionPath)){
    var projected=new Value{Path="$items"};
    try{projected.Json=ObservationProjection.Capture(value,rule.CollectionPath,rule.ItemPaths,rule.MaxItems,(obj,name)=>Member(obj,obj.GetType(),name),Scalar);}
    catch(Exception e){projected.Error=e.GetBaseException().Message;}
    result.Add(projected);
   }
   return result.ToArray();
  }
  private static void Write(string file,object value,bool durable=true){using(var memory=new MemoryStream()){new DataContractJsonSerializer(value.GetType()).WriteObject(memory,value);if(BD2.LocalIpc.RuntimeFiles.Write(file,memory.ToArray()))return;}var tmp=file+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var f=File.Create(tmp)){new DataContractJsonSerializer(value.GetType()).WriteObject(f,value);f.Flush(durable);}if(File.Exists(file))File.Replace(tmp,file,null);else File.Move(tmp,file);}finally{if(File.Exists(tmp))File.Delete(tmp);}}
  private static void Configure(string text){
   EvidenceConfig next;using(var ms=new MemoryStream(Encoding.UTF8.GetBytes(text)))next=(EvidenceConfig)new DataContractJsonSerializer(typeof(EvidenceConfig)).ReadObject(ms);
   var game=typeof(UIBase).Assembly;if(next.Mvid!=game.ManifestModule.ModuleVersionId.ToString())throw new InvalidOperationException("Evidence client MVID mismatch");
   if(next.Taps.Select(r=>r.Role).Distinct().Count()!=next.Taps.Length)throw new InvalidOperationException("Duplicate evidence role");
   var planned=new List<Tuple<TapRule,MethodBase,MethodInfo>>();
   foreach(var r in next.Taps){
    var req=game.ManifestModule.ResolveMethod(r.RequestToken);
    foreach(int token in r.ResponseTokens??new[]{r.ResponseToken}){var resp=(MethodInfo)game.ManifestModule.ResolveMethod(token);
    var pars=resp.GetParameters();if(resp.ReturnType!=typeof(bool)||pars.Length!=3||pars[0].ParameterType!=typeof(byte[])||pars[1].ParameterType!=typeof(int)||pars[2].ParameterType!=typeof(int))throw new InvalidOperationException("Unsupported response shape: "+r.Role);
    if(!typeof(IMessage).IsAssignableFrom(game.GetType(r.ResponseType,true)))throw new InvalidOperationException("Invalid DTO");
    planned.Add(Tuple.Create(r,req,resp));}
   }
   Stop();harmony=new Harmony("bd2.equipment.live.evidence.v1");
   foreach(var pair in planned){
    if(!requests.ContainsKey(pair.Item2)){
     requests.Add(pair.Item2,pair.Item1);
     harmony.Patch(pair.Item2,prefix:new HarmonyMethod(typeof(Evidence).GetMethod("Before",BindingFlags.NonPublic|BindingFlags.Static)));
    }else if(requests[pair.Item2].Role!=pair.Item1.Role)throw new InvalidOperationException("Request has conflicting roles");
    responses.Add(pair.Item3,pair.Item1);
    harmony.Patch(pair.Item3,postfix:new HarmonyMethod(typeof(Evidence).GetMethod("After",BindingFlags.NonPublic|BindingFlags.Static)));
   }
   config=next;last=text;error="";
  }
  private static void Before(MethodBase __originalMethod){
   try{TapRule rule;if(!requests.TryGetValue(__originalMethod,out rule))return;
    lock(queue){int n;inFlight.TryGetValue(rule.Role,out n);inFlight[rule.Role]=n+1;queue.Enqueue(new EvidenceEvent{AtUtcTicks=DateTime.UtcNow.Ticks,Role=rule.Role,Kind="request",Frame=frame,Sequence=++seq});}
   }catch{}
  }
  private static void After(MethodBase __originalMethod,byte[] __0,int __2,bool __result){
   try{TapRule rule;if(!responses.TryGetValue(__originalMethod,out rule))return;
    var e=new EvidenceEvent{AtUtcTicks=DateTime.UtcNow.Ticks,Role=rule.Role,Kind="response",Frame=frame,ErrorCode=__2,Accepted=__result,Sequence=++seq};
    try{if(__0!=null){var type=typeof(UIBase).Assembly.GetType(rule.ResponseType,true);var parser=type.GetProperty("Parser").GetValue(null,null);var parse=parser.GetType().GetMethod("ParseFrom",new[]{typeof(byte[])});e.Values=Values(parse.Invoke(parser,new object[]{__0}),rule.Paths);}}catch(Exception ex){e.Error=ex.GetBaseException().Message;}
    lock(queue){int n;inFlight.TryGetValue(rule.Role,out n);inFlight[rule.Role]=Math.Max(0,n-1);queue.Enqueue(e);}
   }catch{}
  }
  internal static void Tick(Frame current){
   frame=current;
   try{
    NativeCatalog.Tick(root,current);
    var bytes=BD2.LocalIpc.RuntimeFiles.Read(Path.Combine(root,"evidence-config.json"));if(bytes!=null){string text=Encoding.UTF8.GetString(bytes);if(text!=last&&!Waiting)Configure(text);}
    var state=new EvidenceState{AtUtcTicks=DateTime.UtcNow.Ticks,Frame=current,Config=Convert.ToBase64String(System.Security.Cryptography.SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(last)))};
    var request=Request();state.ObservationRequest=request==null?"":request.Id;var costs=new List<string>();
    if(config!=null){
     state.Taps=config.Taps.Select(r=>r.Role).ToArray();var readings=new List<Reading>();
     foreach(var r in LivePolicy.GameplayReady(current)?config.Reads:new ReadRule[0]){
      if(!Bridge.LegacyObservation&&(request==null||request.ExpiresUtcTicks<DateTime.UtcNow.Ticks||!request.Prefixes.Any(prefix=>prefix=="*"||r.Id==prefix||r.Id.StartsWith(prefix+".",StringComparison.Ordinal))))continue;
      var timer=Stopwatch.StartNew();
      try{var type=typeof(UIBase).Assembly.GetType(ClientNames.Map(r.Type),true);
       if(!string.IsNullOrEmpty(r.StaticMember)){var value=Member(null,type,r.StaticMember,true);readings.Add(new Reading{Id=r.Id,Values=ReadValues(value,r)});}
       else foreach(var value in (Bridge.LegacyObservation?UnityEngine.Object.FindObjectsOfType(type):Bridge.Find(type))){var component=value as UnityEngine.Component;if(component!=null&&component.gameObject.activeInHierarchy)readings.Add(new Reading{Id=r.Id,InstanceId=component.GetInstanceID(),Values=ReadValues(value,r)});}
      }catch(Exception e){readings.Add(new Reading{Id=r.Id,Error=e.GetBaseException().Message});}
      costs.Add(r.Id+"="+timer.Elapsed.TotalMilliseconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture));
     }
     state.Readings=readings.ToArray();
    }
    bool changed=false;while(true){EvidenceEvent item;lock(queue){if(queue.Count==0)break;item=queue.Dequeue();}recent.Enqueue(item);while(recent.Count>128)recent.Dequeue();changed=true;}
    if(changed)Write(Path.Combine(root,"events.json"),new EventBatch{Events=recent.ToArray()},false);
    Costs=costs.ToArray();state.Error="";Write(Path.Combine(root,"evidence.json"),state,false);error="";
   }catch(Exception e){error=e.GetBaseException().ToString();Write(Path.Combine(root,"evidence.json"),new EvidenceState{AtUtcTicks=DateTime.UtcNow.Ticks,Frame=current,Error=error});}
  }
 }
}
