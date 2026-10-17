using System.IO;
using System.Reflection;
using System.Diagnostics;
namespace Dustweave.Desktop;
internal static class SuiteHostProbe {
 public static void Run(string output){
  Directory.CreateDirectory(output);var checks=new List<string>();
  string[] names={"BD2_DAILY_HOSTED_TOOL","BD2_DAILY_GAME_PID","BD2_DAILY_GAME_START"};
  var old=names.ToDictionary(n=>n,n=>Environment.GetEnvironmentVariable(n));
  try{
   Environment.SetEnvironmentVariable(names[1],"42");Environment.SetEnvironmentVariable(names[2],"100");
   foreach(var tool in DailyToolCatalog.All){
    Environment.SetEnvironmentVariable(names[0],tool.Id);
    var assembly=Assembly.Load(tool.Id=="equipment"?"BD2Equipment.Connection":tool.AssemblyName+".Core");
    bool rejected=false;
    if(tool.Id is not ("mansion-runaway" or "pandora")){
    var injector=assembly.GetType("SharpMonoInjector.Injector",true)!;
    try{Activator.CreateInstance(injector,[int.MaxValue]);}
    catch(TargetInvocationException e)when(e.InnerException is InvalidOperationException x&&x.Message.Contains("managed by the daily host")){rejected=true;}
    if(!rejected)throw new Exception(tool.Id+" can still inject in hosted mode");
    checks.Add(tool.Id+": hosted injector unavailable");
    }
    var pipeType=assembly.GetType("BD2.LocalIpc.PipeClient",true)!;
    bool Classifies(string method,Exception error)=>(bool)pipeType.GetMethod(method)!.Invoke(null,[error])!;
    var waiting=new TargetInvocationException(new DailyObservationPendingException("fixture-gap"));
    var changed=new TargetInvocationException(new DailyToolSessionChangedException("fixture-changed"));
    if(!Classifies("ObservationPending",waiting)||Classifies("SessionEnded",waiting)||!Classifies("SessionEnded",changed)||Classifies("ObservationPending",changed)||Classifies("SessionEnded",new IOException("ordinary-timeout")))
        throw new Exception(tool.Id+" cannot distinguish missing observation from a changed session");
    checks.Add(tool.Id+": temporary identity gaps and terminal changes remain distinct across assembly boundaries");
    var pipe=Activator.CreateInstance(pipeType,[output,int.MaxValue,1L])!;
    var guard=tool.Id is "mansion-runaway" or "pandora"?null:assembly.GetType("BD2.LocalIpc.HostedConnection",true)!;
    rejected=false;
    try{if(guard!=null)guard.GetMethod("TryOpen")!.Invoke(null,[pipe,int.MaxValue,1L]);else Activator.CreateInstance(tool.Id=="pandora"?typeof(PandoraClient):typeof(MansionClient),[output,int.MaxValue,1L,"fixture"]);}
    catch(TargetInvocationException e)when(e.InnerException is InvalidOperationException){rejected=true;}
    if(!rejected)throw new Exception(tool.Id+" accepts a changed game");
    checks.Add(tool.Id+": changed process rejected before transport");
    var timer=Stopwatch.StartNew();rejected=false;
    try{pipeType.GetMethod("Fingerprint")!.Invoke(pipe,null);}
    catch(TargetInvocationException e)when(e.InnerException is IOException){rejected=Classifies("SessionEnded",e);}
    if(!rejected||timer.ElapsedMilliseconds>1000)throw new Exception(tool.Id+" dead game check is slow");
    checks.Add(tool.Id+": dead process fails without pipe wait");
    int writes=0;AppDomain.CurrentDomain.SetData("BD2Daily.HostedWriteGuard",(Action<string,string,byte[]>)((root,name,bytes)=>{writes++;throw new InvalidOperationException("execution-owned-by-another-activity");}));
    rejected=false;
    try{pipeType.GetMethod("Write")!.Invoke(pipe,new object[]{"control.json",System.Text.Encoding.UTF8.GetBytes("{\"Enabled\":true}"),false});}
    catch(TargetInvocationException e)when(e.InnerException is InvalidOperationException x&&x.Message=="execution-owned-by-another-activity"){rejected=true;}
    finally{AppDomain.CurrentDomain.SetData("BD2Daily.HostedWriteGuard",null);}
    if(!rejected||writes!=1)throw new Exception(tool.Id+" bypasses execution ownership");
    checks.Add(tool.Id+": operation rejected before transport when another activity owns control");
    if(tool.Id is "fishing" or "territory" or "rhythm" or "sichuan")CheckControlPulse(tool.Id,assembly,Path.Combine(output,"pulse-"+tool.Id),checks);
   }
   DailyJson.Write(Path.Combine(output,"validation.json"),new{status="passed",count=checks.Count,checks,realGameTouched=false});
  }finally{foreach(var n in names)Environment.SetEnvironmentVariable(n,old[n]);}
 }
 static void CheckControlPulse(string id,Assembly assembly,string root,List<string> checks)
 {
  const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
  string prefix="BD2"+char.ToUpperInvariant(id[0])+id[1..];
  var type=assembly.GetType(prefix+"."+char.ToUpperInvariant(id[0])+id[1..]+"ControlLink",true)!;
  var files=assembly.GetType("BD2.LocalIpc.DesktopFiles",true)!;
  files.GetMethod("Configure")!.Invoke(null,[root,"control.json|enabled-until.txt|execution-lease.json"]);
  object arg=id=="sichuan"?Activator.CreateInstance(assembly.GetType("BD2Sichuan.SichuanLiveFiles",true)!,[root])!:root;
  using var link=(IDisposable)Activator.CreateInstance(type,[arg])!;
  ((System.Threading.Timer)type.GetField("timer",fields)!.GetValue(link)!).Change(Timeout.Infinite,Timeout.Infinite);
  files.GetMethod("Connect")!.Invoke(null,[root,int.MaxValue,1L]);
  object? command=id=="sichuan"?null:type.GetField(id=="rhythm"?"control":"command",fields)!.GetValue(link);
  void Set(bool enabled){if(command==null)type.GetField("enabled",fields)!.SetValue(link,enabled);else command.GetType().GetProperty("Enabled")!.SetValue(command,enabled);}
  bool Enabled()=>command==null?(bool)type.GetField("enabled",fields)!.GetValue(link)!:(bool)command.GetType().GetProperty("Enabled")!.GetValue(command)!;
  void Pulse()=>type.GetMethod("Pulse",fields)!.Invoke(link,null);
  try{
   Set(true);AppDomain.CurrentDomain.SetData("BD2Daily.HostedWriteGuard",(Action<string,string,byte[]>)((_,_,_)=>throw new DailyObservationPendingException("fixture-missing")));
   Pulse();if(!Enabled())throw new Exception(id+" stops on a temporary missing identity");
   AppDomain.CurrentDomain.SetData("BD2Daily.HostedWriteGuard",(Action<string,string,byte[]>)((_,_,_)=>throw new DailyToolSessionChangedException("fixture-changed")));
   Pulse();if(Enabled())throw new Exception(id+" keeps renewing after account change");
   Set(true);AppDomain.CurrentDomain.SetData("BD2Daily.HostedWriteGuard",null);
   Pulse();if(Enabled())throw new Exception(id+" keeps renewing after the game exits");
   checks.Add(id+": actual heartbeat retains temporary gaps and stops on changed account or exited process");
  }finally{AppDomain.CurrentDomain.SetData("BD2Daily.HostedWriteGuard",null);}
 }
}
