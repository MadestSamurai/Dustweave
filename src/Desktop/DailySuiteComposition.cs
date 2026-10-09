extern alias liveUtility;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Dustweave.Compatibility;
using Mono.Cecil;
namespace Dustweave.Desktop;

internal static class DailySuiteComposition {
 private static readonly SemaphoreSlim compiler=new(1,1);
 private static readonly Dictionary<string,string> prefixes=new(){
  ["fishing"]="BD2Fishing",["sichuan"]="BD2Sichuan",["territory"]="BD2Territory",
  ["rhythm"]="BD2Rhythm",["apostle-defense"]="BD2ApostleDefense",
  ["infinite-gacha"]="BD2InfiniteGacha",["secret-vision"]="BD2SecretVision",["fiend-hunter"]="BD2FiendHunter"};
 public static void Configure(){
  if(Environment.GetEnvironmentVariable("BD2_DAILY_SUITE_OWNER")==null)
   { Environment.SetEnvironmentVariable("BD2_DAILY_SUITE_OWNER",Guid.NewGuid().ToString("N")); using var owner=System.Diagnostics.Process.GetCurrentProcess(); Environment.SetEnvironmentVariable("BD2_DAILY_OWNER_PID",owner.Id.ToString()); Environment.SetEnvironmentVariable("BD2_DAILY_OWNER_START",owner.StartTime.ToUniversalTime().Ticks.ToString()); }
  // The connection must not load or resolve the nine optional compilers.
  DailyHookCompiler.SuiteFingerprint=DailySuite.ConnectionFingerprint(typeof(DailySuiteComposition).Module.ModuleVersionId,DailyPlugin.Current.Fingerprint);
  DailySuite.Prepare=Prepare;
  DailySuite.PrepareModule=PrepareModule;
 }
 private static Task<string> ClientKey(string managed,CancellationToken cancel)=>Task.Run(()=>ClientInputs.ReadManaged(managed).Key,cancel);
 public static async Task<PreparedDailyHook> Prepare(string managed,Action<string> progress,CancellationToken cancel){
  await compiler.WaitAsync(cancel);
  try{
   string clientKey=await ClientKey(managed,cancel);
   string directory=Path.Combine(DailyIdentity.DataRoot,"suite-cache",DailyHookCompiler.Fingerprint,clientKey);
   Directory.CreateDirectory(directory);
   string cache=Path.Combine(directory,"observer.dll"), report=Path.Combine(directory,"observer.json");
   if(File.Exists(cache)&&DailyJson.TryRead<CacheInfo>(report) is {} info){
    var bytes=await File.ReadAllBytesAsync(cache,cancel);
    if(Hash(bytes)==info.Hash)return new(bytes,info.Report,info.Guild,info.Startup);
   }
   progress("准备公共连接组件");
   // An empty manifest enables the module host without compiling or loading any business tool.
   var result=await Task.Run(()=>DailyHookCompiler.Prepare(managed,suite:new Dictionary<string,byte[]>(),manifest:""),cancel);
   await Task.Run(()=>ClientInputs.RequireStable(managed,clientKey),cancel);
   await File.WriteAllBytesAsync(cache,result.Payload,cancel);
   DailyJson.Write(report,new CacheInfo(Hash(result.Payload),result.Report,result.GuildReport,result.StartupReport));
   DailyJson.Write(Path.Combine(directory,"modules.json"),new{included=Array.Empty<string>(),onDemand=true,realGameTouched=false});
   return result;
  }finally{compiler.Release();}
 }
 public static async Task<byte[]> PrepareModule(string managed,string id,Action<string> progress,CancellationToken cancel){
  if(SuiteRules.ModuleEntry(id).Length==0)throw new ArgumentException("未知功能："+id);
  await compiler.WaitAsync(cancel);
  try{
   cancel.ThrowIfCancellationRequested();
   string tool=id.EndsWith("-daily",StringComparison.Ordinal)?id[..^6]:id;
   string assemblyName=id=="mansion-runaway"?"Dustweave.Compatibility":id=="daily"?"Dustweave.Connection":tool=="equipment"?"BD2Equipment.Connection":prefixes[tool]+".Compatibility";
   var assembly=Assembly.Load(assemblyName);
   string key=DailyIdentity.Hash("module-v1|"+id+"|"+assembly.ManifestModule.ModuleVersionId+"|"+(id=="daily"?DailyPlugin.Current.Fingerprint:""));
   string clientKey=await ClientKey(managed,cancel);
   string directory=Path.Combine(DailyIdentity.DataRoot,"suite-cache","modules",id,key,clientKey);
   Directory.CreateDirectory(directory);
   string cache=Path.Combine(directory,"module.dll"),report=Path.Combine(directory,"module.json");
   if(File.Exists(cache)&&DailyJson.TryRead<ModuleCache>(report) is {} info){
    var bytes=await File.ReadAllBytesAsync(cache,cancel);
    if(Hash(bytes)==info.Hash){progress("复用已准备组件 · "+Name(id));return bytes;}
   }
   progress("首次准备 · "+Name(id));
   byte[] payload;
   if(id=="mansion-runaway"){
    payload=await Task.Run(()=>MansionCompiler.Prepare(managed),cancel);
   }else if(id=="daily"){
    string target=Path.Combine(directory,"build-"+Guid.NewGuid().ToString("N"));
    // Adaptation and compilation are CPU-heavy even before RunAsync's first await.
    await Task.Run(()=>liveUtility::Dustweave.Connection.LiveEntry.RunAsync(["prepare",managed,target],false),cancel);
    if(!File.Exists(Path.Combine(target,"bridge.dll")))throw new InvalidDataException("日常执行组件编译失败，尚未启用。");
    payload=await File.ReadAllBytesAsync(Path.Combine(target,"bridge.dll"),cancel);
   }else if(tool=="equipment"){
    string target=Path.Combine(directory,"build-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(target);
    await Task.Run(()=>assembly.GetType("BD2Equipment.Live.LiveEntry")!.GetMethod("Prepare")!.Invoke(null,[managed,target]),cancel);
    payload=await File.ReadAllBytesAsync(Path.Combine(target,"bridge.dll"),cancel);
   }else{
    var method=assembly.GetType(prefixes[tool]+".Compatibility.HookCompiler")!.GetMethod("Prepare")!;
    var args=method.GetParameters().Select(p=>p.Position==0?(object)managed:p.Name=="daily"?(object)id.EndsWith("-daily",StringComparison.Ordinal):p.DefaultValue).ToArray();
    var prepared=await Task.Run(()=>method.Invoke(null,args),cancel);
    payload=(byte[])prepared!.GetType().GetProperty("Payload")!.GetValue(prepared)!;
   }
   cancel.ThrowIfCancellationRequested();
   if(payload.Length==0||payload.Length>8*1024*1024)throw new InvalidDataException("功能组件大小异常，未发送。");
   await Task.Run(()=>ClientInputs.RequireStable(managed,clientKey),cancel);
   await File.WriteAllBytesAsync(cache,payload,cancel);
   DailyJson.Write(report,new ModuleCache(Hash(payload)));
   return payload;
  }finally{compiler.Release();}
 }
 private static string Name(string id)=>id=="mansion-runaway"?"MANSION RUNAWAY":id=="daily"?"日常执行":DailyToolCatalog.Find(id.EndsWith("-daily",StringComparison.Ordinal)?id[..^6]:id).Name;
 private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
 public sealed record CacheInfo(string Hash,BindingReport Report,BindingReport Guild,BindingReport Startup);
 public sealed record ModuleCache(string Hash);
 // Full compilation belongs to explicit offline release validation, never the user's connect path.
 public static async Task Check(string managed,string output){
  Directory.CreateDirectory(output);
  var prepared=await Prepare(managed,_=>{},CancellationToken.None);
  using var module=ModuleDefinition.ReadModule(new MemoryStream(prepared.Payload));
  var manifest=(EmbeddedResource)module.Resources.Single(r=>r.Name=="Suite.manifest");
  if(manifest.GetResourceData().Length!=0||module.Resources.Any(r=>r.Name.StartsWith("Suite.")&&r.Name.EndsWith(".dll")))throw new InvalidDataException("Initial connection eagerly prepared tool modules.");
  await File.WriteAllBytesAsync(Path.Combine(output,"observer.dll"),prepared.Payload);
  var entries=new List<object>();
  foreach(string id in new[]{"daily"}.Concat(prefixes.Keys).Concat(["equipment","fishing-daily","sichuan-daily","mansion-runaway"])){
   var bytes=await PrepareModule(managed,id,_=>{},CancellationToken.None);
   using var child=ModuleDefinition.ReadModule(new MemoryStream(bytes));
   var entry=child.GetType(SuiteRules.ModuleEntry(id))??throw new InvalidDataException("Missing module loader: "+id);
   if(!entry.Methods.Any(m=>m.Name=="Load"&&m.IsStatic&&m.Parameters.Count==0))throw new InvalidDataException("Missing module load: "+id);
   entries.Add(new{id,bytes=bytes.Length,sha256=Hash(bytes)});
  }
  DailyJson.Write(Path.Combine(output,"suite.json"),new{status="passed",onDemand=true,initialModules=0,entries,bytes=prepared.Payload.Length,realGameTouched=false});
 }
}
