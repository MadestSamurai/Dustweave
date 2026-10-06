using System.Text;
using Dustweave;
internal static class ActivityLeaseCases
{
 public static void Run(string root,List<string> cases)
 {
  void Check(bool value,string name){if(!value)throw new Exception(name);cases.Add("activity: "+name);}
  var now=DateTime.UtcNow;string path=Path.Combine(root,"activity-tests");
  using var tool=new DailyActivityLease(path,()=>now);
  Check(!tool.Held&&!DailyToolControl.IsOccupied(path),"idle settings window does not own execution");
  using(var daily=DailyToolControl.Acquire(path)){
   bool rejected=false;try{tool.Enter();}catch(InvalidOperationException){rejected=true;}
   Check(rejected&&!tool.Held,"daily rejects simultaneous tool start before commands");
  }
  tool.Enter();Check(tool.Held&&DailyToolControl.IsOccupied(path),"enabled tool owns execution");
  bool blocked=false;try{using var daily=DailyToolControl.Acquire(path);}catch(InvalidOperationException){blocked=true;}
  Check(blocked,"tool rejects daily and second tool");
  now=now.AddMinutes(1);tool.Observe(true,true,true,false);Check(tool.Held,"enabled waiting mode retains lease without commands");
  now=now.AddSeconds(2);tool.Observe(false,true,true,true);Check(tool.Held,"stop waits for pending settlement");
  now=now.AddSeconds(2);tool.Observe(false,true,false,false);Check(tool.Held,"unknown acknowledgement cannot unlock");
  now=now.AddSeconds(1);tool.Observe(false,true,true,false);Check(!tool.Held,"confirmed stop releases while window remains open");
  using(DailyToolControl.Acquire(path))Check(true,"daily starts with idle tool window");
  tool.Enter();tool.Observe(true,false,false,true);Check(!tool.Held,"game exit releases ownership");
  using var first=new DailyActivityLease(path);using var second=new DailyActivityLease(path);int winners=0;
  Parallel.Invoke(()=>{try{first.Enter();Interlocked.Increment(ref winners);}catch(InvalidOperationException){}},()=>{try{second.Enter();Interlocked.Increment(ref winners);}catch(InvalidOperationException){}});
  Check(winners==1,"atomic concurrent start has exactly one owner");first.Dispose();second.Dispose();
  bool Needs(string name,string text)=>DailyActivityRules.RequiresOwnership(name,Encoding.UTF8.GetBytes(text));
  Check(!Needs("control.json","{\"Enabled\":false}"),"stop can always be sent");
  Check(Needs("control.json","{\"Enabled\":true}"),"enabled heartbeat requires ownership");
  Check(!Needs("enabled-until.txt","123456789"),"read-only Sichuan hints do not own automation");
  Check(Needs("run-command.json","{}")&&Needs("layout-request.json","{}"),"single steps and layouts also require ownership");
  Check(!Needs("catalog-request.json","{}"),"read-only catalog refresh does not occupy automation");
  Check(Needs("control.json","{\"Enabled\":true,\"UntilUtcTicks\":0}"),"enabled flag never bypasses ownership through an invalid deadline");
  Check(Needs("control.json","broken"),"malformed command does not bypass ownership");
  Check(!DailyActivityRules.ExecutionPending("fishing: snapshot writer")&&DailyActivityRules.ExecutionPending("fishing: pending fishing response"),"capture IO does not retain execution but network reply does");
 }
}
