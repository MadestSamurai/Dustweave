using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using BD2.LocalIpc;
namespace BD2ApostleDefense.Desktop;
public sealed class ConnectionSmokePort:IClientPort
{
 private readonly string root;public int Reads;public TaskCompletionSource Connecting=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public ConnectionSmokePort(string root){this.root=root;DesktopFiles.Configure(root,Identity.LiveEntries);}
 public GameProcess? Find(){using var p=Process.GetCurrentProcess();return new(p.Id,p.StartTime.ToUniversalTime().Ticks,"test-only");}
 public Snapshot? Read(){Interlocked.Increment(ref Reads);return JsonFiles.Read<Snapshot>(Path.Combine(root,"snapshot.json"));}
 public void Write(Control c)=>JsonFiles.Write(Path.Combine(root,"control.json"),c);
 public async Task ConnectAsync(Action<string> p,CancellationToken t){Connecting.TrySetResult();await Task.Delay(Timeout.Infinite,t);}
}
public partial class MainWindow
{
 public async Task ConnectionSmoke(string output)
 {
  timer.Stop();ShowActivated=false;ShowInTaskbar=false;Left=-10000;Show();
  var checks=new List<string>();void Check(bool value,string message){if(!value)throw new Exception(message);checks.Add(message);}
  using var process=Process.GetCurrentProcess();var start=process.StartTime.ToUniversalTime().Ticks;
  DesktopFiles.Connect(root,process.Id,start);var testPort=(ConnectionSmokePort)port;
  int beats=0;double previous=0,maxGap=0;var clock=Stopwatch.StartNew();
  var heartbeat=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(15)};
  heartbeat.Tick+=(_,_)=>{var now=clock.Elapsed.TotalMilliseconds;maxGap=Math.Max(maxGap,now-previous);previous=now;beats++;};heartbeat.Start();
  try
  {
   var retained=DemoPort.Demo();snapshot=retained;var first=Refresh();await Task.Delay(50);connectionEpoch++;
   Check(!first.IsCompleted,"missing pipe waits on worker");await Refresh();Check(testPort.Reads==1,"refresh never overlaps");
   await first;Check(ReferenceEquals(snapshot,retained),"old refresh cannot overwrite a newer connection epoch");Check(beats>30&&maxGap<700,"UI remains responsive during missing pipe timeout");
   Check(File.ReadAllText(ConnectionDiagnostics.PathFor(root)).Contains("TimeoutException"),"missing pipe timeout logged before hook starts");
   connecting=true;int reads=testPort.Reads;await Refresh();Check(testPort.Reads==reads,"connection suppresses polling");connecting=false;
   using(var cancelled=new CancellationTokenSource())
   {
    cancelled.Cancel();bool rejected=false;
    try{await new GamePort(root).ConnectAsync(_=>{},cancelled.Token);}catch(OperationCanceledException){rejected=true;}
    Check(rejected,"cancelled connect never injects");var log=File.ReadAllText(ConnectionDiagnostics.PathFor(root));
    Check(log.Contains("connect.requested")&&log.Contains("connect.requested.failed"),"early connection failure has diagnostics");
   }
   var accepted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
   var commands=new List<Control>();using var cancelPeer=new CancellationTokenSource();
   var peer=Task.Run(async()=>
   {
    try{while(!cancelPeer.IsCancellationRequested)
    {
     using var server=new NamedPipeServerStream(Wire.Endpoint(process.Id,start),PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
     await server.WaitForConnectionAsync(cancelPeer.Token);using var reader=new BinaryReader(new MemoryStream(Wire.ReadFrame(server)));
     reader.ReadInt32();var verb=reader.ReadString();reader.ReadString();reader.ReadString();reader.ReadString();reader.ReadString();var bytes=Wire.ReadBytes(reader);
     if(verb!="write")throw new Exception("Unexpected test request: "+verb);commands.Add(JsonSerializer.Deserialize<Control>(bytes,JsonFiles.Options)!);
     if(commands.Count==1){accepted.SetResult();await release.Task.WaitAsync(cancelPeer.Token);}
     Wire.WriteFrame(server,Wire.Encode(w=>{w.Write("ok");w.Write("");w.Write("");Wire.Bytes(w,Array.Empty<byte>());}));
    }}catch(OperationCanceledException){}
   });
   var state=DemoPort.Demo();state.ProcessId=process.Id;state.ProcessStart=start;state.At=DateTime.UtcNow.Ticks;
   var startControl=Task.Run(()=>controller.Start(state,new(),state.At));await accepted.Task.WaitAsync(TimeSpan.FromSeconds(3));
   var stoppedVersion=controller.StopVersion;var stopWatch=Stopwatch.StartNew();var stopped=PauseAsync();
   Check(!controller.Running&&stopWatch.ElapsedMilliseconds<150,"pause immediately revokes local state during blocked write");
   int before=beats;await Task.Delay(200);Check(beats>before+3,"UI remains responsive awaiting stop receipt");
   release.SetResult();await Task.WhenAll(startControl,stopped).WaitAsync(TimeSpan.FromSeconds(4));
   Check(commands.Count>=2&&commands[0].Enabled&&commands.Skip(1).All(c=>!c.Enabled&&c.Expires==0),"late renewal cannot reenable after pause");
   bool cancelledStart=false;try{await Task.Run(()=>controller.Start(state,new(),DateTime.UtcNow.Ticks,stoppedVersion));}catch(OperationCanceledException){cancelledStart=true;}
   Check(cancelledStart&&!controller.Running,"queued start cancelled by newer pause");
   cancelPeer.Cancel();await peer;
   var connection=ConnectAsync();await testPort.Connecting.Task.WaitAsync(TimeSpan.FromSeconds(3));reads=testPort.Reads;
   await Refresh();Check(testPort.Reads==reads,"slow connection stays isolated from refresh");
   var closeWatch=Stopwatch.StartNew();Close();await shutdownTask;await connection;
   Check(closeWatch.ElapsedMilliseconds<2700,"close bounded without server");Check(!controller.Running&&lifetime.IsCancellationRequested,"close cancels pending connection and control");
   JsonFiles.Write(Path.Combine(output,"connection-smoke.json"),new{status="passed",gameRequests=0,injection=false,dispatcherMaximumGapMs=maxGap,dispatcherTicks=beats,checks});
  }
  finally{heartbeat.Stop();}
 }
}
