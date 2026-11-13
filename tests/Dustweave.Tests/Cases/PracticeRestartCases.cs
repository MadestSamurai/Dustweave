using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
internal static class PracticeRestartCases
{
    sealed class Host(string mode):IGameHost
    {
        public DateTimeOffset Now=DateTimeOffset.UtcNow;
        public GameInstance? Game=new(7,10,"C:/fixture/BrownDust II.exe");
        public int Closes,Launches,Starts,Reads;
        public StartupPermit? Permit;
        public CancellationTokenSource Cancel=new();
        public GameInstance? Find()=>Game;
        public Task CloseAsync(GameInstance game,CancellationToken token){Closes++;if(mode!="close-failed")Game=null;return Task.CompletedTask;}
        public void Launch(){Launches++;if(mode!="missing")Game=new(8,20,"C:/fixture/BrownDust II.exe");}
        public Task ConnectAsync(GameInstance game,Action<string> report,CancellationToken token)=>Task.CompletedTask;
        public DailyRuntimeStatus? ReadStatus()=>null;
        public DailySnapshot? ReadSnapshot()
        {
            Reads++;
            if(Game==null)return null;
            var s=new DailySnapshot{Runtime=DailyIdentity.RuntimeName,ProcessId=Game.ProcessId,ProcessStartTicks=Game.StartTicks,
                InstanceId="instance",AccountKey=new string('a',64),PlayerKey=new string('b',64),State="identified",FrameUtcTicks=Now.UtcTicks,
                Sequence=Reads,Capabilities=["account.identity"],Guild=new(){ServerKey="test",CycleKey="today"},Startup=new(){Supported=true}};
            if(Launches>0)
            {
                if(mode=="read-gap"&&Reads<8)return null;
                if(mode=="account")s.AccountKey=new string('c',64);
                if(mode=="player")s.PlayerKey=new string('c',64);
                if(mode=="cycle")s.Guild.CycleKey="tomorrow";
                if(Starts==0||mode is "second-title" or "login-failed" or "stuck")
                {
                    s.State="waiting_start";s.Startup=new(){Supported=true,Visible=true,TitleInstanceId=mode=="second-title"&&Starts>0?92:91,
                        Attempted=Starts>0,Ready=true,ClickEnabled=true};
                    if(mode=="login-failed"&&Starts>0)s.Startup.BlockReason="请先处理启动页弹窗：IntroMessagePopupUI";
                    if(mode=="terms")s.Startup.BlockReason="请先处理启动页弹窗：IntroMessagePopupUI";
                    if(mode=="download")s.Startup.DownloadVisible=true;
                }
            }
            return s;
        }
        public void Grant(StartupPermit? p){Permit=p;if(p!=null){if(!p.ResumeOnly)throw new Exception("unrestricted permit");if(Starts==0)Starts++;if(mode=="cancel")Cancel.Cancel();}}
        public Task Delay(TimeSpan t){Now+=t;return Task.CompletedTask;}
        public Task Run()=>DailyPracticeRestart.RunAsync(CommandDriverCases.Context(),this,Launch,_=>{},Grant,Cancel.Token,()=>Now,Delay);
    }
    public static async Task Run(string root,List<string> cases)
    {
        void Check(bool ok,string name){if(!ok)throw new Exception(name);cases.Add("practice restart: "+name);}
        long time=0;var clock=new DailyPracticeRestartClock(()=>time);
        time=100000000;Check(!clock.Due,"disabled by default");
        clock.Minutes=15;time+=899999;Check(!clock.Due,"not before selected interval");time++;
        Check(clock.Due,"due at selected interval");clock.Checkpoint(true);Check(clock.Due,"pending settlement defers restart");
        bool due=false;try{clock.Checkpoint(false);}catch(DailyPracticeRestartDue){due=true;}Check(due,"clean checkpoint requests restart");
        clock.Reset();Check(!clock.Due,"successful restart begins fresh interval");clock.Minutes=30;time+=900000;Check(!clock.Due,"changed interval takes effect without old deadline");
        clock.Minutes=0;time+=99999999;Check(!clock.Due,"can disable while running");clock.Minutes=-1;Check(clock.Minutes==0,"unknown saved value is off");
        string settings=Path.Combine(root,"practice-settings.json");DailyJson.Write(settings,new DailyPracticeSettings(60));
        Check(DailyJson.TryRead<DailyPracticeSettings>(settings)?.RestartMinutes==60,"interval persists independently of jobs");
        foreach(string mode in new[]{"ok","read-gap"})
        {
            var h=new Host(mode);await h.Run();Check(h.Closes==1&&h.Launches==1&&h.Starts==1&&h.Permit==null,mode+" one process restart and one login");
        }
        foreach(string mode in new[]{"close-failed","missing","account","player","cycle","second-title","login-failed","stuck","terms","download","cancel"})
        {
            var h=new Host(mode);bool failed=false;try{await h.Run();}catch(Exception e)when(e is StageHostException or OperationCanceledException){failed=true;}
            Check(failed&&h.Closes==1&&h.Launches<=1&&h.Starts<=1&&h.Permit==null,mode+" stops without recursive login/restart");
            if(mode=="close-failed")Check(h.Launches==0,"failed normal close never launches another game");
        }
        {
            var h=new Host("ok");h.Game=h.Game! with{ProcessId=99};bool failed=false;
            try{await h.Run();}catch(StageHostException){failed=true;}
            Check(failed&&h.Closes==0&&h.Launches==0,"replacement process remains untouched");
        }
    }
}
