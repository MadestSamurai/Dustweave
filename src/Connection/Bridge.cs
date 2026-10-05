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
namespace BD2Daily.Live {
 public static class Bridge {
  private static readonly string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2DailyAssistant");
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
  private static FieldObjectBase approachTarget;private static int approachMap;private static long approachUntil;private static bool approachContact;
  private static bool mainlineStealthNav;private static long squareNavUntil;private static string squareNavScene="";
  static void TickApproach(){
   try{TickApproachCore();}catch(Exception ex){approachTarget=null;Status("active","Travel approach failed: "+ex.Message);}
  }
  static void TickApproachCore(){
   if(approachTarget==null)return;
   var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;if(field==null||field.ὮὬὬὮὠὮὪὠὧὩὪ==null){approachTarget=null;return;}var player=field.ὪὨὯὢὫὮὨὩὮὡὬ;
   if(retiring||DateTime.UtcNow.Ticks>approachUntil||player==null||field.ὮὬὬὮὠὮὪὠὧὩὪ.Id!=approachMap){approachTarget=null;if(player!=null)field.SetPlayerMoveState(MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.Stop);return;}
   var move=player.ὭὦὫὤὮὧὦὡὨὤὢ;
   if(move.ὠὭὬὪὫὣὠὡὡὠὫ==MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.DontMove){approachTarget=null;return;}
   var gate=approachTarget as GateSpotData;var collider=gate==null?null:gate.ὣὥὥὮὣὧὢὠὡὨὬ;var body=move.ὬὨὡὫὭὮὠὡὧὦὭ;
   // Loading may place the player inside a trigger before gameplay accepts it.
   // Reconcile only an actual physical overlap, once, using the game's guarded handler.
   UnityEngine.Vector3 normal;float depth;
   if(!approachContact&&collider!=null&&collider.enabled&&body!=null&&body.enabled&&UnityEngine.Physics.ComputePenetration(body,body.transform.position,body.transform.rotation,collider,collider.transform.position,collider.transform.rotation,out normal,out depth)){
    approachContact=true;approachTarget=null;squareNavUntil=0;player.OnTriggerEnter(collider);return;
   }
   var point=collider==null?approachTarget.transform.position:collider.bounds.center;var direction=point-player.transform.position;direction.y=0;
   if(direction.sqrMagnitude<.0001f){approachTarget=null;field.SetPlayerMoveState(MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.Stop);return;}
   move.ChangeMoveType(MoveController.ὧὮὧὠὢὢὦὪὭὢὨ.CharController);player.SetRotation(direction.normalized);player.SetMoveStart();
  }
  // Keep the guard through arrival, result waits and menu transitions too.
  // The daily runner pauses this channel when stopped; handoff retires it.
  internal static bool DailyActionsActive { get { return installed&&CollectionReadAllowed; } }
  internal static Frame CurrentFrame { get { return frame; } }
  internal static bool CollectionReadAllowed { get { return !retiring&&BD2.LocalIpc.RuntimeFiles.Read(Path.Combine(live,"pause"))==null; } }
  private static Frame frame;private static Receipt pending;
  private static readonly Dictionary<int,UIBase> uis=new Dictionary<int,UIBase>();
  private static readonly Dictionary<int,GameObject> targets=new Dictionary<int,GameObject>();
  [DataContract] private sealed class Identity {
   [DataMember] public int ProcessId;[DataMember] public long ProcessStartTicks,FrameUtcTicks;
   [DataMember] public string AccountKey,PlayerKey,State,Scene;
  }
  [DataContract] private sealed class Performance {
   [DataMember]public long AtUtcTicks;[DataMember]public int Count,TargetFps,RenderFrames,SlowFrames;[DataMember]public double MaxFrameMs;
   [DataMember]public double ObserveMs,EvidenceMs,WriteMs,MaxMs,FrameDeltaMs,TimeScale;
   [DataMember]public bool Legacy,Focused;[DataMember]public string[] Reads;
  }
  [DataContract] private sealed class Lease {[DataMember]public long ExpiresUtcTicks;[DataMember]public string Owner;}
  private static Assembly harmonyAssembly;
  public static void Load(){
   if(!resolver){AppDomain.CurrentDomain.AssemblyResolve+=Resolve;resolver=true;}
   LoadHarmony();
   if(handoff==null)handoff=new BD2.LocalIpc.Handoff(typeof(Bridge).Assembly.FullName,"daily-live","daily",false,Start,Pause,HandoffBusy,Stop,Status);
   if(!handoff.IsActive&&!handoff.Pending)BD2.LocalIpc.RuntimeFiles.Start(live,BD2.LocalIpc.Build.Fingerprint,LiveProtocol.LiveEntries);
   handoff.Request(DateTime.UtcNow);if(pump==null)pump=new BD2.LocalIpc.MainThread(()=>{BD2.LocalIpc.LegacyPilots.Discover();handoff.Tick(DateTime.UtcNow);return handoff.Pending;},handoff.Fail);pump.Schedule();
  }
  private static Assembly Resolve(object sender,ResolveEventArgs args){return new AssemblyName(args.Name).Name=="0Harmony"?LoadHarmony():null;}
  private static Assembly LoadHarmony(){if(harmonyAssembly!=null)return harmonyAssembly;using(var s=typeof(Bridge).Assembly.GetManifestResourceStream("BD2Daily.Harmony.dll"))using(var b=new MemoryStream()){if(s==null)throw new InvalidDataException("日常执行组件缺少内置依赖，请更新完整工具包。");s.CopyTo(b);return harmonyAssembly=Assembly.Load(b.ToArray());}}
  [DataContract] sealed class ComponentStatus{[DataMember]public string State,Error;[DataMember]public long AtUtcTicks;}
  static void Status(string state,string error){if(state=="active"&&handoff!=null&&handoff.IsActive)BD2.LocalIpc.RuntimeFiles.Activate();Write(Path.Combine(live,"runtime.json"),new ComponentStatus{State=state,Error=error,AtUtcTicks=DateTime.UtcNow.Ticks});}
  static void Pause(){SquareRoutePilot.Cancel("组件交接");retiring=true;BD2.LocalIpc.RuntimeFiles.Revoke();DispatchRecoveryNative.Cancel();MiniGamesNative.StopDice();WeeklyNpcNative.Stop();
   if(squareNavUntil>0){Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ClearQuestNav(true);GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.SetPlayerMoveState(MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.Stop);squareNavUntil=0;mainlineStealthNav=false;approachTarget=null;}}
  static string HandoffBusy(){if(busy||pending!=null)return "command observation";if(Evidence.Waiting)return "daily request awaiting response";if(EquipmentToolsNative.Busy())return "native refinement";return "";}
  public static void Unload(){BD2.LocalIpc.MainThread.Drain(()=>{if(handoff!=null)handoff.Unload();},Status);}
  [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
  private static void Start(){lock(typeof(Bridge)){
   if(installed)return;retiring=false;BD2.LocalIpc.RuntimeFiles.Start(live,BD2.LocalIpc.Build.Fingerprint,LiveProtocol.LiveEntries);if(AppDomain.CurrentDomain.GetData("BD2.LocalIpc.SuiteModules.v1")!=null)BD2.LocalIpc.RuntimeFiles.Write(Path.Combine(live,"pause"),new byte[0]);
   Directory.CreateDirectory(live);Directory.CreateDirectory(Path.Combine(live,"receipts"));Directory.CreateDirectory(Path.Combine(live,"claimed"));
   using(var p=Process.GetCurrentProcess()){pid=p.Id;start=p.StartTime.ToUniversalTime().Ticks;}
   // This passive extension coexists with the existing identity / guild evidence observer.
   // No autonomous action, no replacement of active game hooks, no game restart.
   UnityEngine.Canvas.willRenderCanvases+=Tick;installed=true;Evidence.Start(live);TalentSafetyNative.Start();
   Write(Path.Combine(live,"attached.json"),new Frame{ProcessId=pid,ProcessStartTicks=start,Instance=instance,AtUtcTicks=DateTime.UtcNow.Ticks});
  }}
  private static void Stop(){SquareRoutePilot.Cancel("组件卸载");if(resolver){AppDomain.CurrentDomain.AssemblyResolve-=Resolve;resolver=false;}DispatchRecoveryNative.Cancel();MiniGamesNative.StopDice();WeeklyNpcNative.Stop();TalentSafetyNative.Stop();Evidence.Stop();UnityEngine.Canvas.willRenderCanvases-=Tick;installed=false;uis.Clear();targets.Clear();}
  private static T Read<T>(string file)where T:class{
   try{if(BD2.LocalIpc.RuntimeFiles.Handles(file)){var bytes=BD2.LocalIpc.RuntimeFiles.Read(file);if(bytes==null)return null;using(var memory=new MemoryStream(bytes))return(T)new DataContractJsonSerializer(typeof(T)).ReadObject(memory);}if(Path.GetDirectoryName(file)==root){var bytes=BD2.LocalIpc.PipeBroker.ReadCurrent(root,Path.GetFileName(file));if(bytes==null)return null;using(var memory=new MemoryStream(bytes))return(T)new DataContractJsonSerializer(typeof(T)).ReadObject(memory);}using(var f=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))return(T)new DataContractJsonSerializer(typeof(T)).ReadObject(f);}
   catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}
  }
  private static void Write(string file,object data,bool durable=true){using(var memory=new MemoryStream()){new DataContractJsonSerializer(data.GetType()).WriteObject(memory,data);if(BD2.LocalIpc.RuntimeFiles.Write(file,memory.ToArray()))return;}
   var tmp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{using(var f=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){new DataContractJsonSerializer(data.GetType()).WriteObject(f,data);f.Flush(durable);}
    if(File.Exists(file))File.Replace(tmp,file,null);else File.Move(tmp,file);
   }finally{if(File.Exists(tmp))File.Delete(tmp);}
  }
  private static IEnumerable<FieldInfo> Fields(Type t){for(;t!=null&&t!=typeof(MonoBehaviour);t=t.BaseType)foreach(var f in t.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly))yield return f;}
  internal static string PathOf(Transform t){var p=new List<string>();for(;t!=null;t=t.parent)p.Add(t.name);p.Reverse();return string.Join("/",p.ToArray());}
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
  internal static bool InputReady(UIBase u){
   // Event result uses its own click gate; EXP presentation deliberately clears
   // UIBase's general flag and does not restore it. Match the native handler.
   if(u is BattleResultUI && BattlePlayManager.ὪὫὢὨὯὭὦὪὦὨὣ.GetBattleMode()==Proto.Net.Define_BattleModeType.PackEventBattle || u is BattleResultUI && BattlePlayManager.ὪὫὢὨὯὭὦὪὦὨὣ.GetBattleMode()==Proto.Net.Define_BattleModeType.TacticsBingo){
    return u.ὪὯὣὥὬὫὬὩὠὮὠ && !(bool)Fields(u.GetType()).Single(x=>x.Name=="ὮὩὪὪὦὤὯὯὧὨὪ").GetValue(u);
   }

   if(!u.ὣὤὥὦὯὦὩὤὨὠὪ||!u.ὪὯὣὥὬὫὬὩὠὮὠ||(u.ὫὥὧὬὭὫὡὠὠὪὠ&&u.ὦὡὮὫὧὠὮὡὭὭὬ))return false;
   if(!CollectionProgressNative.StealTalkInputReady(u))return false;
   if(u is EventMainUI && !(bool)Fields(u.GetType()).Single(x=>x.Name=="ὬὯὤὣὮὧὧὩὦὪὫ").GetValue(u))return false;
   if(u is HuntDispatchPopupUI){var field=Fields(u.GetType()).First(x=>x.Name=="ὣὭὠὤὧὠὡὮὣὢὦ");if((bool)field.GetValue(u))return false;}
   return true;
  }
  private static Frame Observe(){
   var f=new Frame{ProcessId=pid,ProcessStartTicks=start,Instance=instance,AtUtcTicks=DateTime.UtcNow.Ticks,Sequence=++sequence,PluginAvailable=PluginHost.Available,PluginFingerprint=BD2.LocalIpc.Build.PluginFingerprint,Scene=SceneManager.GetActiveScene().name??""};
   var identity=Read<Identity>(Path.Combine(root,"snapshot.json"));
   if(identity!=null&&identity.ProcessId==pid&&identity.ProcessStartTicks==start&&identity.State=="identified"&&identity.Scene==f.Scene&&identity.FrameUtcTicks>DateTime.UtcNow.AddSeconds(-3).Ticks){f.AccountKey=identity.AccountKey;f.PlayerKey=identity.PlayerKey;}
   else f.Error="Waiting for fresh account identity";
   sceneComponents=null;uis.Clear();targets.Clear();var surfaces=new List<Surface>();
   foreach(var u in UnityEngine.Object.FindObjectsOfType<UIBase>().Where(u=>u!=null&&u.gameObject.activeInHierarchy)){
    uis[u.GetInstanceID()]=u;
    var canvas=u.GetComponent<Canvas>();var popup=Fields(u.GetType()).FirstOrDefault(x=>x.Name=="_isPopupUI");
    var sf=new Surface{Id=u.GetInstanceID(),Type=u.GetType().Name,Path=PathOf(u.transform),Popup=popup!=null&&(bool)popup.GetValue(u),Order=canvas==null?0:canvas.sortingOrder,InputReady=InputReady(u)};
    var choices=new List<Target>();
    foreach(var link in Links(u)){
     var go=link.Value;if(!go.activeInHierarchy)continue;var button=go.GetComponent<Selectable>();
     // Only click-like serialized references become targets; never arbitrary child objects.
     if(button==null&&link.Key.IndexOf("button",StringComparison.OrdinalIgnoreCase)<0&&!(u is BattlePauseUI&&link.Key=="_objectRun"))continue;
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
    sf.NoticeSuppression=NoticeNative.Observe(u);sf.NativeContext=StoryNative.PopupContext(u);if(u is MessagePopupUI)sf.NativeContext=TalentSafetyNative.PopupContext(u);if(u is QuestBoardUI||u is QuestPopupUI)sf.NativeContext=WeeklyNpcNative.BoardContext(u);
    surfaces.Add(sf);
   }
   surfaces.AddRange(StoryNative.Observe());surfaces.AddRange(StoryNative.ObserveScripts());
   f.Surfaces=surfaces.OrderBy(s=>s.Id).ToArray();f.SquareNavigation=SquareRoutePilot.Observe();
   var key=f.Scene+"|"+string.Join("|",f.Surfaces.Select(s=>s.Id+":"+s.Order+":"+s.NoticeSuppression+":"+s.NativeContext+":"+string.Join(",",s.Targets.Select(t=>t.Id+":"+t.Enabled))).ToArray());
   using(var sha=SHA256.Create())f.UiToken=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-","");
   return f;
  }
  private static void Save(Receipt r){r.AtUtcTicks=DateTime.UtcNow.Ticks;Write(Path.Combine(live,"receipts",r.Command.Id+".json"),r);}
  private static bool OtherOwner(){
   var lease=Read<Lease>(Path.Combine(root,"lease.json"));if(lease!=null&&!string.IsNullOrEmpty(lease.Owner)&&lease.ExpiresUtcTicks>DateTime.UtcNow.Ticks)return true;
   var permit=Read<Lease>(Path.Combine(root,"startup-permit.json"));if(permit!=null&&!string.IsNullOrEmpty(permit.Owner)&&permit.ExpiresUtcTicks>DateTime.UtcNow.Ticks)return true;
   return (BD2.LocalIpc.PipeBroker.ReadCurrent(root,"guild-command.json")!=null);
  }
  private static void Tick(){
   if(!installed)return;
   if(UnityEngine.Time.frameCount!=lastFrame){lastFrame=UnityEngine.Time.frameCount;renderFrames++;var ms=UnityEngine.Time.unscaledDeltaTime*1000;maxFrameMs=Math.Max(maxFrameMs,ms);if(ms>50)slowFrames++;}
   TickApproach();SquareRoutePilot.Tick(frame,CollectionReadAllowed);
   if(busy||DateTime.UtcNow.Ticks<next)return;busy=true;next=DateTime.UtcNow.AddMilliseconds((BD2.LocalIpc.RuntimeFiles.Read(Path.Combine(live,"pause"))!=null)&&!LegacyObservation?1000:500).Ticks;
   try{
    LegacyObservation=(BD2.LocalIpc.RuntimeFiles.Read(Path.Combine(live,"legacy-observation"))!=null);var timer=Stopwatch.StartNew();frame=Observe();MiniGamesNative.GuardDice(frame,live);WeeklyNpcNative.Tick();
    if(squareNavUntil>0&&((BD2.LocalIpc.RuntimeFiles.Read(Path.Combine(live,"pause"))!=null)||DateTime.UtcNow.Ticks>squareNavUntil||frame.Scene!=squareNavScene||(mainlineStealthNav&&!TalentSkillManager.ὪὫὢὨὯὭὦὪὦὨὣ.IsTalentSkillDurationing(ὪὯὯὢὨὮὨὠὥὤὬ.Stealth)))){
     Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ClearQuestNav(true);GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.SetPlayerMoveState(MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.Stop);squareNavUntil=0;mainlineStealthNav=false;approachTarget=null;
    }var observed=timer.Elapsed.TotalMilliseconds;
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
    var reason=(BD2.LocalIpc.RuntimeFiles.Read(Path.Combine(live,"pause"))!=null)?"paused":LivePolicy.Gate(c,frame,DateTime.UtcNow.Ticks,OtherOwner());
    if(reason.Length>0){r.State="rejected";r.Error=reason;Save(r);return;}
    if(c.Kind=="mainline_talent"){
     try{var row=Find(typeof(QuickMenuTalentSkillLoopScrollItem)).Cast<QuickMenuTalentSkillLoopScrollItem>().SingleOrDefault(x=>x.GetInstanceID()==c.Value);
      reason=CollectionProgressNative.TalentGate(row,uis[c.SurfaceId] as QuickMenuUI);
     }catch(Exception ex){reason="talent_reject:unreadable_gate:"+ex.GetType().Name;}
     // This is before the dispatch marker and before OnClick's quest/latch side effects.
     if(reason.Length>0){r.State="rejected";r.Error=reason;r.MayHaveDispatched=false;Save(r);return;}
    }
    if(LivePolicy.WeeklyNpcKind(c.Kind)){
     try{reason=WeeklyNpcNative.Preflight(c);}catch(Exception ex){reason="npc_preflight_unavailable:"+ex.GetType().Name;}
     if(reason.Length>0){r.State="rejected";r.Error=reason;r.MayHaveDispatched=false;Save(r);return;}
    }
    r.MayHaveDispatched=true;r.State="dispatching";Save(r);
    try{
     if(c.Kind=="detach"){Unload();r.State="detached";Save(r);return;}
     var u=(c.Kind=="story_skip"||c.Kind=="story_advance")?null:uis[c.SurfaceId];
     if(c.Kind=="story_advance")StoryNative.Advance(c);
     else if(c.Kind=="story_skip")StoryNative.Skip(c);
     else if(c.Kind=="talent_error_ack")TalentSafetyNative.Acknowledge(u);
     else if(c.Kind=="notice_suppress")NoticeNative.Select(u);
     else if(LivePolicy.PowderKind(c.Kind)||c.Kind=="equipment_refine_batch")EquipmentToolsNative.Execute(c,u);
     else if(LivePolicy.ExtensionKind(c.Kind))PluginHost.Execute(c,u);
     else if(LivePolicy.EventKind(c.Kind))EventSweepActions.Execute(c,u);
     else if(LivePolicy.TradeKind(c.Kind))TradeActions.Execute(c,u);
     else if(LivePolicy.WeeklyNpcKind(c.Kind))WeeklyNpcNative.Dispatch(c,u);
     else if(c.Kind=="collection_query"||c.Kind=="collection_query_steal")CollectionProgressNative.Query(c);
     else if(c.Kind=="friendship_complete")FriendshipNative.Complete(c,u);
     else if(c.Kind=="friendship_select")FriendshipNative.Select(c);
     else if(c.Kind=="weekly_book_lobby"){
      var gate=Find(typeof(TotalWarLobbyGateController)).Cast<TotalWarLobbyGateController>().Single();
      int quest=(int)typeof(TotalWarLobbyGateController).GetField("_reqQuestId",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(gate);
      if(quest!=0&&!ὮὢὦὯὧὣὦὡὧὯὢ.ὥὫὣὧὩὧὦὮὤὧὠ(quest))throw new InvalidOperationException("Total War is not unlocked");
      ὩὭὨὪὨὨὮὣὪὣὥ.ὢὣὠὮὩὭὧὭὦὪὨ<TotalWarUI>(delegate { GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.SetPlayerMoveState(MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.DontMove); });
     }
     else if(c.Kind=="weekly_book_open"){
      var mission=ὮὦὮὣὥὢὠὫὭὫὭ.ὢὦὩὩὢὪὫὯὫὯὩ(218);
      if(mission.ConditionType!=280||mission.ShortCutId<=0)throw new InvalidOperationException("Weekly Total War mission changed");
      var shortcut=ὮὣὨὦὥὫὮὮὣὧὩ.ὤὭὦὬὭὫὫὤὡὬὪ(mission.ShortCutId);
      ὩὭὨὪὨὨὮὣὪὣὥ.ὧὠὡὮὯὦὭὧὬὭὬ(shortcut.UiName,u,true,ὮὣὨὦὥὫὮὮὣὧὩ.ὭὩὩὩὬὣὮὬὫὣὧ(shortcut));
     }
     else if(c.Kind=="monster_open"){
      // Same native lobby as the current season event entry. No battle here.
      ὩὭὨὪὨὨὮὣὪὣὥ.ὢὣὠὮὩὭὧὭὦὪὨ(delegate(MonsterHuntUI ui){ui.RefreshUI(c.Value==1);});
     }
     else if(c.Kind=="mirror_ready"){
      // Same navigation as PVPGateController, no battle request or currency mutation.
      var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;
      var points=field.ὩὣὦὡὡὯὠὮὦὭὯ;
      if(points==null||points.Count!=1)throw new InvalidOperationException("Mirror gate encounter is ambiguous");
      var point=points[0];
      Singleton<FieldEncountManager>.ὪὫὢὨὯὭὦὪὦὨὣ.SetPVPEncountData(point);
      field.EnterPVPMap(ὮὧὥὮὣὭὧὬὦὢὪ.BMT_PVP_READY,point);
     }
     else if(c.Kind=="dispatch_recover")DispatchRecoveryNative.Execute(c);
     else if(c.Kind=="dispatch_collect_all"){
      if(BattlePlayManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὨὢὤὪὪὢὩὢὬὠὦ.ToString()!="BMT_NONE")throw new InvalidOperationException("Dispatch collection requires field idle");
      if(Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ.waitResetPackKeyWordSet.Contains("DispatchAutoSupport")||ὣὡὧὡὦὣὣὬὨὪὫ.ὡὩὧὩὡὤὤὭὠὫὣ)throw new InvalidOperationException("Native dispatch chain is active");
      var target=Find(typeof(FieldDispatchObjectController)).Concat(Find(typeof(FieldTotalWarRewardObjectController))).Cast<FieldObjectBase>().Single(x=>x.GetInstanceID()==c.Value);
      if(!target.gameObject.activeInHierarchy||!target.ὩὤὨὮὥὦὫὭὫὭὨ||(bool)Fields(target.GetType()).First(x=>x.Name=="ὦὮὠὩὣὣὮὤὩὥὫ").GetValue(target))throw new InvalidOperationException("Reward messenger is no longer interactable");
      // This is the actual field click. It collects all dispatch, Total War and
      // Evil Castle rewards and lets the native callback perform redispatch.
      target.InteractionFieldObject();
     }
     else if(c.Kind=="mainline_pack"){
      var pack=ὤὫὬὭὯὪὢὬὬὩὣ.ὬὣὢὥὫὪὬὮὣὠὦ(c.Value);
      if(pack==null||!(CollectionProgressNative.IsStoryField(c.Value)||(pack.PackType==6&&new[]{2001,2002,2005,2007}.Contains(c.Value)))||ὣὡὧὡὦὣὣὬὨὪὫ.ὬὣὢὥὫὪὬὮὣὠὦ(c.Value)==null)throw new InvalidOperationException("Owned story cartridge required");
      PackListLoopScrollItem.StartPackSelection(pack);
     }
     else if(c.Kind=="weekly_steal_talk")CollectionProgressNative.StealTalk((BalloonScriptUI)u,c);
     else if(c.Kind=="weekly_steal_confirm")CollectionProgressNative.StealConfirm((StealInfoPopupUI)u,c);
     else if(c.Kind=="weekly_steal_menu"){
      var npc=Find(typeof(NPCController)).Cast<NPCController>().Single(x=>x.GetInstanceID()==c.Value);
      if(!CollectionProgressNative.IsStealNpc(npc)||!npc.ὩὤὨὮὥὦὫὭὫὭὨ)throw new InvalidOperationException("Weekly NPC is not in interaction range");
      Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ClearQuestNav(true);squareNavUntil=0;
      npc.InteractionFieldObject();
     }
     else if(c.Kind=="mainline_talent"||c.Kind=="weekly_steal_talent"){
      var target=Find(typeof(QuickMenuTalentSkillLoopScrollItem)).Cast<QuickMenuTalentSkillLoopScrollItem>().Single(x=>x.GetInstanceID()==c.Value);
      if(target.GetComponentInParent<QuickMenuUI>()!=(QuickMenuUI)u)throw new InvalidOperationException("Talent row belongs to another menu");
      var fs=Fields(target.GetType()).ToArray();
      var table=(Proto.Design.common.TalentSkillTable)fs.Single(x=>x.Name=="ὣὡὪὭὤὨὭὣὪὧὩ").GetValue(target);
      if(c.Kind=="weekly_steal_talent"){
       int npc=(int)fs.Single(x=>x.Name=="ὣὧὢὨὭὦὠὬὪὢὭ").GetValue(target);
       if(table.ClassType!=1||table.ResetType!=2||npc!=c.Items[0]||table.GroupId!=c.Items[1])throw new InvalidOperationException("Weekly steal talent does not match NPC");
      }
      else if(!new[]{2,3,4,6,15,17,20}.Contains(table.ClassType))throw new InvalidOperationException("Only mainline gathering talents allowed");
      foreach(var name in new[]{"_objDisableImage","_goBlock"})if(((GameObject)fs.Single(x=>x.Name==name).GetValue(target)).activeSelf)throw new InvalidOperationException("Native talent row disabled");
      target.OnClick();
     }
     else if(c.Kind=="mainline_interact"){
      var target=Find(typeof(WayPointController)).Cast<WayPointController>().Single(x=>x.GetInstanceID()==c.Value);
      if(!target.ὩὤὨὮὥὦὫὭὫὭὨ)throw new InvalidOperationException("Waypoint not in interaction range");
      target.InteractionFieldObject();
     }
     else if(c.Kind=="mainline_cancel_nav"){
      WeeklyNpcNative.Stop();
      Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ClearQuestNav(true);GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.SetPlayerMoveState(MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.Stop);squareNavUntil=0;mainlineStealthNav=false;approachTarget=null;
     }
     else if(c.Kind=="mainline_approach"){
      var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;var target=Find(typeof(FieldObjectBase)).Cast<FieldObjectBase>().Single(x=>x.GetInstanceID()==c.Value);
      if(BattlePlayManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὨὢὤὪὪὢὩὢὬὠὦ.ToString()!="BMT_NONE"||!(target is GateSpotData||target is WayPointController))throw new InvalidOperationException("Field entrance approach requires idle field");
      var player=field.ὪὨὯὢὫὮὨὩὮὡὬ;var move=player.ὭὦὫὤὮὧὦὡὨὤὢ;var agent=move.ὢὮὡὭὢὥὠὧὧὯὬ;
      var gate=target as GateSpotData;var point=gate!=null&&gate.ὣὥὥὮὣὧὢὠὡὨὬ!=null?gate.ὣὥὥὮὣὧὢὠὡὨὬ.bounds.center:target.transform.position;
      if(agent==null||UnityEngine.Vector3.Distance(point,player.transform.position)>agent.height*2)throw new InvalidOperationException("travel_unreachable:not_near_entrance");
      if(gate!=null){var map=ὩὥὫὮὯὥὮὪὨὡὩ.ὧὥὮὪὣὬὦὡὫὧὢ(gate.ὤὦὨὨὭὮὯὡὧὤὯ);string error="";if(map==null||map.PackId!=field.ὮὬὬὮὠὮὪὠὧὩὪ.PackId||!gate.IsPossibleJoinGate(ref error))throw new InvalidOperationException("travel_unreachable:gate_not_available");}
      mainlineStealthNav=LiveProtocol.TransitRequiresStealth(field.ὮὬὬὮὠὮὪὠὧὩὪ.PackId,c.RequireStealth);
      if(mainlineStealthNav&&!TalentSkillManager.ὪὫὢὨὯὭὦὪὦὨὣ.IsTalentSkillDurationing(ὪὯὯὢὨὮὨὠὥὤὬ.Stealth))throw new InvalidOperationException("Protected transit requires active stealth");
      var direction=point-player.transform.position;direction.y=0;
      if(direction.sqrMagnitude<.0001f)throw new InvalidOperationException("travel_unreachable:entrance_contact_not_triggered");
      Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ClearQuestNav(true);move.ChangeMoveType(MoveController.ὧὮὧὠὢὢὦὪὭὢὨ.CharController);player.SetRotation(direction.normalized);player.SetMoveStart();
      approachTarget=target;approachMap=field.ὮὬὬὮὠὮὪὠὧὩὪ.Id;approachContact=false;approachUntil=DateTime.UtcNow.AddSeconds(2).Ticks;squareNavScene=frame.Scene;squareNavUntil=approachUntil;
     }
     else if(c.Kind=="mainline_reposition"){
      var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;var travel=CollectionProgressNative.Travel(frame);
      mainlineStealthNav=LiveProtocol.TransitRequiresStealth(field.ὮὬὬὮὠὮὪὠὧὩὪ.PackId,c.RequireStealth);
      if(mainlineStealthNav&&!TalentSkillManager.ὪὫὢὨὯὭὦὪὦὨὣ.IsTalentSkillDurationing(ὪὯὯὢὨὮὨὠὥὤὬ.Stealth))throw new InvalidOperationException("Protected transit requires active stealth");
      if(BattlePlayManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὨὢὤὪὪὢὩὢὬὠὦ.ToString()!="BMT_NONE"||!travel.Maps.Any(x=>x.Id==travel.Map&&x.CanSummon))throw new InvalidOperationException("Current map does not allow waypoint creation");
      var player=field.ὪὨὯὢὫὮὨὩὮὡὬ;var move=player.ὭὦὫὤὮὧὦὡὨὤὢ;var agent=move.ὢὮὡὭὢὥὠὧὧὯὬ;
      if(agent==null){move.CanNavReach(player.transform.position);agent=move.ὢὮὡὭὢὥὠὧὧὯὬ;}
      if(agent==null)throw new InvalidOperationException("travel_unreachable:navigation_initialization_failed");
      var origin=player.transform.position;var offset=player.ὨὩὨὫὢὯὤὡὬὪὦ-origin;UnityEngine.Vector3? goal=null;
      int mask=(1<<field.ὯὠὫὯὫὥὬὭὠὢὩ)+(1<<field.ὠὧὮὤὥὣὬὧὠὫὨ);
      foreach(float radius in new[]{1f,2f,4f,8f}){
       for(int i=0;i<16;i++){
        float a=i*UnityEngine.Mathf.PI/8;var point=origin+new UnityEngine.Vector3(UnityEngine.Mathf.Cos(a)*radius,0,UnityEngine.Mathf.Sin(a)*radius);UnityEngine.AI.NavMeshHit hit;
        if(!UnityEngine.AI.NavMesh.SamplePosition(point,out hit,.5f,agent.areaMask)||Math.Abs(hit.position.y-origin.y)>agent.height||!move.CanNavReach(hit.position))continue;
        if(UnityEngine.Physics.OverlapSphere(hit.position+offset,.4f,mask).Any(x=>x!=null&&x.gameObject.GetComponent<WayPointController>()!=null))continue;
        goal=hit.position;break;
       }
       if(goal.HasValue)break;
      }
      if(!goal.HasValue)throw new InvalidOperationException("travel_unreachable:no_legal_waypoint_ground");
      Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ClearQuestNav(true);move.ChangeMoveType(MoveController.ὧὮὧὠὢὢὦὪὭὢὨ.Navigation);player.SetMoveStart();
      if(!move.SetMoveNav(goal.Value,null,true)){field.SetPlayerMoveState(MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.Stop);throw new InvalidOperationException("Waypoint approach did not start");}
      squareNavScene=frame.Scene;squareNavUntil=DateTime.UtcNow.AddSeconds(20).Ticks;
     }
     else if(c.Kind=="sichuan_open"||c.Kind=="sichuan_select"){MiniGamesNative.DispatchSichuan(c,u);}
     else if(c.Kind=="route_probe"){
      float probeLength=CollectionProgressNative.StartProbe(frame,c);mainlineStealthNav=TalentSkillManager.ὪὫὢὨὯὭὦὪὦὨὣ.IsTalentSkillDurationing(ὪὯὯὢὨὮὨὠὥὤὬ.Stealth);approachTarget=null;squareNavScene=frame.Scene;squareNavUntil=DateTime.UtcNow.AddSeconds(LiveProtocol.NavigationSeconds(probeLength)).Ticks;
     }
     else if(c.Kind=="mainline_walk"){
      var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;
      if(BattlePlayManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὨὢὤὪὪὢὩὢὬὠὦ.ToString()!="BMT_NONE"||!(CollectionProgressNative.IsStoryField(field.ὮὬὬὮὠὮὪὠὧὩὪ.PackId)||new[]{2001,2002,2005,2007}.Contains(field.ὮὬὬὮὠὮὪὠὧὩὪ.PackId)))throw new InvalidOperationException("Mainline field required");
      var target=Find(typeof(FieldObjectBase)).Cast<FieldObjectBase>().Single(x=>x.GetInstanceID()==c.Value);
      if(!(target is GateSpotData||target is FieldRewardObjectController||target is WayPointController||CollectionProgressNative.IsStealNpc(target as NPCController)))throw new InvalidOperationException("Gathering target required");
      var gate=target as GateSpotData;if(gate!=null){var map=ὩὥὫὮὯὥὮὪὨὡὩ.ὧὥὮὪὣὬὦὡὫὧὢ(gate.ὤὦὨὨὭὮὯὡὧὤὯ);if(map==null||map.PackId!=field.ὮὬὬὮὠὮὪὠὧὩὪ.PackId)throw new InvalidOperationException("Gate leaves current chapter");}
      mainlineStealthNav=LiveProtocol.TransitRequiresStealth(field.ὮὬὬὮὠὮὪὠὧὩὪ.PackId,c.RequireStealth);
      if(mainlineStealthNav&&!TalentSkillManager.ὪὫὢὨὯὭὦὪὦὨὣ.IsTalentSkillDurationing(ὪὯὯὢὨὮὨὠὥὤὬ.Stealth))throw new InvalidOperationException("Protected transit requires active stealth");
      var player=field.ὪὨὯὢὫὮὨὩὮὡὬ;var move=player.ὭὦὫὤὮὧὦὡὨὤὢ;
      UnityEngine.Vector3 destination;float length;string pathReason;
      if(!CollectionProgressNative.TryApproach(target,out destination,out length,out pathReason))throw new InvalidOperationException("travel_unreachable:"+pathReason);
      if(!move.CanNavReach(destination))throw new InvalidOperationException("travel_unreachable:native_check");
      Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ.ClearQuestNav(true);move.ChangeMoveType(MoveController.ὧὮὧὠὢὢὦὪὭὢὨ.Navigation);player.SetMoveStart();
      if(!move.SetMoveNav(destination,null,true)){field.SetPlayerMoveState(MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.Stop);throw new InvalidOperationException("Native navigation did not start");}
      squareNavScene=frame.Scene;squareNavUntil=DateTime.UtcNow.AddSeconds(LiveProtocol.NavigationSeconds(length)).Ticks;
     }
     else if(c.Kind=="mainline_menu"){
      if(BattlePlayManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὨὢὤὪὪὢὩὢὬὠὦ.ToString()!="BMT_NONE")throw new InvalidOperationException("Field idle required");
      ὩὭὨὪὨὨὮὣὪὣὥ.ὢὣὠὮὩὭὧὭὦὪὨ(delegate(QuickMenuUI menu){menu.SetSpecificTalentSkill((ὪὯὯὢὨὮὨὠὥὤὬ)c.Value);menu.SetMenuByPlayer();});
     }
     else if(c.Kind=="dispatch_menu"){
      // Native talent navigation only; claiming and currency use remain ordinary UI actions.
      if(BattlePlayManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὨὢὤὪὪὢὩὢὬὠὦ.ToString()!="BMT_NONE")throw new InvalidOperationException("Dispatch navigation requires field idle");
      ὩὭὨὪὨὨὮὣὪὣὥ.ὢὣὠὮὩὭὧὭὦὪὨ(delegate(QuickMenuUI menu){
       menu.SetSpecificTalentSkill(ὪὯὯὢὨὮὨὠὥὤὬ.Dispatch);
       menu.SetMenuByPlayer();
      });
     }
     else if(LivePolicy.QuizKind(c.Kind)){MiniGamesNative.Dispatch(c,u);}
     else if(LivePolicy.RewardKind(c.Kind)){RewardNative.Dispatch(c,u);}
     else if(c.Kind=="reward_refresh"){
      // Query server-owned progress before resuming a reset/rejected claim.
      ὢὮὥὡὩὥὧὨὥὢὢ.ὣὮὢὩὤὯὬὯὩὪὣ();
      ὨὩὤὣὡὣὦὭὭὤὪ.ὩὠὠὠὢὠὣὫὬὫὥ();
     }
     else if(c.Kind=="dice_query"){
      var state=RewardNative.Capture();
      if(state.Kind!="MiniGameDiceUI"||!state.Ready||state.Auto)throw new InvalidOperationException("Dice must be idle for server query");
      var schedule=Proto.Net.EventScheduleDBInfo.Parser.ParseJson(state.Schedule);
      ὤὦὣὡὠὤὪὩὦὤὤ.ὨὬὥὭὫὠὨὨὦὭὡ(new[]{schedule.Id},null);
     }
     else if(c.Kind=="pass_select"){
      var pass=(PassUI)u;
      if(!pass.ὠὤὭὯὯὪὯὧὮὪὬ.Any(x=>x.ὥὬὭὪὪὦὬὩὦὧὠ==c.Value))throw new InvalidOperationException("Requested pass is not active for this account");
      pass.Init(PassUI.ὠὥὧὪὫὩὦὫὣὪὠ.EPassBanner,c.Value,PassUIPrefabBase.ὨὧὬὫὥὯὠὢὨὤὮ.Mission);
     }
     else if(c.Kind=="pass_init"){
      var pass=(PassUI)u;
      if(pass.ὠὤὭὯὯὪὯὧὮὪὬ.Count!=0||pass.ὦὩὪὣὣὤὣὯὩὠὢ!=0)throw new InvalidOperationException("Pass already initialized");
      pass.Init(PassUI.ὠὥὧὪὫὩὦὫὣὪὠ.EMenuUI,0);
     }
     else if(LivePolicy.PreviewKind(c.Kind)){
      if(c.Kind.StartsWith("square_")){
       if(!Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ.IsSquarePack())throw new InvalidOperationException("Not in square");
       var nav=Singleton<QuestNavigationManager>.ὪὫὢὨὯὭὦὪὦὨὣ;
       var field=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ;var actor=field.ὪὨὯὢὫὮὨὩὮὡὬ;
       if(c.Kind=="square_cancel_nav"){SquareRoutePilot.Cancel("桌面请求停止移动");nav.ClearQuestNav(true);field.SetPlayerMoveState(MoveController.ὯὣὠὮὤὡὤὯὢὩὯ.Stop);squareNavUntil=0;}
       else if(c.Kind=="square_route_probe"){
        var goal=new Vector3(c.Items[0]/1000f,c.Items[1]/1000f,c.Items[2]/1000f);
        if(Vector3.Distance(goal,actor.transform.position)>180)throw new InvalidOperationException("广场移动测试目标超出范围");
        SquareRoutePilot.Begin(c,null,null,goal);
       }
       else if(c.Kind=="square_shop_nav"||c.Kind=="square_shop_interact"){
        if(BattlePlayManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὨὢὤὪὪὢὩὢὬὠὦ.ToString()!="BMT_NONE")throw new InvalidOperationException("Merchant requires idle field");
        var merchant=TradeObservation.Merchant();
        if(c.Kind=="square_shop_interact"){
         if(!merchant.ὩὤὨὮὥὦὫὭὫὭὨ)throw new InvalidOperationException("Merchant not in interaction range");
         SquareRoutePilot.Cancel("商人交互");merchant.InteractionFieldObject();
        }else SquareRoutePilot.Begin(c,merchant,null,merchant.ὩὬὧὢὧὬὠὡὦὥὧ);
       }
       else if(c.Kind=="square_goddess_interact"){
        var statue=Find(typeof(Field.SquareStatueObject)).Cast<Field.SquareStatueObject>().Single();
        if(!statue.ὩὤὨὮὥὦὫὭὫὭὨ)throw new InvalidOperationException("Goddess not in interaction range");
        SquareRoutePilot.Cancel("女神像交互");statue.InteractionFieldObject();
       }else if(c.Kind=="square_goddess_nav"){
        if(!ὮὨὬὣὪὩὨὫὨὦὫ.ὮὫὪὢὥὨὢὠὡὪὥ)throw new InvalidOperationException("Goddess reward already received");
        var statue=Find(typeof(Field.SquareStatueObject)).Cast<Field.SquareStatueObject>().Single();
        SquareRoutePilot.Begin(c,statue,null,statue.ὩὬὧὢὧὬὠὡὦὥὧ);
       }else{
        var target=Find(typeof(FieldStatueObjectController)).Concat(Find(typeof(FieldGuildRaidStatueObjectController))).Cast<FieldObjectBase>().Single(x=>x.ὪὬὣὧὥὨὦὯὨὮὬ==c.Value);
        if(ὢὯὯὣὧὦὯὠὢὪὬ.ὭὬὢὤὬὦὬὤὢὡὯ(c.Value))throw new InvalidOperationException("Ranking reward already received");
        var rewardField=target is FieldGuildRaidStatueObjectController?"_rewardCollider":"_statueRewardCollideDector";
        var reward=(Component)Fields(target.GetType()).Single(x=>x.Name==rewardField).GetValue(target);
        var collider=reward==null?null:reward.GetComponent<Collider>();
        if(collider==null||!collider.enabled||!collider.gameObject.activeInHierarchy)throw new InvalidOperationException("Ranking reward collider unavailable");
        // Walk through the actual reward trigger; never consume its edge getter.
        var goal=collider.bounds.center;
        SquareRoutePilot.Begin(c,target,collider,goal);
       }
      }else if(c.Kind=="mail_tab"){
       var tabs=(Array)Fields(u.GetType()).Single(x=>x.Name=="_mailTabs").GetValue(u);
       var tab=tabs.GetValue(c.Value);var root=(GameObject)tab.GetType().GetField("root").GetValue(tab);
       u.OnClickUI(root);
      }else if(c.Kind=="equipment_craft_menu")EquipmentMakingSelectUI.Open(false);
      else if(c.Kind=="equipment_craft_preview"){
       var menu=(EquipmentMakingSelectUI)u;
       var recipe=menu.EquipmentMakingDatas.Single(x=>x.ὫὮὩὬὤὫὮὨὢὯὬ.Id==1);
       menu.OnEquipmentMakingUI(recipe);
      }else{
       var items=c.Items.Select(id=>ὡὩὩὤὡὯὩὡὧὥὦ.ὥὯὬὦὠὪὭὢὪὯὩ(id)).ToList();
       if(items.Any(x=>x==null))throw new InvalidOperationException("Equipment no longer exists");
       if(c.Kind=="equipment_refine_preview"){
        if(items[0].BaseInfo.Level!=9)throw new InvalidOperationException("Refinement requires +9");
        ὩὭὨὪὨὨὮὣὪὣὥ.ὢὣὠὮὩὭὧὭὦὪὨ(delegate(EquipmentUpgradeUI ui){ui.SetEquipmentUI(items[0]);});
       }else{
        if(items.Any(x=>x.LockFlag!=0||x.KeepFlag!=0||x.UseChar!=0))throw new InvalidOperationException("Protected equipment");
        var ids=c.Items.ToList();
        if(c.Kind=="equipment_break_preview")ὩὭὨὪὨὨὮὣὪὣὥ.ὢὣὠὮὩὭὧὭὦὪὨ(delegate(EquipmentBreakPopupUI ui){ui.SetUI(ids);});
        else{
         var forge=(EquipmentForgeSelector)Fields(u.GetType()).Single(x=>x.Name=="_equipmentForgeSelector").GetValue(u);
         ὩὭὨὪὨὨὮὣὪὣὥ.ὢὣὠὮὩὭὧὭὦὪὨ(delegate(EquipmentUpgradePopupUI ui){ui.Open(ids,forge.ὤὬὪὨὤὭὤὬὨὧὫ);});
        }
       }
      }
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
