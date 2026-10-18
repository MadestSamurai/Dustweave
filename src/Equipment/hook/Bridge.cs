using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using gamfs;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
namespace BD2Equipment.Live {
 public static class Bridge {
  private static readonly string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2EquipmentAssistant");
  private static readonly string live=Path.Combine(root,"live");
  private static readonly string instance=Guid.NewGuid().ToString("N");
  private static bool installed,busy,retiring,resolver;private static BD2.LocalIpc.Handoff handoff;private static BD2.LocalIpc.MainThread pump;private static long next,sequence,start;private static int pid;
  private static int lastFrame=-1,renderFrames,slowFrames;private static double maxFrameMs;
  private static long profileAt;private static int profileCount;private static double observeMs,evidenceMs,writeMs,maxMs;
  internal static bool LegacyObservation;
  private static MonoBehaviour[] sceneComponents;
  internal static IEnumerable<UnityEngine.Object> Find(Type type){
   if(typeof(UIBase).IsAssignableFrom(type))return uis.Values.Where(u=>u!=null&&type.IsInstanceOfType(u)).Cast<UnityEngine.Object>();
   if(!typeof(MonoBehaviour).IsAssignableFrom(type))throw new InvalidOperationException("Observation type must be a scene component");
   if(sceneComponents==null)sceneComponents=UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
   return sceneComponents.Where(c=>c!=null&&type.IsInstanceOfType(c)).Cast<UnityEngine.Object>();
  }

