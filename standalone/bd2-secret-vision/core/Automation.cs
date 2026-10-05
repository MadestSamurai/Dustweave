using System.Text.Json.Nodes;
namespace BD2SecretVision;
public static class ResultRules {
 public static readonly TimeSpan SuccessDisplayDuration=TimeSpan.FromSeconds(8);
 public static bool Confirmed(JsonNode? server,JsonNode? end,int stage,string session,string round,DateTimeOffset started,int target){
 if(server==null||end==null||server.S("Session")!=session||end.S("Session")!=session||server.S("Round")!=round||end.S("Round")!=round||end.D("Stage")!=stage)return false;
 if(!DateTimeOffset.TryParse(server.S("AtUtc"),out var at)||at<=started||!DateTimeOffset.TryParse(end.S("AtUtc"),out var endedAt)||endedAt<=started)return false;
 JsonNode? data;try{data=server["Data"] is JsonValue?JsonNode.Parse(server.S("Data")):server["Data"];}catch{return false;}
 return data.D("stageId")==stage&&data.B("isClear")&&end.D("Percent")>=target&&(target<100||!end.B("Continued"));
 }
 public static bool LocalSuccess(JsonNode? end,string session,string round)=>end?.S("Session")==session&&end.S("Round")==round&&end.D("Percent")>=80;
}
public static class AttemptRules {
 public static bool Ready(Snapshot s,string round,DateTimeOffset started)=>s.Round==round&&s.AtUtc>started&&s.Board?.State=="Playing";

