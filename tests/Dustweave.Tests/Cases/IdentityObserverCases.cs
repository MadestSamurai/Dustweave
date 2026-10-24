using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Text;
using Dustweave;

internal static class IdentityObserverCases
{
    public static void Run(List<string> cases)
    {
        // Execute the shipped frame observer, not a duplicate of its decision
        // logic. Native services are synthetic; no game process is touched.
        var source = Encoding.UTF8.GetString(Dustweave.Compatibility.DailyHookCompiler.Resource("Hook.RuntimeEngine.cs"));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(DailySnapshot).Assembly.Location).Append(typeof(DailyIdentityGuard).Assembly.Location).Distinct()
            .Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("IdentityObserver" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(Fixture) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var buffer = new MemoryStream(); var emitted = compilation.Emit(buffer);
        if (!emitted.Success) throw new Exception(string.Join("\n", emitted.Diagnostics));
        var runner = Assembly.Load(buffer.ToArray()).GetType("Runner")!;
        cases.AddRange((string[])runner.GetMethod("Run")!.Invoke(null, null)!);
        var startup = Encoding.UTF8.GetString(Dustweave.Compatibility.DailyHookCompiler.Resource("Hook.StartupRuntime.cs"));
        var startupCompilation = CSharpCompilation.Create("StartupObserver" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(startup), CSharpSyntaxTree.ParseText(StartupFixture) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var startupBuffer = new MemoryStream(); var startupEmitted = startupCompilation.Emit(startupBuffer);
        if (!startupEmitted.Success) throw new Exception(string.Join("\n", startupEmitted.Diagnostics));
        var startupRunner = Assembly.Load(startupBuffer.ToArray()).GetType("Runner")!;
        cases.AddRange((string[])startupRunner.GetMethod("Run")!.Invoke(null, null)!);
    }

    const string StartupFixture = """
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using BD2Daily;
using BD2Daily.Runtime;
using Dustweave;
namespace UnityEngine {
 public class Object {
  public static Object[] Surfaces=new Object[0];
  public static T[] FindObjectsOfType<T>(){return Surfaces.OfType<T>().ToArray();}
  public int GetInstanceID(){return 1;}
 }
 public class GameObject { public bool activeInHierarchy=true;public T GetComponent<T>() where T:new(){return new T();} }
}
namespace UnityEngine.UI {public class Selectable {public bool isActiveAndEnabled=true;public bool IsInteractable(){return true;}}}
public class MonoBehaviour:UnityEngine.Object {public bool isActiveAndEnabled=true;public UnityEngine.GameObject gameObject=new UnityEngine.GameObject();}
public class UIBase:MonoBehaviour {public bool Popup;}
public class UIObjectBase:MonoBehaviour {}
public class MenuUI:UIBase {}
public class MyRoomUI:UIBase {}
public class MailUI:UIBase {}
public class GachaUI:UIBase {}
public class FutureFeatureUI:UIBase {}
public class LoadingUI:UIBase {}
public class EntranceBackgroundCoverUI:UIBase {}
public class IntroUI:UIObjectBase {public string State="IntroWorkFinished";public UnityEngine.GameObject Enter=new UnityEngine.GameObject();public int Clicks;public bool Clickable=true;public bool Enabled(){return Clickable;}public void Click(){Clicks++;}}
public class DownloadPopupUI:UIBase {public UnityEngine.GameObject Button=new UnityEngine.GameObject();public int Clicks;public void OnClickUI(UnityEngine.GameObject button){Clicks++;}}
public class IntroMessagePopupUI:UIObjectBase {}
public class IntroExitPopupUI:UIBase {}
public class AppManager {
 public static AppManager Instance=new AppManager();public static AppManager Get(){return Instance;}
 public bool Active,Closing=true;public int AnimatorReads;
 public bool IsActiveUILoading(){return Active;}
 public bool IsPlayAnimUILoading(){AnimatorReads++;return Closing;}
}
namespace BD2Daily.Runtime {
 public static class StartupBindings {
  public static bool Supported=true;
  public static object Get(string role){switch(role){
   case "Services.App":return typeof(AppManager).GetMethod("Get");
   case "Title.State":return typeof(IntroUI).GetField("State");
   case "Title.Enter":return typeof(IntroUI).GetField("Enter");
   case "Title.Enabled":return typeof(IntroUI).GetMethod("Enabled");
   case "Title.Click":return typeof(IntroUI).GetMethod("Click");
   case "Download.Button":return typeof(DownloadPopupUI).GetField("Button");
   case "UI.Popup":return typeof(UIBase).GetField("Popup");
   default:throw new Exception("unexpected binding: "+role);
  }}
 }
 public static class LocalStorage {public static string Root="fixture";public static StartupPermit Permit;public static T Read<T>(string path) where T:class{return Permit as T;}}
 public static class GuildStore {public static int Writes;public static void Write(string path,object value){if(LocalStorage.Permit==null)throw new Exception("unexpected input");Writes++;}}
}
public static class Runner {
 static void Check(bool value,string name,List<string> checks){if(!value)throw new Exception(name);checks.Add("startup observer: "+name);}
 static DailySnapshot Frame(){return new DailySnapshot{ProcessId=123,ProcessStartTicks=456,FrameUtcTicks=DateTime.UtcNow.Ticks,InstanceId="fixture",Sequence=1,State="identified",Scene="Empty",AccountKey=DailyIdentity.MemberKey("1001"),PlayerKey=DailyIdentity.PlayerKey(DailyIdentity.MemberKey("1001"),50)};}
 public static string[] Run(){var checks=new List<string>();var observer=new StartupRuntime();var game=new GameInstance(123,456,"fixture");
  UnityEngine.Object.Surfaces=new UIBase[]{new MenuUI()};var menu=Frame();observer.Tick(menu);
  Check(menu.MainMenuReady&&!menu.Loading&&DailyIdentityGuard.Ready(menu,game,DateTimeOffset.UtcNow),"native Empty home menu confirms identity even with retained closing animator state",checks);
  Check(AppManager.Instance.AnimatorReads==0,"hidden animator state is never queried as loading visibility",checks);
  var guard=new DailyIdentityGuard();Check(!guard.Observe(menu,game,menu.AccountKey,menu.PlayerKey,DateTimeOffset.UtcNow),"home menu still requires two advancing identity frames",checks);
  var second=Frame();second.Sequence=2;second.FrameUtcTicks=Math.Max(second.FrameUtcTicks,menu.FrameUtcTicks+1);observer.Tick(second);
  Check(guard.Observe(second,game,second.AccountKey,second.PlayerKey,DateTimeOffset.UtcNow),"second home-menu frame completes identity verification",checks);
  AppManager.Instance.Active=true;var active=Frame();observer.Tick(active);
  Check(active.Loading&&!active.MainMenuReady&&DailyIdentityGuard.Ready(active,game,DateTimeOffset.UtcNow),"native loading is reported without invalidating logged-in identity",checks);
  AppManager.Instance.Active=false;
  foreach(var transition in new UIBase[]{new LoadingUI(),new EntranceBackgroundCoverUI()}){
   UnityEngine.Object.Surfaces=new UIBase[]{new MenuUI(),transition};var frame=Frame();observer.Tick(frame);
   Check(frame.Loading&&!frame.MainMenuReady&&DailyIdentityGuard.Ready(frame,game,DateTimeOffset.UtcNow),"visible "+transition.GetType().Name+" reports transition without logging account out",checks);
  }
  UnityEngine.Object.Surfaces=new UIBase[0];var empty=Frame();observer.Tick(empty);
  Check(!empty.MainMenuReady&&DailyIdentityGuard.Ready(empty,game,DateTimeOffset.UtcNow),"absence of a menu does not invalidate authenticated identity; input needs separate target evidence",checks);
  foreach(var page in new UIBase[]{new MyRoomUI(),new MailUI(),new GachaUI(),new FutureFeatureUI()}){
   UnityEngine.Object.Surfaces=new UIBase[]{page};var frame=Frame();observer.Tick(frame);
   Check(!frame.MainMenuReady&&!frame.Loading&&DailyIdentityGuard.Ready(frame,game,DateTimeOffset.UtcNow),"Empty child page retains identity without a UI whitelist: "+page.GetType().Name,checks);
   var again=Frame();again.Sequence=2;again.FrameUtcTicks=Math.Max(again.FrameUtcTicks,frame.FrameUtcTicks+1);observer.Tick(again);
   guard=new DailyIdentityGuard();Check(!guard.Observe(frame,game,frame.AccountKey,frame.PlayerKey,DateTimeOffset.UtcNow)&&guard.Observe(again,game,again.AccountKey,again.PlayerKey,DateTimeOffset.UtcNow),"reconnect works directly inside "+page.GetType().Name,checks);
  }
  UnityEngine.Object.Surfaces=new UIBase[]{new MyRoomUI(),new LoadingUI()};var mapLoading=Frame();mapLoading.Scene="Map3009_003";observer.Tick(mapLoading);
  Check(mapLoading.Loading&&DailyIdentityGuard.Ready(mapLoading,game,DateTimeOffset.UtcNow),"actual transition is reported on non-Empty scenes without hiding identity",checks);
  UnityEngine.Object.Surfaces=new UIBase[]{new MenuUI{isActiveAndEnabled=false}};var hidden=Frame();observer.Tick(hidden);
  Check(!hidden.MainMenuReady,"disabled menu cannot authorize an empty scene",checks);
  UnityEngine.Object.Surfaces=new UIBase[]{new MenuUI(),new MenuUI()};var duplicate=Frame();observer.Tick(duplicate);
  Check(!duplicate.MainMenuReady,"ambiguous duplicate menu cannot authorize an empty scene",checks);
  UnityEngine.Object.Surfaces=new UIBase[]{new MenuUI()};var tool=Frame();observer.Tick(tool,false);
  Check(DailyIdentityGuard.Ready(tool,game,DateTimeOffset.UtcNow),"tool ownership disables startup actions but preserves scene observation",checks);
  UnityEngine.Object.Surfaces=new UnityEngine.Object[]{new IntroUI()};var title=Frame();title.Scene="Splash";observer.Tick(title);
  Check(title.Startup.Visible&&!DailyIdentityGuard.Ready(title,game,DateTimeOffset.UtcNow),"visible title still cannot pass login verification",checks);
  var ownedTitle=Frame();observer.Tick(ownedTitle,false);
  Check(ownedTitle.Startup.Visible&&!DailyIdentityGuard.Ready(ownedTitle,game,DateTimeOffset.UtcNow),"tool ownership cannot hide a newly visible login page",checks);
  UnityEngine.Object.Surfaces=new UnityEngine.Object[]{new IntroUI(),new IntroUI()};var titles=Frame();observer.Tick(titles);
  Check(titles.Startup.Visible&&!DailyIdentityGuard.Ready(titles,game,DateTimeOffset.UtcNow),"ambiguous login pages remain blocked",checks);
  UnityEngine.Object.Surfaces=new UIBase[0];var map=Frame();map.Scene="Map3009_003";observer.Tick(map);
  Check(DailyIdentityGuard.Ready(map,game,DateTimeOffset.UtcNow),"loaded map remains eligible without a home menu",checks);
  var nativeTitle=new IntroUI();UnityEngine.Object.Surfaces=new UnityEngine.Object[]{nativeTitle};
  observer=new StartupRuntime();var ready=Frame();ready.Scene="Splash";
  LocalStorage.Permit=new StartupPermit{Owner="test",AccountKey=ready.AccountKey,InstanceId=ready.InstanceId,ProcessId=ready.ProcessId,ProcessStartTicks=ready.ProcessStartTicks,ExpiresUtcTicks=DateTime.UtcNow.AddSeconds(10).Ticks};
  UnityEngine.Object.Surfaces=new UnityEngine.Object[]{nativeTitle,new IntroMessagePopupUI()};observer.Tick(ready);
  Check(ready.Startup.Visible&&ready.Startup.BlockReason.Contains("IntroMessagePopupUI")&&nativeTitle.Clicks==0,"native UIObjectBase message popup blocks title input",checks);
  UnityEngine.Object.Surfaces=new UnityEngine.Object[]{nativeTitle};ready=Frame();observer.Tick(ready,false);
  Check(ready.Startup.Visible&&nativeTitle.Clicks==0,"owned tool still blocks title click with a valid permit",checks);
  ready=Frame();observer.Tick(ready);
  Check(ready.Startup.Visible&&ready.Startup.Ready&&ready.Startup.Attempted&&nativeTitle.Clicks==1&&BD2Daily.Runtime.GuildStore.Writes==1,"native UIObjectBase title reaches its actual click method",checks);
  observer.Tick(Frame());Check(nativeTitle.Clicks==1,"same title is not clicked repeatedly",checks);
  var download=new DownloadPopupUI{Popup=true};nativeTitle=new IntroUI{State="CheckDownload"};observer=new StartupRuntime();
  UnityEngine.Object.Surfaces=new UnityEngine.Object[]{nativeTitle,download};ready=Frame();observer.Tick(ready);
  Check(ready.Startup.DownloadVisible&&ready.Startup.DownloadAttempted&&download.Clicks==1&&nativeTitle.Clicks==0,"download on a UIObjectBase title still uses its normal native confirmation",checks);
  LocalStorage.Permit=null;
  return checks.ToArray();
 }
}
""";

    const string Fixture = """
using System;
using System.Reflection;
using System.Collections.Generic;
using BD2Daily;
using BD2Daily.Runtime;
using Dustweave;
namespace HarmonyLib {
 public enum HarmonyPatchType { All }
 public class HarmonyMethod { public HarmonyMethod(MethodInfo method){} }
 public class Harmony { public string Id="fixture";public Harmony(string id){} public void Patch(MethodInfo method,HarmonyMethod postfix){} public void Unpatch(MethodInfo method,HarmonyPatchType kind,string id){} }
}
namespace Neo.Unity.Neon { public class Unused{} }
namespace Proto.Net { public class UserDBInfo { public long OwnerIndex=50;public string UserId="fixture"; } }
namespace UnityEngine {
 public static class Canvas { public static event Action willRenderCanvases; }
 public static class Time { public static int frameCount;public static float timeScale=1; }
 public static class Application { public static bool isFocused=true,runInBackground=true; }
}
namespace UnityEngine.SceneManagement {
 public struct Scene { public string name; }
 public static class SceneManager { public static Scene GetActiveScene(){return new Scene{name="MainMenu"};} }
}
namespace BD2.LocalIpc {
 public static class LoadingTrace { public static bool Paused;public static void Start(){} public static long Begin(string name){return 0;} public static void End(string name,long started){} public static void Note(string name,string value){} }
}
namespace BD2Daily.Runtime {
 public class GameData { public static Proto.Net.UserDBInfo Player {get;set;}=new Proto.Net.UserDBInfo();public void Frame(){} }
 public static class ClientNames { public const string DataType="BD2Daily.Runtime.GameData",DataProperty="Player",PumpType="BD2Daily.Runtime.GameData",PumpMethod="Frame"; }
 public static class SdkIdentity { public static bool Ready=true;public static bool TryRead(out string key){key=Ready?DailyIdentity.MemberKey("1001"):"";return Ready;} }
 public static class Loader { public static string State,Error;public static void WriteStatus(string state,string error){State=state;Error=error;} }
 public static class LocalStorage {
  public static DailySnapshot Last;public static bool FailWrite;
  public static T Read<T>(string name)where T:class{return null;}
  public static void Write(string name,object value){if(FailWrite)throw new System.IO.IOException("fixture write failure");Last=(DailySnapshot)value;}
 }
 public static class SuiteRuntime {
  public static bool Fail,ToolActive;public static long SeenPublished;
  public static void Start(){} public static void Stop(){} public static void Dispose(){} public static string Busy(){return "";}
  public static void Tick(DailySnapshot s){SeenPublished=LocalStorage.Last==null?0:LocalStorage.Last.Sequence;if(Fail)throw new InvalidOperationException("fixture module status failure");}
 }
 public class GuildRuntime { public bool HandoffBusy;public void Start(){} public void Stop(){} public void Tick(DailySnapshot s,bool allow){} }
 public class StartupRuntime {
  public static bool Loading,Visible,Fail;
  public void Tick(DailySnapshot s,bool allowActions=true){if(Fail)throw new InvalidOperationException("fixture startup failure");s.Loading=Loading;s.Startup=new StartupObservation{Visible=Visible};}
 }
 public static class LoadingTraceHooks { public static void Start(){}public static void Stop(){}public static void Observe(){} }
}
public static class Runner {
 static readonly Type Engine=typeof(RuntimeEngine);
 static void Set(object engine,string name,object value){Engine.GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(engine,value);}
 static void Heartbeat(object engine){Engine.GetMethod("Heartbeat",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(engine,null);}
 static void Tick(object engine){Set(engine,"nextRead",0L);Engine.GetMethod("AfterFrame",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);}
 static void Check(bool value,string name,List<string> checks){if(!value)throw new Exception(name);checks.Add("identity observer: "+name);}
 public static string[] Run(){var checks=new List<string>();var engine=new RuntimeEngine();
  // Set the same bindings as Start without installing callbacks or timers.
  Engine.GetField("active",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,engine);
  Set(engine,"player",typeof(GameData).GetProperty("Player"));Set(engine,"pid",123);Set(engine,"start",456L);
  Set(engine,"startup",new StartupRuntime());Set(engine,"guild",new GuildRuntime());
  var game=new GameInstance(123,456,"fixture");
  Tick(engine);var first=LocalStorage.Last;
  Check(DailyIdentityGuard.Ready(first,game,DateTimeOffset.UtcNow),"normal main-thread identity remains valid",checks);
  SuiteRuntime.Fail=true;
  for(int i=0;i<10;i++)Tick(engine);
  Check(LocalStorage.Last.Sequence==first.Sequence+10,"module status failures cannot starve identity publication",checks);
  Check(SuiteRuntime.SeenPublished==LocalStorage.Last.Sequence,"identity reaches mailbox before module control callbacks",checks);
  Heartbeat(engine);Check(Loader.State=="active"&&Loader.Error.Contains("module-control"),"optional module failure is visible without falsely invalidating identity",checks);
  var guard=new DailyIdentityGuard();var current=LocalStorage.Last;
  Check(!guard.Observe(current,game,current.AccountKey,current.PlayerKey,DateTimeOffset.UtcNow),"one frame alone still grants no authority",checks);
  Tick(engine);Check(guard.Observe(LocalStorage.Last,game,current.AccountKey,current.PlayerKey,DateTimeOffset.UtcNow),"advancing identity can recover independently of module failure",checks);
  StartupRuntime.Loading=true;Tick(engine);
  Check(LocalStorage.Last.Loading&&DailyIdentityGuard.Ready(LocalStorage.Last,game,DateTimeOffset.UtcNow),"publishing retains loading status independently of authenticated identity",checks);
  StartupRuntime.Loading=false;StartupRuntime.Visible=true;Tick(engine);
  Check(!DailyIdentityGuard.Ready(LocalStorage.Last,game,DateTimeOffset.UtcNow),"visible title still blocks identity confirmation",checks);
  StartupRuntime.Visible=false;StartupRuntime.Fail=true;Tick(engine);
  Check(LocalStorage.Last.State=="read_error"&&!DailyIdentityGuard.Ready(LocalStorage.Last,game,DateTimeOffset.UtcNow),"failed startup observation cannot masquerade as a ready game",checks);
  StartupRuntime.Fail=false;SuiteRuntime.Fail=false;LocalStorage.FailWrite=true;Tick(engine);Heartbeat(engine);
  Check(Loader.State=="error"&&Loader.Error.Contains("identity-publish"),"heartbeat preserves failed identity publication",checks);
  LocalStorage.FailWrite=false;Tick(engine);Heartbeat(engine);
  Check(Loader.State=="active"&&Loader.Error=="","successful fresh publication clears a recovered failure",checks);
  SdkIdentity.Ready=false;Tick(engine);
  Check(LocalStorage.Last.State=="waiting_sdk"&&!DailyIdentityGuard.Ready(LocalStorage.Last,game,DateTimeOffset.UtcNow),"SDK readiness remains mandatory",checks);
  SdkIdentity.Ready=true;Tick(engine);guard=new DailyIdentityGuard();bool rejected=false;
  try{guard.Observe(LocalStorage.Last,game,DailyIdentity.MemberKey("9999"),"",DateTimeOffset.UtcNow);}catch(InvalidOperationException){rejected=true;}
  Check(rejected,"fresh identity for a different account remains rejected",checks);
  Set(engine,"stopped",true);Loader.State="retired";Heartbeat(engine);
  Check(Loader.State=="retired","retired observer cannot overwrite successor heartbeat",checks);
  Engine.GetField("active",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,null);
  return checks.ToArray();
 }
}
""";
}
