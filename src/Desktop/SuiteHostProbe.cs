using System.IO;
using System.Reflection;
using System.Diagnostics;
namespace BD2Daily.Desktop;
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
    var injector=assembly.GetType("SharpMonoInjector.Injector",true)!;
    bool rejected=false;
    try{Activator.CreateInstance(injector,[int.MaxValue]);}
    catch(TargetInvocationException e)when(e.InnerException is InvalidOperationException x&&x.Message.Contains("managed by the daily host")){rejected=true;}
    if(!rejected)throw new Exception(tool.Id+" can still inject in hosted mode");
    checks.Add(tool.Id+": hosted injector unavailable");
    var pipeType=assembly.GetType("BD2.LocalIpc.PipeClient",true)!;
    var pipe=Activator.CreateInstance(pipeType,[output,int.MaxValue,1L])!;
    var guard=assembly.GetType("BD2.LocalIpc.HostedConnection",true)!;
    rejected=false;
    try{guard.GetMethod("TryOpen")!.Invoke(null,[pipe,int.MaxValue,1L]);}
    catch(TargetInvocationException e)when(e.InnerException is InvalidOperationException){rejected=true;}
    if(!rejected)throw new Exception(tool.Id+" accepts a changed game");
    checks.Add(tool.Id+": changed process rejected before transport");
    var timer=Stopwatch.StartNew();rejected=false;
    try{pipeType.GetMethod("Fingerprint")!.Invoke(pipe,null);}
    catch(TargetInvocationException e)when(e.InnerException is IOException){rejected=true;}
    if(!rejected||timer.ElapsedMilliseconds>1000)throw new Exception(tool.Id+" dead game check is slow");
    checks.Add(tool.Id+": dead process fails without pipe wait");
    int writes=0;AppDomain.CurrentDomain.SetData("BD2Daily.HostedWriteGuard",(Action<string,string,byte[]>)((root,name,bytes)=>{writes++;throw new InvalidOperationException("execution-owned-by-another-activity");}));
    rejected=false;
    try{pipeType.GetMethod("Write")!.Invoke(pipe,new object[]{"control.json",System.Text.Encoding.UTF8.GetBytes("{\"Enabled\":true}"),false});}
    catch(TargetInvocationException e)when(e.InnerException is InvalidOperationException x&&x.Message=="execution-owned-by-another-activity"){rejected=true;}
    finally{AppDomain.CurrentDomain.SetData("BD2Daily.HostedWriteGuard",null);}
    if(!rejected||writes!=1)throw new Exception(tool.Id+" bypasses execution ownership");
    checks.Add(tool.Id+": operation rejected before transport when another activity owns control");
   }
   DailyJson.Write(Path.Combine(output,"validation.json"),new{status="passed",count=checks.Count,checks,realGameTouched=false});
  }finally{foreach(var n in names)Environment.SetEnvironmentVariable(n,old[n]);}
 }
}
