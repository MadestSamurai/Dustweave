using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using BD2Equipment.Core;
namespace BD2Equipment;
internal sealed class WorkerClient
{
 internal bool OfflineChecks;
 internal static string Data=>J.Data;
 internal static string StopPath=>J.Stop;
 internal async Task<JsonObject> Run(JsonObject job,Action<JsonObject>? progress=null){
  if(OfflineChecks&&J.S(job["operation"]) is "capture" or "execute")throw new InvalidOperationException("Offline checks cannot connect or consume resources");
  // The host migrates legacy logs during idle maintenance, never before inventory reads.
  Directory.CreateDirectory(J.Diagnostics);string path=Path.Combine(J.Diagnostics,Guid.NewGuid().ToString("N")+".json");var elapsed=Stopwatch.StartNew();
  var reports=new Progress<JsonObject>(p=>progress?.Invoke(p));
  try{var result=await Task.Run(()=>EquipmentService.Handle(job,OfflineChecks?null:Invoke,p=>((IProgress<JsonObject>)reports).Report(p)));J.Write(path,new JsonObject{["operation"]=J.S(job["operation"]),["state"]="completed",["elapsedMs"]=elapsed.ElapsedMilliseconds,["timings"]=result["timings"]?.DeepClone()});return result;}
  catch(Exception ex){J.Write(path,new JsonObject{["operation"]=J.S(job["operation"]),["state"]="failed",["elapsedMs"]=elapsed.ElapsedMilliseconds,["job"]=job.DeepClone()});await File.WriteAllTextAsync(Path.ChangeExtension(path,"log"),ex.ToString(),Encoding.UTF8);if(EquipmentReadRetry.Transient(ex))throw new InvalidOperationException(L.T(J.S(job["operation"])=="execute"?"游戏连接暂时超时；已保留本批进度，不会重复消耗材料。请重新连接并读取库存后接续。":"游戏连接暂时超时，尚未开始制作。请保持游戏已登录，再点击连接读取；请在织尘的诊断页面导出日志。"),ex);throw;}
 }
 internal static string Invoke(string[] args){
  var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=AppContext.BaseDirectory};start.ArgumentList.Add("--connection");foreach(var arg in args)start.ArgumentList.Add(arg);
  using var process=Process.Start(start)??throw new InvalidOperationException(L.T("无法启动内置连接入口"));var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
  if(!process.WaitForExit(120000)){process.Kill(true);throw new InvalidOperationException(L.T("连接准备超时，请查看诊断记录"));}
  string text=output.GetAwaiter().GetResult(),detail=error.GetAwaiter().GetResult();if(process.ExitCode!=0)throw new InvalidOperationException(detail.Length>0?detail:text);return text;
 }
}
