namespace BD2SecretVision;
public sealed class AdaptivePlanner(IGamePort port) {
 async Task<Board?> Board(CancellationToken ct){var b=(await port.Read(ct)).Board;if(b==null||b.Finished)return null;if(b.State!="Playing")throw new InvalidOperationException("control-flow-interrupted: game-"+b.State);return b;}
 public async Task Run(CancellationToken ct){
  var b=await Board(ct);if(b==null)return;
  if(!b.HasTopology)throw new InvalidOperationException("component-topology-missing: connect the current component");
  Topology.Validate(b);
  // Every action starts from the current graph. No phase/u/pos cursor survives destruction.
  var excluded=new Dictionary<string,DateTimeOffset>();string topology="";int steps=0;bool allowPickup=true;
  while(true){ct.ThrowIfCancellationRequested();var snapshot=await port.Read(ct);b=snapshot.Board;if(b==null||b.Finished)return;if(b.State!="Playing")throw new InvalidOperationException("control-flow-interrupted: game-"+b.State);if(snapshot.Running){await Task.Delay(80,ct);continue;}
   if(b.Player.Drawing||b.Player.Closing){await Task.Delay(80,ct);continue;}
   if(b.TopologyKey!=topology){excluded.Clear();topology=b.TopologyKey;}
   var now=DateTimeOffset.UtcNow;var plan=RepairPlanning.Choose(b,key=>excluded.TryGetValue(key,out var until)&&until>now,allowPickup);
   if(plan==null){await Task.Delay(150,ct);continue;}
   port.Log("decisions",new{Label=plan.Purpose,Step=++steps,Topology=b.TopologyKey,Plan=plan});
   port.Log("planning-state",new{Step=steps,At=snapshot.AtUtc,Board=b});
   var before=b;string result=await port.Execute(plan.Start,plan.Corners,true,ct);if(result=="ended")return;
   b=await Board(ct);if(b==null)return;
   int gained=0,lost=0;for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++){if(!before.Claimed(x,y)&&b.Claimed(x,y))gained++;if(before.Claimed(x,y)&&!b.Claimed(x,y))lost++;}
   bool changed=before.TopologyKey!=b.TopologyKey;
   port.Log("repair-result",new{Step=steps,Result=result,Predicted=plan.Cells,Gained=gained,Lost=lost,TopologyChanged=changed,Before=before.TopologyKey,After=b.TopologyKey});
   if(result=="completed")allowPickup=plan.Purpose!="pickup";
   if(!changed&&result is "completed" or "replan-topology"){excluded[plan.Key]=result=="completed"?DateTimeOffset.MaxValue:DateTimeOffset.UtcNow.AddMilliseconds(700);}
   if(result=="replan-window")await Task.Delay(120,ct);
  }
 }
}
