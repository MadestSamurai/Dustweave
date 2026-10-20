using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using BD2.LocalIpc;
namespace BD2Territory.Desktop;
public partial class TerritoryWindow
{
 public async Task ConnectionSmokeAsync(string output)
 {
  timer.Stop();await controlQueue;ShowActivated=false;ShowInTaskbar=false;Left=-10000;Show();
  var checks=new List<string>();void Check(bool value,string message){if(!value)throw new Exception(message);checks.Add(message);}
  using var process=Process.GetCurrentProcess();
  var start=process.StartTime.ToUniversalTime().Ticks;
  var pipe=DesktopFiles.Connect(root,process.Id,start);
  int beats=0;double previous=0,maxGap=0;var clock=Stopwatch.StartNew();
  var heartbeat=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(15)};
  heartbeat.Tick+=(_,_)=>{var now=clock.Elapsed.TotalMilliseconds;maxGap=Math.Max(maxGap,now-previous);previous=now;beats++;};heartbeat.Start();
  try
  {
   var retained=new TerritorySnapshot();snapshot=retained;int reads=0;testSnapshotReader=()=>{Interlocked.Increment(ref reads);return TerritoryJson.Read<TerritorySnapshot>(Path.Combine(root,"latest.json"));};
   var first=RefreshAsync();
   await Task.Delay(50);
   Check(!first.IsCompleted,"missing pipe is still waiting in background");
   await RefreshAsync();Check(!first.IsCompleted,"overlapping refresh is skipped");
   connectionEpoch++;await first;Check(ReferenceEquals(snapshot,retained),"older refresh cannot overwrite newer connection state");Check(reads==1,"only one snapshot read was issued");Check(beats>20&&maxGap<700,"dispatcher stays responsive during cold connection timeout");
   Check(File.ReadAllText(TerritoryDiagnostics.PathFor(root)).Contains("TimeoutException"),"cold connection timeout is logged before hook starts");
   connecting=true;var skipped=RefreshAsync();Check(skipped.IsCompleted,"no snapshot polling during connection");connecting=false;
   using(var cancelled=new CancellationTokenSource())
   {
    cancelled.Cancel();bool rejected=false;
    try{await Task.Run(()=>new TerritoryConnection(root).Connect(cancellationToken:cancelled.Token));}catch(OperationCanceledException){rejected=true;}
    Check(rejected,"cancelled connection never reaches process discovery or injection");
    var log=File.ReadAllText(TerritoryDiagnostics.PathFor(root));Check(log.Contains("connect.requested")&&log.Contains("connect.requested.failed"),"early connection stages and failure are recorded");
   }
   await HostedStartSmokeAsync(Check);
   // This peer belongs to this test process. No game process or game command is involved.
   var accepted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
   var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
   var commands=new List<TerritoryControl>();using var cancelPeer=new CancellationTokenSource();
   var peer=Task.Run(async()=>
   {
    try
    {
     while(!cancelPeer.IsCancellationRequested)
     {
      using var server=new NamedPipeServerStream(Wire.Endpoint(process.Id,start),PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
      await server.WaitForConnectionAsync(cancelPeer.Token);
      using var reader=new BinaryReader(new MemoryStream(Wire.ReadFrame(server)));
      reader.ReadInt32();var verb=reader.ReadString();reader.ReadString();reader.ReadString();reader.ReadString();reader.ReadString();var bytes=Wire.ReadBytes(reader);
      if(verb=="open"){Wire.WriteFrame(server,Wire.Encode(w=>{w.Write("ok");w.Write("generation");w.Write("ticket");Wire.Bytes(w,Array.Empty<byte>());}));continue;}
      if(verb!="write")throw new Exception("Unexpected test request: "+verb);
      commands.Add(JsonSerializer.Deserialize<TerritoryControl>(bytes)!);
      if(commands.Count==1){accepted.SetResult();await release.Task.WaitAsync(cancelPeer.Token);}
      Wire.WriteFrame(server,Wire.Encode(w=>{w.Write("ok");w.Write("");w.Write("");Wire.Bytes(w,Array.Empty<byte>());}));
     }
    }catch(OperationCanceledException){}
   });
   await Task.Run(()=>pipe.Open("fixture"));var stopVersion=link.StopVersion;
   var startControl=Task.Run(()=>link.Start(123));await accepted.Task.WaitAsync(TimeSpan.FromSeconds(3));
   var stopWatch=Stopwatch.StartNew();var stopped=StopAsync();
   Check(!link.Enabled&&stopWatch.ElapsedMilliseconds<150,"stop immediately revokes local state while write is blocked");
   var pausedVersion=link.StopVersion;
   int before=beats;await Task.Delay(200);Check(beats>before+3,"dispatcher remains responsive while stop awaits acknowledgement");
   release.SetResult();await Task.WhenAll(startControl,stopped).WaitAsync(TimeSpan.FromSeconds(4));
   Check(link.StopVersion==pausedVersion,"sending a requested stop does not cancel subsequent starts again");
   Check(commands.Count>=2&&commands[0].Enabled&&commands.Skip(1).All(c=>!c.Enabled&&c.UntilUtcTicks==0),"in-flight start cannot leave a renewed lease after stop");
   bool staleStart=false;try{await Task.Run(()=>link.Start(123,expectedStopVersion:stopVersion));}catch(OperationCanceledException){staleStart=true;}Check(staleStart&&!link.Enabled,"queued start cannot outlive a newer stop");
   cancelPeer.Cancel();await peer;
   // A failed start is already reported to the user; opening Layout must not
   // throw that old task's exception forever after connection recovery.
   try{await QueueControl(()=>throw new InvalidOperationException("synthetic failed start"));}catch(InvalidOperationException){}
   await QueueControl(()=>{});
   Check(controlQueue.IsCompletedSuccessfully,"layout barrier recovers after a previously failed start");
   // Layout reads also use the same unavailable pipe without blocking the dispatcher.
   var layout=new LayoutWindow(root,link){ShowActivated=false,ShowInTaskbar=false,Left=-10000};
   int layoutBefore=beats;await layout.ConnectionSmokeAsync();Check(beats>layoutBefore+30,"layout polling and close remain responsive without a pipe server");
   var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
   testConnector=async(_,token)=>{entered.SetResult();await Task.Delay(Timeout.Infinite,token);return "unused";};
   var connection=ConnectAsync();await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));int previousReads=reads;await RefreshAsync();Check(reads==previousReads,"slow connection cannot race snapshot polling");
   var closeWatch=Stopwatch.StartNew();Close();await shutdownTask;await connection;
   Check(closeWatch.ElapsedMilliseconds<2700,"close finishes within deadline without pipe server");
   Check(!link.Enabled&&lifetime.IsCancellationRequested,"closed window cancels connection and cannot renew control");
   Check(maxGap<700,"all connection, stop and layout waits leave dispatcher responsive");
   TerritoryJson.Write(Path.Combine(output,"connection-smoke.json"),new{status="pass",gameRequests=0,injection=false,dispatcherMaximumGapMs=maxGap,dispatcherTicks=beats,assertions=checks});
  }
  finally{heartbeat.Stop();}
 }
 private async Task HostedStartSmokeAsync(Action<bool,string> check)
 {
  using var process=Process.GetCurrentProcess();long start=process.StartTime.ToUniversalTime().Ticks;
  string local=Path.Combine(root,"hosted-start"),record=Path.Combine(local,"connection.json");
  var connection=new TerritoryConnection(local);
  string[] keys={"BD2_DAILY_HOSTED_TOOL","BD2_DAILY_SUITE_OWNER","BD2_DAILY_DATA_ROOT","BD2_DAILY_GAME_PID","BD2_DAILY_GAME_START","BD2_DAILY_TOOL_FINGERPRINT"};
  var previous=keys.ToDictionary(k=>k,Environment.GetEnvironmentVariable);
  using var cancel=new CancellationTokenSource();int mode=0,opens=0,requests=0;
  var peer=Task.Run(async()=>
  {
   try
   {
    while(!cancel.IsCancellationRequested)
    {
     using var server=new NamedPipeServerStream(Wire.Endpoint(process.Id,start),PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
     await server.WaitForConnectionAsync(cancel.Token);
     using var reader=new BinaryReader(new MemoryStream(Wire.ReadFrame(server)));
     reader.ReadInt32();string verb=reader.ReadString();reader.ReadString();reader.ReadString();reader.ReadString();reader.ReadString();Wire.ReadBytes(reader);requests++;
     byte[] value;
     if(verb=="read")value=JsonSerializer.SerializeToUtf8Bytes(new{State="ready",Tool="territory",Owner=mode==1?"another-owner":"fixture-owner",At=DateTime.UtcNow.AddSeconds(mode==2?-10:0).Ticks});
     else if(verb=="info")value=System.Text.Encoding.UTF8.GetBytes(mode==3?"different-build":"fixture");
     else if(verb=="open"){opens++;value=Array.Empty<byte>();}
     else throw new Exception("Hosted start validation sent a game command: "+verb);
     Wire.WriteFrame(server,Wire.Encode(w=>{w.Write("ok");w.Write("generation");w.Write("ticket");Wire.Bytes(w,value);}));
    }
   }catch(OperationCanceledException){}
  });
  try
  {
   Environment.SetEnvironmentVariable(keys[0],"territory");Environment.SetEnvironmentVariable(keys[1],"fixture-owner");
   Environment.SetEnvironmentVariable(keys[2],Path.Combine(local,"host"));Environment.SetEnvironmentVariable(keys[3],process.Id.ToString());
   Environment.SetEnvironmentVariable(keys[4],start.ToString());Environment.SetEnvironmentVariable(keys[5],"fixture");
   await Task.Run(()=>connection.ValidateStart(process.Id,process.Id,start));
   check(!File.Exists(record)&&DesktopFiles.HasLease(local)&&opens==1,"hosted start accepts the live owned session without a standalone receipt");
   TerritoryJson.Write(record,new TerritoryConnectionState{ProcessId=process.Id,ProcessStartTicks=start-1});
   await Task.Run(()=>connection.ValidateStart(process.Id,process.Id,start));
   check(opens==2,"old standalone receipt cannot invalidate a current hosted session");
   async Task Rejected(Action validate,string message)
   {
    int before=opens;bool rejected=false;try{await Task.Run(validate);}catch(InvalidOperationException){rejected=true;}
    check(rejected&&opens==before,message);
   }
   for(int invalid=1;invalid<=3;invalid++)
   {
    mode=invalid;
    await Rejected(()=>connection.ValidateStart(process.Id,process.Id,start),"hosted start rejects "+new[]{"","changed owner","expired heartbeat","different component"}[invalid]);
   }
   mode=0;int count=requests;
   await Rejected(()=>connection.ValidateStart(process.Id+1,process.Id,start),"stale observed process cannot start");
   await Rejected(()=>connection.ValidateStart(process.Id,process.Id,start-1),"reused PID with a different start time cannot start");
   check(requests==count,"wrong process is rejected before any pipe request");
   Environment.SetEnvironmentVariable(keys[0],null);
   await Rejected(()=>connection.ValidateStart(process.Id,process.Id,start),"standalone launch still rejects an outdated receipt");
   File.Delete(record);
   await Rejected(()=>connection.ValidateStart(process.Id,process.Id,start),"standalone launch still requires its own connection");
   TerritoryJson.Write(record,new TerritoryConnectionState{ProcessId=process.Id,ProcessStartTicks=start});
   await Task.Run(()=>connection.ValidateStart(process.Id,process.Id,start));
   check(requests==count,"valid standalone receipt keeps the existing start path");
  }
  finally
  {
   foreach(var key in keys)Environment.SetEnvironmentVariable(key,previous[key]);
   cancel.Cancel();await peer;
  }
 }
}

public partial class LayoutWindow {
 internal async Task ConnectionSmokeAsync(){timer.Stop();Show();while(polling)await Task.Delay(20);await RefreshAsync();Close();await shutdownTask;}
}
