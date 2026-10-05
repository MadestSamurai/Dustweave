using System.Text.Json.Nodes;
namespace BD2SecretVision;
public interface IGamePort {Task<Snapshot> Read(CancellationToken token);Task<string> Send(string kind,object? fields,CancellationToken token);Task<string> Execute(Point start,Point[] corners,bool drawing,CancellationToken token);void Log(string name,object value);}
public sealed class GamePort:IGamePort {
 readonly Connection connection;readonly GameProcess game;readonly string session,owner;readonly string output;
 public string Root=>connection.Root;
 public GamePort(Connection c,Snapshot s,string owner,string output){connection=c;game=Connection.Find()??throw new InvalidOperationException("game-not-running");session=s.Session;this.owner=owner;this.output=output;}
 public async Task<Snapshot> Read(CancellationToken token){var deadline=DateTimeOffset.UtcNow.AddSeconds(6);while(true){token.ThrowIfCancellationRequested();var s=connection.Read();if(Connection.Fresh(s,game,DateTimeOffset.UtcNow)){if(s!.Session!=session)throw new InvalidOperationException("session-changed");return s;}if(DateTimeOffset.UtcNow>=deadline)throw new InvalidOperationException("component-heartbeat-missing");await Task.Delay(100,token);}}
 public void Log(string name,object value)=>Files.Note(Path.Combine(output,name+".jsonl"),value);
 public async Task<string> Send(string kind,object? fields,CancellationToken token){
 var s=await Read(token);var q=fields==null?new JsonObject():System.Text.Json.JsonSerializer.SerializeToNode(fields)!.AsObject();string id=Guid.NewGuid().ToString("N");
 q["Id"]=id;q["Kind"]=kind;q["Session"]=session;q["Owner"]=owner;q["ExpiresUtc"]=DateTimeOffset.UtcNow.AddSeconds(8).ToString("O");
 var path=Path.Combine(Root,"command.json");token.ThrowIfCancellationRequested();if(!BD2.LocalIpc.DesktopFiles.Write(path,System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(q,Files.Options),true))throw new IOException("component-not-connected");
 var until=DateTimeOffset.UtcNow.AddSeconds(10);
 while(DateTimeOffset.UtcNow<until){await Read(token);var r=Files.Read<JsonNode>(Path.Combine(Root,"receipt-"+id+".json"));if(r!=null){Log("receipts",r);if(r.S("Status")!="dispatched"&&!(kind=="route"&&r.S("Status")=="deferred"))throw new InvalidOperationException("command-rejected: "+r.S("Error"));return id;}await Task.Delay(100,token);}
 throw new InvalidOperationException("command-result-unknown");
 }
 public async Task<string> Execute(Point start,Point[] corners,bool drawing,CancellationToken token){
 var s=await Read(token);var b=s.Board;if(b==null||b.Finished)return "ended";if(b.State!="Playing")throw new RouteFailure("game-"+b.State);if(!drawing&&(b.Player.Drawing||b.Player.Closing))throw new RouteFailure("unfinished-closure");
 Point[] actual,walk;try{actual=drawing?Planning.Trim(b,start,corners):[];walk=Planning.SafePath(b,start);}catch(RouteFailure){return "replan-topology";}
 if(walk.Length==0&&!drawing)return "completed";
 var nodes=walk.Select(p=>new Node(p.X,p.Y)).Concat(actual.Select((p,i)=>new Node(p.X,p.Y,true,i==0?1:0))).ToArray();
 Log("routes",new{At=s.AtUtc,BoardId=b.Id,Drawing=drawing,Start=start,Corners=actual,Nodes=nodes});
 var id=await Send("route",new{BoardId=b.Id,Route=nodes,Guard=true},token);
 var r=Files.Read<JsonNode>(Path.Combine(Root,"receipt-"+id+".json"))!;if(r.S("Status")=="deferred"){await Task.Delay(100,token);return "deferred";}var receiptAt=DateTimeOffset.Parse(r.S("AtUtc"));var deadline=DateTimeOffset.UtcNow.AddSeconds(40);
 while(true){await Task.Delay(100,token);s=await Read(token);b=s.Board;if(s.AtUtc<=receiptAt)continue;if(b==null||b.Finished)return "ended";
 if(!s.Running){if(s.Reason=="route-completed")return "completed";if(s.Reason is "replan-window" or "replan-topology" or "escape-completed")return s.Reason;if(s.Reason is "lease/stop" or "requested")throw new OperationCanceledException();throw new RouteFailure(s.Reason);}
 if(DateTimeOffset.UtcNow>=deadline)throw new RouteFailure("route-timeout");
 }
 }
}