  private static Frame frame;private static Receipt pending;
  private static readonly Dictionary<int,UIBase> uis=new Dictionary<int,UIBase>();
  private static readonly Dictionary<int,GameObject> targets=new Dictionary<int,GameObject>();
  [DataContract] private sealed class Performance {
   [DataMember]public long AtUtcTicks;[DataMember]public int Count,TargetFps,RenderFrames,SlowFrames;[DataMember]public double MaxFrameMs;
   [DataMember]public double ObserveMs,EvidenceMs,WriteMs,MaxMs,FrameDeltaMs,TimeScale;
   [DataMember]public bool Legacy,Focused;[DataMember]public string[] Reads;
  }
  [DataContract] private sealed class Lease {[DataMember]public long ExpiresUtcTicks;[DataMember]public string Owner;}
  public static void Load(){
   if(!resolver){AppDomain.CurrentDomain.AssemblyResolve+=Resolve;resolver=true;}
   if(handoff==null)handoff=new BD2.LocalIpc.Handoff(typeof(Bridge).Assembly.FullName,"equipment","equipment",false,()=>Start(),Pause,HandoffBusy,Stop,Status);
   if(!handoff.IsActive&&!handoff.Pending)BD2.LocalIpc.RuntimeFiles.Start(live,BD2.LocalIpc.Build.Fingerprint,LiveProtocol.LiveEntries);
   handoff.Request(DateTime.UtcNow);if(pump==null)pump=new BD2.LocalIpc.MainThread(()=>{BD2.LocalIpc.LegacyPilots.Discover();handoff.Tick(DateTime.UtcNow);return handoff.Pending;},handoff.Fail);pump.Schedule();
  }
  static void Status(string state,string error){if(state=="active"&&handoff!=null&&handoff.IsActive)BD2.LocalIpc.RuntimeFiles.Activate();Write(Path.Combine(live,"runtime.json"),new ComponentStatus{State=state,Error=error,AtUtcTicks=DateTime.UtcNow.Ticks});}
  [DataContract] sealed class ComponentStatus{[DataMember]public string State,Error;[DataMember]public long AtUtcTicks;}
  static void Pause(){retiring=true;BD2.LocalIpc.RuntimeFiles.Revoke();}
  static string HandoffBusy(){if(busy||pending!=null)return "command observation";if(Evidence.Waiting)return "equipment request awaiting response";if(EquipmentToolsNative.Busy())return "native batch refinement";return "";}
  public static void Unload(){BD2.LocalIpc.MainThread.Drain(()=>{if(handoff!=null)handoff.Unload();},Status);}
  private static Assembly Resolve(object sender,ResolveEventArgs args){if(new AssemblyName(args.Name).Name!="0Harmony")return null;var old=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name=="0Harmony");if(old!=null)return old;using(var s=typeof(Bridge).Assembly.GetManifestResourceStream("Equipment.Harmony.dll"))using(var b=new MemoryStream()){s.CopyTo(b);return Assembly.Load(b.ToArray());}}
  [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
  private static void Start(){lock(typeof(Bridge)){
   if(installed)return;retiring=false;BD2.LocalIpc.RuntimeFiles.Start(live,BD2.LocalIpc.Build.Fingerprint,LiveProtocol.LiveEntries);
   if(typeof(UIBase).Assembly.ManifestModule.ModuleVersionId.ToString()!=ClientNames.Mvid)throw new InvalidOperationException("Client changed after compilation; reconnect.");
   Directory.CreateDirectory(live);Directory.CreateDirectory(Path.Combine(live,"receipts"));Directory.CreateDirectory(Path.Combine(live,"claimed"));
   using(var p=Process.GetCurrentProcess()){pid=p.Id;start=p.StartTime.ToUniversalTime().Ticks;}
   // Equipment-only observation. Commands require a fresh account, scene and approved plan.
   // No autonomous action, no replacement of active game hooks, no game restart.
   UnityEngine.Canvas.willRenderCanvases+=Tick;installed=true;Evidence.Start(live);
   Write(Path.Combine(live,"attached.json"),new Frame{ProcessId=pid,ProcessStartTicks=start,Instance=instance,AtUtcTicks=DateTime.UtcNow.Ticks});
  }}
  private static void Stop(){if(resolver){AppDomain.CurrentDomain.AssemblyResolve-=Resolve;resolver=false;}Evidence.Shutdown();UnityEngine.Canvas.willRenderCanvases-=Tick;installed=false;uis.Clear();targets.Clear();}
  private static T Read<T>(string file)where T:class{
   try{var bytes=BD2.LocalIpc.RuntimeFiles.Read(file);if(BD2.LocalIpc.RuntimeFiles.Handles(file)){if(bytes==null)return null;using(var memory=new MemoryStream(bytes))return(T)new DataContractJsonSerializer(typeof(T)).ReadObject(memory);}using(var f=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))return(T)new DataContractJsonSerializer(typeof(T)).ReadObject(f);}
   catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}
  }
  private static void Write(string file,object data,bool durable=true){
   using(var memory=new MemoryStream()){new DataContractJsonSerializer(data.GetType()).WriteObject(memory,data);if(BD2.LocalIpc.RuntimeFiles.Write(file,memory.ToArray()))return;}
   var tmp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{using(var f=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){new DataContractJsonSerializer(data.GetType()).WriteObject(f,data);f.Flush(durable);}
    if(File.Exists(file))File.Replace(tmp,file,null);else File.Move(tmp,file);
   }finally{if(File.Exists(tmp))File.Delete(tmp);}
  }
  private static IEnumerable<FieldInfo> Fields(Type t){for(;t!=null&&t!=typeof(MonoBehaviour);t=t.BaseType)foreach(var f in t.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly))yield return f;}
  private static string PathOf(Transform t){var p=new List<string>();for(;t!=null;t=t.parent)p.Add(t.name);p.Reverse();return string.Join("/",p.ToArray());}
  private static GameObject Go(object value){var g=value as GameObject;if(g!=null)return g;var c=value as Component;return c==null?null:c.gameObject;}
  private static IEnumerable<KeyValuePair<string,GameObject>> Links(UIBase u){
   foreach(var field in Fields(u.GetType())){
    object value;try{value=field.GetValue(u);}catch{continue;}
    var g=Go(value);if(g!=null){yield return new KeyValuePair<string,GameObject>(field.Name,g);if(value.GetType().Name!="SliderSelectCount")continue;}
    if(value==null||value.GetType().Assembly!=typeof(UIBase).Assembly||!(value.GetType().IsNested||value.GetType().Name=="HuntDispatchUIButton"||value.GetType().Name=="TabButton"||value.GetType().Name=="SliderSelectCount"))continue;
    foreach(var sub in value.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)){
     var nested=Go(sub.GetValue(value));if(nested!=null)yield return new KeyValuePair<string,GameObject>(field.Name+"."+sub.Name,nested);
    }
   }
  }
  private static bool InputReady(UIBase u){
   if(!u.ὣὤὥὦὯὦὩὤὨὠὪ||!u.ὪὯὣὥὬὫὬὩὠὮὠ||(u.ὫὥὧὬὭὫὡὠὠὪὠ&&u.ὦὡὮὫὧὠὮὡὭὭὬ))return false;
   return true;
  }
  private static Frame Observe(){
   var f=new Frame{ProcessId=pid,ProcessStartTicks=start,Instance=instance,AtUtcTicks=DateTime.UtcNow.Ticks,Sequence=++sequence,Scene=SceneManager.GetActiveScene().name??""};
   try {
    if(!Neo.Unity.Neon.NeonSdk.IsInitialized){f.Error="Waiting for login SDK initialization";return f;}
    var member=Neo.Unity.Neon.NeonSdk.Auth==null?null:Neo.Unity.Neon.NeonSdk.Auth.LoggedMember;
    if(member!=null)f.AccountKey=Hash("bd2-member-v1|"+member.MemberId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    var user=(Proto.Net.UserDBInfo)IdentityProperty.GetValue(null,null);
    if(user!=null&&user.OwnerIndex>0&&!string.IsNullOrEmpty(f.AccountKey))f.PlayerKey=Hash("bd2-player-v1|"+f.AccountKey+"|"+user.OwnerIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
    else f.Error="Waiting for account login";
   }catch(Exception e){f.Error=e.GetBaseException().Message;}
   sceneComponents=null;uis.Clear();targets.Clear();var surfaces=new List<Surface>();
   foreach(var u in UnityEngine.Object.FindObjectsOfType<UIBase>().Where(u=>u!=null&&u.gameObject.activeInHierarchy)){
    uis[u.GetInstanceID()]=u;
    var canvas=u.GetComponent<Canvas>();var popup=Fields(u.GetType()).FirstOrDefault(x=>x.Name=="_isPopupUI");
    var sf=new Surface{Id=u.GetInstanceID(),Type=u.GetType().Name,Path=PathOf(u.transform),Popup=popup!=null&&(bool)popup.GetValue(u),Order=canvas==null?0:canvas.sortingOrder,InputReady=InputReady(u)};
    var choices=new List<Target>();
    foreach(var link in Links(u)){
     var go=link.Value;if(!go.activeInHierarchy)continue;var button=go.GetComponent<Selectable>();
     // Only click-like serialized references become targets; never arbitrary child objects.
     if(button==null&&link.Key.IndexOf("button",StringComparison.OrdinalIgnoreCase)<0)continue;
     bool enabled=button==null||(button.isActiveAndEnabled&&button.IsInteractable());
     targets[go.GetInstanceID()]=go;choices.Add(new Target{Id=go.GetInstanceID(),Field=link.Key,Path=PathOf(go.transform),Enabled=enabled});
    }
    foreach(var component in u.GetComponentsInChildren<MonoBehaviour>()){
     if(!(component is IPointerClickHandler)||!component.isActiveAndEnabled)continue;
     var go=component.gameObject;var button=go.GetComponent<Selectable>();
     if(choices.Any(t=>t.Id==go.GetInstanceID()&&t.Route=="pointer"))continue;
     targets[go.GetInstanceID()]=go;
     choices.Add(new Target{Id=go.GetInstanceID(),Route="pointer",Field="$pointer/"+(go==u.gameObject?"$self":PathOf(go.transform).Substring(PathOf(u.transform).Length+1)),Path=PathOf(go.transform),Enabled=button==null||button.IsInteractable()});
    }
    sf.Targets=choices.ToArray();
    sf.Text=u.GetComponentsInChildren<TMPro.TMP_Text>().Where(t=>t!=null&&t.isActiveAndEnabled&&!string.IsNullOrWhiteSpace(t.text)).Select(t=>t.text).Concat(u.GetComponentsInChildren<Text>().Where(t=>t!=null&&t.isActiveAndEnabled&&!string.IsNullOrWhiteSpace(t.text)).Select(t=>t.text)).Take(80).Select(t=>t.Length>400?t.Substring(0,400):t).ToArray();
    surfaces.Add(sf);
   }
   f.Surfaces=surfaces.OrderBy(s=>s.Id).ToArray();
   var key=f.Scene+"|"+string.Join("|",f.Surfaces.Select(s=>s.Id+":"+s.Order+":"+string.Join(",",s.Targets.Select(t=>t.Id+":"+t.Enabled))).ToArray());
   using(var sha=SHA256.Create())f.UiToken=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-","");
   return f;
  }
  private static void Save(Receipt r){r.AtUtcTicks=DateTime.UtcNow.Ticks;Write(Path.Combine(live,"receipts",r.Command.Id+".json"),r);}
  private static readonly PropertyInfo IdentityProperty=typeof(UIBase).Assembly.GetTypes().SelectMany(t=>t.GetProperties(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)).Single(p=>p.PropertyType==typeof(Proto.Net.UserDBInfo)&&p.GetIndexParameters().Length==0);
  private static string Hash(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
  private static void Tick(){
   if(!installed)return;
   if(UnityEngine.Time.frameCount!=lastFrame){lastFrame=UnityEngine.Time.frameCount;renderFrames++;var ms=UnityEngine.Time.unscaledDeltaTime*1000;maxFrameMs=Math.Max(maxFrameMs,ms);if(ms>50)slowFrames++;}
   if(busy||DateTime.UtcNow.Ticks<next)return;busy=true;next=DateTime.UtcNow.AddMilliseconds((BD2.LocalIpc.RuntimeFiles.Read(Path.Combine(live,"pause"))!=null)&&!LegacyObservation?1000:500).Ticks;
   try{
    LegacyObservation=(BD2.LocalIpc.RuntimeFiles.Read(Path.Combine(live,"legacy-observation"))!=null);var timer=Stopwatch.StartNew();frame=Observe();
    var observed=timer.Elapsed.TotalMilliseconds;
    Evidence.Tick(frame);var evidenced=timer.Elapsed.TotalMilliseconds;
    Write(Path.Combine(live,"snapshot.json"),frame,false);var total=timer.Elapsed.TotalMilliseconds;
    observeMs+=observed;evidenceMs+=evidenced-observed;writeMs+=total-evidenced;maxMs=Math.Max(maxMs,total);profileCount++;
    if(DateTime.UtcNow.Ticks>=profileAt){
     Write(Path.Combine(live,"performance.json"),new Performance{AtUtcTicks=DateTime.UtcNow.Ticks,Count=profileCount,ObserveMs=observeMs/profileCount,EvidenceMs=evidenceMs/profileCount,WriteMs=writeMs/profileCount,MaxMs=maxMs,Legacy=LegacyObservation,FrameDeltaMs=UnityEngine.Time.unscaledDeltaTime*1000,TimeScale=UnityEngine.Time.timeScale,Focused=Application.isFocused,TargetFps=Application.targetFrameRate,Reads=Evidence.Costs,RenderFrames=renderFrames,SlowFrames=slowFrames,MaxFrameMs=maxFrameMs});
     profileAt=DateTime.UtcNow.AddSeconds(10).Ticks;profileCount=0;renderFrames=slowFrames=0;maxFrameMs=0;observeMs=evidenceMs=writeMs=maxMs=0;
    }
    if(pending!=null){pending.After=frame;pending.State="observed_after_dispatch";Save(pending);pending=null;}
    if(retiring)return;
    var path=Path.Combine(live,"command.json");var bytes=BD2.LocalIpc.RuntimeFiles.Take(path);if(bytes==null)return;
    Command c;using(var memory=new MemoryStream(bytes))c=(Command)new DataContractJsonSerializer(typeof(Command)).ReadObject(memory);if(c==null)return;
    Guid id;if(c.Id==null||c.Id.Length!=32||!Guid.TryParseExact(c.Id,"N",out id))throw new InvalidDataException("Invalid command ID");
    if(!BD2.LocalIpc.RuntimeFiles.TryClaim(c.Id,c.ExpiresUtcTicks))return;
    var r=new Receipt{Command=c,Before=frame};Save(r);
    var reason=(BD2.LocalIpc.RuntimeFiles.Read(Path.Combine(live,"pause"))!=null)?"paused":LivePolicy.Gate(c,frame,DateTime.UtcNow.Ticks,false);
    if(reason.Length>0){r.State="rejected";r.Error=reason;Save(r);return;}
    r.MayHaveDispatched=true;r.State="dispatching";Save(r);
    try{
     if(c.Kind=="detach"){Unload();r.State="detached";Save(r);return;}
     var u=uis[c.SurfaceId];
     if(LivePolicy.PowderKind(c.Kind)||c.Kind=="equipment_refine_batch")EquipmentToolsNative.Execute(c,u);
     else if(c.Kind=="equipment_craft_menu")EquipmentMakingSelectUI.Open(false);
     else if(c.Kind=="equipment_refine_preview"){
      var item=ὡὩὩὤὡὯὩὡὧὥὦ.ὥὯὬὦὠὪὭὢὪὯὩ(c.Items[0]);
      if(item==null||item.BaseInfo.Level!=9)throw new InvalidOperationException("Selected +9 equipment changed");
      ὩὭὨὪὨὨὮὣὪὣὥ.ὢὣὠὮὩὭὧὭὦὪὨ(delegate(EquipmentUpgradeUI ui){ui.SetEquipmentUI(item);});
     }
     else if(c.Kind=="back")u.OnClickBackButton();
     else if(c.Kind=="pointer"){
      if(EventSystem.current==null)throw new InvalidOperationException("No active UI event system");
      ExecuteEvents.Execute(targets[c.TargetId],new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);
     }else u.OnClickUI(targets[c.TargetId]);
     r.State="dispatched";Save(r);pending=r;
    }catch(Exception e){r.State="unknown";r.Error=e.GetBaseException().ToString();Save(r);}
   }catch(Exception e){Write(Path.Combine(live,"error.json"),new Frame{ProcessId=pid,ProcessStartTicks=start,Instance=instance,AtUtcTicks=DateTime.UtcNow.Ticks,Error=e.GetBaseException().ToString()});}
   finally{busy=false;}
  }
 }
}