 public static bool IsControlFault(string reason)=>reason is "game-Paused" or "resume-timeout" or "board changed" || reason.StartsWith("game-",StringComparison.Ordinal);
}
public sealed class Automation(Connection connection,
 Func<GameProcess?>? findGame=null,
 Func<Snapshot,string,string,IGamePort>? createPort=null,
 Func<IGamePort,CancellationToken,Task>? runPlan=null,
 Func<TimeSpan,CancellationToken,Task>? successDelay=null){
 public Progress Progress{get;private set;}=new();public event Action<Progress>? Changed;
 public async Task Run(Settings settings,CancellationToken token){
 settings.Validate();Directory.CreateDirectory(connection.Root);using var exclusive=new FileStream(Path.Combine(connection.Root,"runner.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
 var game=(findGame??Connection.Find)()??throw new InvalidOperationException("game-not-running");var initial=connection.Read();if(!Connection.Fresh(initial,game,DateTimeOffset.UtcNow))throw new InvalidOperationException("component-heartbeat-missing");
 if(initial!.Board==null&&!initial.UIs.Any(u=>u.Type=="HopscotchMainUI"))throw new InvalidOperationException("open-secret-vision");
 string owner=Guid.NewGuid().ToString("N"),session=initial.Session,root=Path.Combine(connection.Root,"runs",DateTime.UtcNow.ToString("yyyyMMddTHHmmss")+"-"+owner[..6]);
 Directory.CreateDirectory(root);Progress=new(){Status="running",Output=root};IGamePort port=createPort?.Invoke(initial,owner,root)??new GamePort(connection,initial,owner,root);
 void Diagnostic(string phase,Exception error){Files.Diagnose(root,phase,error);Files.Diagnose(connection.Root,phase,error);}
 void SaveEvidence(string path,object value){try{Files.Write(path,value);}catch(Exception error)when(Files.IsFileError(error)){Progress.Warning=error.ToString();Diagnostic("evidence-write",error);}}
 void Publish(){var s=connection.Read();if(s?.Session==session&&s.Board!=null){Progress.Percent=s.Board.Percent;Progress.Remaining=s.Board.Remaining;}SaveEvidence(Path.Combine(root,"progress.json"),Progress);SaveEvidence(Path.Combine(connection.Root,"progress.json"),Progress);Changed?.Invoke(System.Text.Json.JsonSerializer.Deserialize<Progress>(System.Text.Json.JsonSerializer.Serialize(Progress,Files.Options),Files.Options)!);}
 void Lease(bool enabled,DateTimeOffset expiry)=>Files.Write(Path.Combine(connection.Root,"control.json"),new{Enabled=enabled,Owner=owner,Session=session,Pid=game.Pid,ProcessStart=game.Start,ExpiresUtc=expiry.ToString("O")});
 using var heartbeat=new ControlHeartbeat(expiry=>Lease(true,expiry),()=>Lease(false,DateTimeOffset.UtcNow.AddSeconds(-1)),Diagnostic);
 using var runCancel=CancellationTokenSource.CreateLinkedTokenSource(token,heartbeat.FailureToken);
 var runToken=runCancel.Token;
 try{
 heartbeat.Start();
 foreach(int stage in settings.Stages){Progress.Stage=stage;
 for(int attempt=1;;attempt++){runToken.ThrowIfCancellationRequested();Progress.Attempt=attempt;Progress.Status="starting";Progress.Detail="";Publish();
 using var watchdog=CancellationTokenSource.CreateLinkedTokenSource(runToken);watchdog.CancelAfter(TimeSpan.FromMinutes(7));var ct=watchdog.Token;
 var folder=Path.Combine(root,$"stage-{stage}-attempt-{attempt:000}");Directory.CreateDirectory(folder);
 var before=await port.Read(ct);if(before.Board!=null)await port.Send("exit",null,ct);
 await Wait(s=>s.Board==null&&s.UIs.Any(u=>u.Type=="HopscotchMainUI"),25,port,ct,"home-not-ready");
 var started=DateTimeOffset.UtcNow;string round=await port.Send("start",new{StageIndex=stage-1},ct);
 var ready=await Wait(s=>AttemptRules.Ready(s,round,started),30,port,ct,"new-board-not-ready");SaveEvidence(Path.Combine(folder,"initial.json"),ready);
 Progress.Status="playing";Publish();string? routeError=null;
 try{if(runPlan==null)await new AdaptivePlanner(port).Run(ct);else await runPlan(port,ct);}catch(RouteFailure e){if(AttemptRules.IsControlFault(e.Message))throw new InvalidOperationException("control-flow-interrupted: "+e.Message,e);routeError=e.Message;await port.Send("stop",null,ct);}
 var final=await port.Read(ct);SaveEvidence(Path.Combine(folder,"final.json"),final);
 JsonNode? end=null,server=null;bool clear=false;var until=DateTimeOffset.UtcNow.AddSeconds(final.Board?.State=="End"?15:1);
 do{ct.ThrowIfCancellationRequested();end=Files.Read<JsonNode>(Path.Combine(connection.Root,"end.json"));server=Files.Read<JsonNode>(Path.Combine(connection.Root,"server-stage.json"));clear=ResultRules.Confirmed(server,end,stage,session,round,started,settings.TargetPercent);if(clear)break;await Task.Delay(200,ct);}while(DateTimeOffset.UtcNow<until);
 SaveEvidence(Path.Combine(folder,"result.json"),new{Stage=stage,Attempt=attempt,Round=round,Started=started,Finished=DateTimeOffset.UtcNow,Clear=clear,End=end,Server=server,Error=routeError});
 if(clear){
 Progress.Cleared.Add(stage);Publish();
 // Keep the confirmed result visible before advancing or returning home.
 // Use the run token so Stop remains immediate even after a successful round.
 await (successDelay?.Invoke(ResultRules.SuccessDisplayDuration,runToken)??Task.Delay(ResultRules.SuccessDisplayDuration,runToken));
 runToken.ThrowIfCancellationRequested();break;
 }
 if((ResultRules.LocalSuccess(end,session,round)||final.Board is {State:"End",Percent:>=80})&&!ResultRules.Confirmed(server,end,stage,session,round,started,80))throw new InvalidOperationException("awaiting-server-confirmation");
 if(!settings.Retry||settings.MaxAttempts>0&&attempt>=settings.MaxAttempts)break;
 Progress.Status="retrying";Progress.Detail=routeError??"attempt-not-cleared";Publish();await Task.Delay(1500,ct);
 }
 }
 Progress.Status=Progress.Cleared.Count==settings.Stages.Length?"completed":"attempted";
 // Return using the native game exit; never open another round after completion.
 var finished=await port.Read(runToken);if(finished.Board!=null){await port.Send("exit",null,runToken);await Wait(s=>s.Board==null,25,port,runToken,"home-not-ready");}
 }catch(OperationCanceledException){
 if(heartbeat.Failure is Exception failure){Progress.Status="blocked";Progress.Detail=failure.ToString();Diagnostic("run-heartbeat",failure);}
 else {Progress.Status=token.IsCancellationRequested?"stopped":"blocked";Progress.Detail=token.IsCancellationRequested?"manual-stop":"attempt-timeout";}
 }
 catch(Exception error){Progress.Status="blocked";Progress.Detail=error.ToString();Diagnostic("run",error);}
 finally{
 var revokeError=await heartbeat.Stop();
 if(heartbeat.Failure is Exception failure&&Progress.Status!="blocked"&&!token.IsCancellationRequested){Progress.Status="blocked";Progress.Detail=failure.ToString();}
 if(revokeError!=null)Progress.Warning=revokeError.ToString();
 Publish();
 }
 }
 static async Task<Snapshot> Wait(Func<Snapshot,bool> predicate,int seconds,IGamePort port,CancellationToken ct,string reason){var end=DateTimeOffset.UtcNow.AddSeconds(seconds);while(true){var s=await port.Read(ct);if(predicate(s))return s;if(DateTimeOffset.UtcNow>=end)throw new InvalidOperationException(reason);await Task.Delay(150,ct);}}
}
