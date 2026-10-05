using System.Diagnostics;using BD2SecretVision.Compatibility;using SharpMonoInjector;
namespace BD2SecretVision;
public sealed record GameProcess(int Pid,long Start,string File);
public sealed class Connection {
 public string Root{get;}public Connection(string? root=null){Root=root??Files.Root;BD2.LocalIpc.DesktopFiles.Configure(Root,Files.LiveEntries);}
 public static GameProcess? Find(){var games=Process.GetProcessesByName("BrownDust II");try{if(games.Length>1)throw new InvalidOperationException("multiple-games");if(games.Length==0)return null;var p=games[0];return new(p.Id,p.StartTime.ToUniversalTime().Ticks,p.MainModule!.FileName);}finally{foreach(var p in games)p.Dispose();}}
 public Snapshot? Read()=>Files.Read<Snapshot>(Path.Combine(Root,"state.json"));
 public static bool Fresh(Snapshot? s,GameProcess? g,DateTimeOffset now)=>s!=null&&g!=null&&s.Protocol==1&&s.Pid==g.Pid&&s.ProcessStart==g.Start&&s.AtUtc>now.AddSeconds(-6)&&s.AtUtc<=now.AddSeconds(2);
 public sealed class Record {public int Pid{get;set;}public long Start{get;set;}public string Fingerprint{get;set;}="";public string State{get;set;}="";public long Address{get;set;}public string Error{get;set;}="";}
 public async Task Connect(Action<string> report,CancellationToken token){
 var game=Find()??throw new InvalidOperationException("game-not-running");var recordPath=Path.Combine(Root,"connection.json");var pipe=BD2.LocalIpc.DesktopFiles.Connect(Root,game.Pid,game.Start);if(BD2.LocalIpc.HostedConnection.TryOpen(pipe,game.Pid,game.Start))return;
 try{if(pipe.Fingerprint()==HookCompiler.Fingerprint&&Fresh(Read(),game,DateTimeOffset.UtcNow)){pipe.Open(HookCompiler.Fingerprint);return;}}
 catch(BD2.LocalIpc.LeaseRevokedException){}catch(TimeoutException){}catch(IOException){}
 report("adapting");var managed=Path.Combine(Path.GetDirectoryName(game.File)!,"BrownDust II_Data","Managed");PreparedHook hook;
 try{hook=await Task.Run(()=>HookCompiler.Prepare(managed),token);Files.Write(Path.Combine(Root,"compatibility.json"),hook.Report);}catch(Exception e){Files.Write(Path.Combine(Root,"compatibility-error.json"),new{At=DateTimeOffset.UtcNow,Error=e.Message});throw;}
 token.ThrowIfCancellationRequested();if(Find()!=game)throw new InvalidOperationException("game-changed");
 report("connecting");var current=new Record{Pid=game.Pid,Start=game.Start,Fingerprint=HookCompiler.Fingerprint,State="attaching"};
 // Opening the process before writing the marker keeps access-denied retries safe.
 await Task.Run(()=>{using var injector=new Injector(game.Pid);Files.Write(recordPath,current);try{current.Address=injector.Inject(hook.Payload,"BD2SecretVision.Runtime","Loader","Load").ToInt64();current.State="attached";Files.Write(recordPath,current);}catch(Exception e){current.Error=e.Message;current.State="failed";Files.Write(recordPath,current);throw;}},CancellationToken.None);
 var until=DateTimeOffset.UtcNow.AddSeconds(35);
 while(DateTimeOffset.UtcNow<until){token.ThrowIfCancellationRequested();if(Fresh(Read(),game,DateTimeOffset.UtcNow)){pipe.Open(HookCompiler.Fingerprint);report("connected");return;}var error=Files.Read<System.Text.Json.Nodes.JsonNode>(Path.Combine(Root,"error.json"));if(error!=null&&DateTimeOffset.TryParse(error.S("AtUtc"),out var at)&&at>DateTimeOffset.UtcNow.AddSeconds(-20))throw new InvalidOperationException(error.S("Error"));await Task.Delay(200,token);}
 throw new InvalidOperationException("component-heartbeat-missing");
 }
}
