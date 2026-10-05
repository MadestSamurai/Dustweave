#if DESKTOP
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SharpMonoInjector;
using BD2.LocalIpc;using System.Reflection.Metadata;using System.Reflection.PortableExecutable;
namespace BD2FiendHunter.Shared;
public static class Client {
 public static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2FiendHunter");
 static Client(){DesktopFiles.Configure(Root,"runtime.json|state.json|error.json|control.json|command.json|stop|receipt-*");}
 static PipeClient? pipe;static bool acquired;
 static PipeClient Reader(){if(pipe!=null)return pipe;using var game=Process.GetProcessesByName("BrownDust II").SingleOrDefault()??throw new IOException("请先启动游戏");return pipe=DesktopFiles.Connect(Root,game.Id,game.StartTime.ToUniversalTime().Ticks);}
 public static void Acquire(){if(!acquired){var p=Reader();p.Open(p.Fingerprint());acquired=true;}}
 public static readonly JsonSerializerOptions Options=new(){IncludeFields=true};
 public static JsonObject? State(){try{Reader();return JsonNode.Parse(AtomicFiles.Read(Path.Combine(Root,"state.json")))!.AsObject();}catch(Exception e)when(e is IOException or JsonException){return null;}}
 public static bool Fresh(JsonObject? s)=>s!=null&&DateTimeOffset.TryParse((string?)s["AtUtc"],out var t)&&DateTimeOffset.UtcNow-t>=TimeSpan.Zero&&DateTimeOffset.UtcNow-t<TimeSpan.FromSeconds(6);
 public static void Write(string file,object value){Directory.CreateDirectory(Root);string p=Path.Combine(Root,file),text=JsonSerializer.Serialize(value,Options);
  for(int i=0;i<12;i++){if(AtomicFiles.TryWrite(p,text))return;Thread.Sleep(25);}
  throw new IOException("控制文件暂时被占用："+file);
 }
 public static async Task Connect(){using var game=Process.GetProcessesByName("BrownDust II").SingleOrDefault()??throw new Exception("请先启动游戏");
  pipe=DesktopFiles.Connect(Root,game.Id,game.StartTime.ToUniversalTime().Ticks);if(HostedConnection.TryOpen(pipe,game.Id,game.StartTime.ToUniversalTime().Ticks)){acquired=true;return;} var managed=Path.Combine(Path.GetDirectoryName(game.MainModule!.FileName)!,"BrownDust II_Data","Managed");
  var hook=await Task.Run(()=>BD2FiendHunter.Compatibility.HookCompiler.Prepare(managed));
  Directory.CreateDirectory(Root);File.WriteAllText(Path.Combine(Root,"compatibility.json"),JsonSerializer.Serialize(hook.Report,Options));
  var payload=hook.Payload;
  using var pe=new PEReader(new MemoryStream(payload));var md=pe.GetMetadataReader();var fingerprint=md.GetGuid(md.GetModuleDefinition().Mvid).ToString("N");
  pipe=DesktopFiles.Connect(Root,game.Id,game.StartTime.ToUniversalTime().Ticks);acquired=false;
  try{if(pipe.Fingerprint()==fingerprint&&Fresh(State())){pipe.Open(fingerprint);acquired=true;return;}}catch(IOException){}catch(TimeoutException){}
  using var injector=new Injector(game.Id);injector.Inject(payload,"BD2FiendHunterRuntimePublic1","Loader","Load");
  var until=DateTime.UtcNow.AddSeconds(35);while(DateTime.UtcNow<until){var b=pipe.Read("runtime.json");if(b!=null){var state=JsonNode.Parse(b)!;if((string?)state["State"]=="error")throw new Exception((string?)state["Error"]);if((string?)state["State"]=="active"){pipe.Open(fingerprint);acquired=true;return;}}await Task.Delay(150);}
  throw new Exception("组件等待当前操作收尾，请稍后重新连接，游戏无需重启。");
 }
 public static async Task Command(string kind,JsonObject? extra=null){Acquire();var s=State();if(!Fresh(s))throw new Exception("游戏连接心跳已过期");var q=extra??new JsonObject();var id=Guid.NewGuid().ToString("N");q["Id"]=id;q["Session"]=s!["Session"]!.GetValue<string>();q["Kind"]=kind;q["ExpiresUtcTicks"]=DateTime.UtcNow.AddSeconds(8).Ticks;var command=Path.Combine(Root,"command.json");if(AtomicFiles.Exists(command))throw new Exception("上一条指令仍在等待，稍后再试");Write("command.json",q);
  for(int i=0;i<45;i++){await Task.Delay(200);var receipt=Path.Combine(Root,"receipt-"+id+".json");if(!AtomicFiles.Exists(receipt))continue;var result=JsonNode.Parse(AtomicFiles.Read(receipt))!;if((string?)result["Status"]!="dispatched")throw new Exception((string?)result["Error"]);return;}
  throw new Exception("游戏操作结果待确认，不自动重复发送");
 }
 public static void Heartbeat(Control c){Acquire();var s=State();if(!Fresh(s))throw new Exception("游戏心跳中断");var session=(string)s!["Session"]!;if(!string.IsNullOrEmpty(c.Session)&&c.Session!=session)throw new Exception("游戏连接已更换，必须重新开始任务");c.Session=session;c.ExpiresUtcTicks=DateTime.UtcNow.AddSeconds(8).Ticks;c.Validate();Write("control.json",c);}
}
#endif
