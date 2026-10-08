using Dustweave;
using System.Text.Json;

static class ParallelCases
{
    public static async Task Run(string output,List<string> cases)
    {
        void Check(bool value,string name){if(!value)throw new Exception(name);cases.Add(name);}
        var clock=DateTimeOffset.UtcNow;
        var runtime=new Fake(()=>clock);
        string root=Path.Combine(output,"parallel");var session=new DailyParallelSession(root,runtime,()=>clock);
        DailyAccount Account(char c,int slot)=>new(slot,"Account "+slot,new string(c,64),"",true,false,"");
        var accounts=new[]{Account('a',1),Account('b',2),Account('c',3)};
        await session.StartAsync(accounts,new(true,2),_=>new());
        async Task Pump(){await session.TickAsync();await Task.Delay(20);await session.TickAsync();}
        await Pump();
        Check(runtime.Started.Count==2,"parallel respects two-slot limit before launching third account");
        var a=session.Current!.Items[0];var b=session.Current.Items[1];var c=session.Current.Items[2];
        Check(c.State=="waiting","excess accounts remain queued");
        session.Control(a.Account.AccountKey,"pause");await Pump();
        Check(session.Current.Items[0].State=="paused"&&session.Current.Items[1].State=="running","pausing one account does not stop its peer");
        Check(runtime.Started.Count==2,"paused game retains its capacity until operator continues or stops it");
        session.Control(a.Account.AccountKey,"resume");await Pump();
        Check(session.Current.Items[0].State=="running","explicit resume continues the same worker job");
        runtime.Set(a.Job,"completed");await Pump();
        Check(runtime.Started.Count==3&&session.Current.Items[2].State=="running","completion automatically fills the freed capacity in account order");
        runtime.Set(b.Job,"failed");await Pump();
        Check(session.Current.Items[2].State=="running","one account failure does not stop another account");
        session.Control("","pause");await Pump();Check(session.Current.Items[2].State=="paused","pause all requests a safe worker pause");
        session.Control("","stop");await Pump();Check(!session.HasWork,"stop all finishes active worker leases");
        var restored=new DailyParallelSession(root,runtime,()=>clock);
        Check(restored.Current!.Items[0].State=="completed","completed account result survives restart");
        await session.StartAsync(accounts,new(true,1),_=>new());await Pump();
        session.Control("","pause");await Pump();
        Check(session.Current!.Items[1].State=="held"&&session.HasWork,"paused waiting items preserve the queue instead of becoming a new runnable plan");
        int count=runtime.Started.Count;var interrupted=new DailyParallelSession(root,runtime,()=>clock);
        await interrupted.TickAsync();Check(runtime.Started.Count==count,"reopening a desktop never silently launches saved unfinished accounts");
        Check(interrupted.Current!.Items[0].State=="interrupted","reopening explains that prior running work needs review");
        session.Control(accounts[0].AccountKey,"stop");await Pump();
        session.Control(accounts[1].AccountKey,"resume");await Pump();
        Check(session.Current.Items[1].State=="running"&&session.Current.Items[2].State=="held","individual resume after pause all runs only that held account");
        session.Control("","stop");await Pump();
        bool rejected=false;try{await session.StartAsync([accounts[0],accounts[0]],new(true,2),_=>new());}catch(InvalidDataException){rejected=true;}
        Check(rejected,"duplicate identities rejected before any sandbox launch");
        rejected=false;try{new DailyParallelOptions(true,5).Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"parallel capacity has a bounded range");
        await session.StartAsync([accounts[0]],new(true,1),_=>new());await Pump();
        var running=session.Current!.Items[0];runtime.Freeze=true;clock=clock.AddSeconds(25);await session.TickAsync();
        Check(session.Current.Items[0].State=="stopping"&&session.Occupied,"stale heartbeat from live worker quarantines capacity and requests stop");
        runtime.Live=false;await session.TickAsync();Check(session.Current.Items[0].State=="interrupted","dead worker is reported without automatic replay");
        var failingRuntime=new Fake(()=>clock){FailAccount=accounts[0].AccountKey};
        var isolatedFailure=new DailyParallelSession(Path.Combine(output,"parallel-launch-failure"),failingRuntime,()=>clock);
        await isolatedFailure.StartAsync(accounts,new(true,1),_=>new());
        for(int n=0;n<10 && isolatedFailure.Current!.Items[1].State!="running";n++){await isolatedFailure.TickAsync();await Task.Delay(20);}
        Check(isolatedFailure.Current!.Items[0].State=="failed"&&isolatedFailure.Current.Items[0].Detail.Contains("归属"),"ownership failure remains visible on its affected account");
        Check(isolatedFailure.Current.Items[1].State=="running"&&isolatedFailure.Current.Items[2].State=="waiting","pre-launch ownership failure frees capacity and advances the next account");
        var lease=new DailyParallelLease("job",accounts[0].AccountKey,clock);
        DailyParallelCommand Command(string action,long sequence)=>new("job",accounts[0].AccountKey,action,sequence,clock);
        lease.Observe(Command("run",0),clock);Check(lease.Authorized&&!lease.Paused,"valid parent lease authorizes execution");
        lease.Paused=true;lease.Observe(Command("run",0),clock);Check(lease.Paused,"unchanged run heartbeat cannot undo a queue-initiated pause");
        lease.Observe(Command("resume",1),clock);Check(!lease.Paused,"new explicit resume releases a paused worker");
        lease.Observe(Command("stop",2),clock);lease.Observe(Command("resume",3),clock);Check(lease.Stopped,"stop cannot be reversed by a later resume");
        var lost=new DailyParallelLease("job",accounts[0].AccountKey,clock);
        lost.Observe(Command("run",0) with {Account=accounts[1].AccountKey},clock.AddSeconds(31));Check(lost.Stopped&&lost.ParentLost&&!lost.Authorized,"wrong-account heartbeat cannot keep an orphan worker active");
        Check(!Directory.EnumerateFiles(root,"*.bd2slot",SearchOption.AllDirectories).Any(),"parallel queue journal contains no exported credentials");
    }
    private sealed class Fake(Func<DateTimeOffset> now):IDailyParallelRuntime
    {
        public readonly List<string> Started=[];private readonly Dictionary<string,DailyParallelStatus> statuses=[];
        public string FailAccount="";
        public bool Freeze,Live=true;
        public Task StartAsync(DailyAccount account,DailyParallelJob job,CancellationToken token){lock(statuses){Started.Add(job.Id);if(account.AccountKey==FailAccount)return Task.FromException(new InvalidDataException("隔离空间归属不一致"));Set(job,"running");}return Task.CompletedTask;}
        public void Set(DailyParallelJob job,string state){lock(statuses)statuses[job.Id]=new(job.Id,job.Account,state,"",now(),123,1);}
        public DailyParallelStatus? Read(DailyParallelJob job){lock(statuses)return statuses.GetValueOrDefault(job.Id);}
        public bool Alive(DailyParallelStatus status)=>Live;
        public bool Busy(DailyParallelJob job)=>Live&&Read(job) is {} s&&DailyParallelSession.Occupies(s.State);
        public void Send(DailyParallelJob job,string action,long sequence)
        {
            lock(statuses){if(Freeze||!statuses.TryGetValue(job.Id,out var s)||!DailyParallelSession.Occupies(s.State))return;Set(job,action=="pause"?"paused":action=="stop"?"stopped":action=="resume"?"running":s.State);}
        }
    }
}
