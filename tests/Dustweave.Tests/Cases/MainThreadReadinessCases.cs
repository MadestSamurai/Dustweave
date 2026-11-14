using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Dustweave.Compatibility;

internal static class MainThreadReadinessCases
{
    public static async Task Run(List<string> cases)
    {
        // Other observer fixtures intentionally load their own UnityEngine.Canvas.
        // A fresh test-owned process makes late assembly loading independent of
        // test ordering and cannot leave a synthetic Canvas in subsequent groups.
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardOutput=true,RedirectStandardError=true};
        start.ArgumentList.Add("--main-thread-readiness");
        using var child=Process.Start(start)??throw new Exception("Readiness fixture did not start");
        var output=child.StandardOutput.ReadToEndAsync();var error=child.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{await child.WaitForExitAsync(timeout.Token);}
        catch(OperationCanceledException){child.Kill();throw new TimeoutException("Isolated readiness fixture timed out");}
        if(child.ExitCode!=0)throw new Exception(await error+await output);
        cases.AddRange(JsonSerializer.Deserialize<string[]>(await output)!);
    }
    public static async Task<string[]> RunChild()
    {
        // Execute the packaged bootstrap and scheduler against a UI assembly
        // loaded late. No game, injection or real account storage is involved.
        var sources=new[]{"Hook.Ipc.MainThread.cs","Hook.Ipc.Handoff.cs","Hook.Loader.cs"}
            .Select(n=>CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(DailyHookCompiler.Resource(n))))
            .Append(CSharpSyntaxTree.ParseText(Fixture));
        var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(p=>MetadataReference.CreateFromFile(p)).ToArray();
        Assembly Compile(string name,IEnumerable<SyntaxTree> trees) {
            var compiled=CSharpCompilation.Create(name+Guid.NewGuid().ToString("N"),trees,references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var bytes=new MemoryStream();var emitted=compiled.Emit(bytes);
            if(!emitted.Success)throw new Exception(string.Join("\n",emitted.Diagnostics));
            return Assembly.Load(bytes.ToArray());
        }
        var runner=Compile("ColdConnection",sources).GetType("Runner")!;
        var waiting=(Task<string[]>)runner.GetMethod("BeforeUi")!.Invoke(null,null)!;
        var cases=new List<string>(await waiting);
        var canvas=Compile("LateUnityUi",new[]{CSharpSyntaxTree.ParseText(Canvas)}).GetType("UnityEngine.Canvas")!;
        cases.AddRange(await (Task<string[]>)runner.GetMethod("AfterUi")!.Invoke(null,[canvas])!);
        return cases.ToArray();
    }
    const string Canvas="""
using System;
public static class DummyCanvasMarker {}
namespace UnityEngine {
 public static class Canvas {
  static Action callbacks;public static bool RejectSubscription;
  public static event Action willRenderCanvases {
   add { if(RejectSubscription)throw new InvalidOperationException("fixture permanent UI subscription failure");callbacks+=value; }
   remove { callbacks-=value; }
  }
  public static int Count {get{return callbacks==null?0:callbacks.GetInvocationList().Length;}}
  public static void Render(){if(callbacks!=null)callbacks();}
 }
}
""";
    const string Fixture="""
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BD2.LocalIpc;
using BD2Daily.Runtime;
namespace BD2.LocalIpc {
 public static class Build {public const string Fingerprint="synthetic";}
 public static class LegacyPilots {public static void Discover(){}}
 public static class RuntimeFiles {
  public static int Starts,Revokes;
  public static void Start(string root,string stamp,string names){Starts++;}
  public static void Revoke(){Revokes++;}public static void Activate(){}
  public static bool Handles(string path){return true;}public static byte[] Read(string path){return null;}
  public static bool Write(string path,byte[] data){return true;}
 }
}
namespace BD2Daily.Runtime {
 public static class DailyIdentity {public const string LiveEntries="fixture";}
 public sealed class DailyRuntimeStatus {public string State,Error;public int ProcessId;public long ProcessStartTicks,AtUtcTicks;}
 public static class LocalStorage {
  public const string Root="synthetic";public static DailyRuntimeStatus Status;
  public static void Write(string name,object value){Status=(DailyRuntimeStatus)value;}
 }
 public static class LiveStore {public static Func<string,bool> Handles;public static Func<string,byte[]> Read;public static Action<string,byte[],bool> Write;}
 public sealed class RuntimeEngine {
  public static int Starts,Stops;
  internal void Start(){Starts++;}internal void Stop(){Stops++;}
  internal void PrepareHandoff(){}internal string HandoffBusy(){return "";}
 }
}
public static class Runner {
 static MainThread resumed,canceled,pending;static int resumedTicks,pendingTicks,failures;
 static readonly List<string> checks=new List<string>();
 static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add("connection-ui: "+label);}
 static object Field(object value,string name){return value.GetType().GetField(name,Private).GetValue(value);}
 static MainThread LoaderFrame(){return (MainThread)typeof(Loader).GetField("frame",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);}
 static async Task Until(Func<bool> condition){var until=DateTime.UtcNow.AddSeconds(5);while(!condition()){if(DateTime.UtcNow>=until)throw new Exception("fixture wait expired");await Task.Delay(25);}}
 public static async Task<string[]> BeforeUi(){
  var immediate=new MainThread(()=>false);bool notReady=false;
  try{immediate.Schedule();}catch(InvalidOperationException){notReady=true;}
  Check(notReady,"cold UI fixture reproduces the previous synchronous startup exception");
  resumed=new MainThread(()=>{resumedTicks++;return false;},e=>Interlocked.Increment(ref failures));
  resumed.ScheduleWhenReady(TimeSpan.FromMilliseconds(100));
  await Until(()=>Volatile.Read(ref failures)==1);
  Check(resumedTicks==0&&Field(resumed,"waiting")==null,"timeout reports once, releases timer and never runs the engine off the UI thread");
  resumed.ScheduleWhenReady(TimeSpan.FromSeconds(10));var timer=Field(resumed,"waiting");
  resumed.ScheduleWhenReady(TimeSpan.FromSeconds(10));
  Check(ReferenceEquals(timer,Field(resumed,"waiting")),"repeated connection while waiting shares one bounded wait");
  canceled=new MainThread(()=>throw new Exception("disposed waiter fired"));
  canceled.ScheduleWhenReady(TimeSpan.FromSeconds(10));canceled.Dispose();
  Check(Field(canceled,"waiting")==null,"cancel before the first UI frame releases the wait");
  pending=new MainThread(()=>{pendingTicks++;return pendingTicks<2;});pending.ScheduleWhenReady(TimeSpan.FromSeconds(10));
  typeof(Loader).GetField("harmonyAssembly",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,typeof(object).Assembly);
  Loader.Load();Loader.Load();
  Check(RuntimeEngine.Starts==0&&RuntimeFiles.Starts==1&&LocalStorage.Status.State=="handoff","real bootstrap remains pending without duplicate IPC startup or a synchronous exception");
  Loader.Unload();
  Check(Field(LoaderFrame(),"waiting")==null&&LocalStorage.Status.State=="inactive","cold bootstrap can be unloaded without waiting for Canvas");
  Loader.Load();
  // Advance only the fixture deadline, then let the real timer handle timeout.
  LoaderFrame().GetType().GetField("deadline",Private).SetValue(LoaderFrame(),DateTime.UtcNow.AddSeconds(-1));
  await Until(()=>LocalStorage.Status.State=="error");
  Check(LocalStorage.Status.Error.Contains("Unity UI")&&RuntimeEngine.Starts==0,"bootstrap timeout retains a concrete error instead of an unavailable managed message");
  Loader.Load();
  Check(LocalStorage.Status.State=="handoff"&&Field(LoaderFrame(),"waiting")!=null,"a new connection can resume after a previous bootstrap timeout");
  var result=checks.ToArray();checks.Clear();return result;
 }
 public static async Task<string[]> AfterUi(Type canvas){
  Func<int> count=()=> (int)canvas.GetProperty("Count").GetValue(null);
  Action render=()=>canvas.GetMethod("Render").Invoke(null,null);
  await Until(()=>count()==3);
  Check(resumedTicks==0&&pendingTicks==0&&RuntimeEngine.Starts==0,"late assembly subscribes all live waits but never executes game work on timer threads");
  Check(Field(resumed,"waiting")==null&&Field(pending,"waiting")==null&&Field(LoaderFrame(),"waiting")==null,"all readiness timers stop after successful subscription");
  render();await Task.Delay(230);render();render();render();
  Check(resumedTicks==1&&pendingTicks==2&&failures==1,"timed-out then retried connection succeeds, repeats only while requested, and canceled work stays canceled");
  Check(RuntimeEngine.Starts==1&&LocalStorage.Status.State=="active"&&count()==0,"actual loader finishes one main-thread handoff and releases its frame subscription");
  Loader.Load();render();
  Check(RuntimeEngine.Starts==1&&LocalStorage.Status.State=="active","reconnecting an active bootstrap does not start a second engine");
  Loader.Unload();render();
  Check(RuntimeEngine.Stops==1&&LocalStorage.Status.State=="inactive"&&count()==0,"active unload still drains through the main thread");
  var cancelledReady=new MainThread(()=>throw new Exception("unsubscribed callback fired"));
  cancelledReady.ScheduleWhenReady(TimeSpan.FromSeconds(1));cancelledReady.Dispose();render();
  Check(count()==0,"cancel after readiness removes the subscribed callback");
  int callbackErrors=0;var broken=new MainThread(()=>throw new InvalidOperationException("fixture tick error"),e=>callbackErrors++);
  broken.ScheduleWhenReady(TimeSpan.FromSeconds(1));render();render();
  Check(callbackErrors==1&&count()==0,"a hard main-thread failure reports once and is not retried blindly");
  canvas.GetField("RejectSubscription").SetValue(null,true);
  var rejected=new MainThread(()=>false);bool hard=false;
  try{rejected.ScheduleWhenReady(TimeSpan.FromSeconds(1));}catch(TargetInvocationException){hard=true;}
  Check(hard&&Field(rejected,"waiting")==null,"non-readiness subscription errors propagate without an endless retry");
  canvas.GetField("RejectSubscription").SetValue(null,false);
  rejected.ScheduleWhenReady(TimeSpan.FromSeconds(1));render();
  Check(count()==0,"a later explicit retry can recover after the subscription failure is corrected");
  return checks.ToArray();
 }
}
""";
}
