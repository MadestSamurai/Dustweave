using System.Text.Json;
using System.Text.Json.Nodes;
using Dustweave.Accounts;
using static Dustweave.DailyData;
namespace Dustweave;

public sealed record DailyPracticeSettings(int RestartMinutes = 0);
public sealed class DailyPracticeRestartDue : Exception;

// Monotonic, account-scoped opt-in. Changing the interval starts a new interval.
public sealed class DailyPracticeRestartClock(Func<long>? clock = null)
{
    public static readonly int[] Choices = [0, 15, 30, 60, 120];
    readonly Func<long> now = clock ?? (() => Environment.TickCount64);
    readonly object sync = new();
    int minutes;
    long since;
    public int Minutes { get { lock(sync)return minutes; } set { lock(sync){ minutes=Choices.Contains(value)?value:0; since=now(); } } }
    public bool Due { get { lock(sync)return minutes>0 && now()-since>=minutes*60000L; } }
    public void Reset() { lock(sync)since=now(); }
    public void Checkpoint(bool pending) { if(!pending&&Due)throw new DailyPracticeRestartDue(); }
}

public static class DailyPracticeRestart
{
    const string Manual="定时重启未完成，练习进度已保留。请在游戏中确认登录状态后继续练习；不会再次自动重启。";
    static StageHostException Failure(string reason)=>new("practice_restart",Manual+" ("+reason+")")
        { HoldQueue=true,Display=DailyText.Of("practice.restart_failed",Manual) };

    // Launches the same executable in the same Windows/Sandboxie scope, without
    // activating a saved slot, restoring credentials or calling SDK sign-in.
    public static async Task RestartAsync(string root,JsonObject context,IAccountSessions accounts,CancellationToken token,Action<string> report)
    {
        var host=new DailyGameHost(root);
        var old=host.Find()??throw Failure("game_missing");
        if(old.ProcessId!=N(context["actor"]?[0])||old.StartTicks!=N(context["actor"]?[1]))throw Failure("process_changed");
        using var control=new DailyControlOwner(root);control.Acquire();
        accounts.EnsureControl();
        string account=S(context["actor"]?[3]);
        DailySandbox.RequireBoundAccount(account);
        if(accounts.Read().CurrentKey!=account)throw Failure("local_account_changed");
        GameLauncher.ValidateLaunchContext(old.Executable);
        var oldMailbox=new DailyPipeMailbox(root,old);
        if(oldMailbox.Read("live","command.json")!=null)throw Failure("unsettled_command");
        DailyPipeMailbox? mailbox=null;
        string owner="",path=Path.Combine(root,"tools","bargain-training","logs","restart-"+Guid.NewGuid().ToString("N")+".json");
        var record=O(("state","restarting"),("at",DateTime.UtcNow.Ticks),("context",context));DailyJson.Write(path,record);
        void Permit(StartupPermit? value)
        {
            if(mailbox==null)return;
            var previous=mailbox.Read("daily","startup-permit.json");
            var active=previous==null?null:JsonSerializer.Deserialize<StartupPermit>(previous,DailyJson.Options);
            if(value!=null)
            {
                token.ThrowIfCancellationRequested();
                if(active is{Owner.Length:>0}&&active.Owner!=owner&&active.ExpiresUtcTicks>DateTime.UtcNow.Ticks)throw Failure("startup_owned");
                owner=value.Owner;
                mailbox.Write("daily","startup-permit.json",JsonSerializer.SerializeToUtf8Bytes(value,DailyJson.Options));
            }
            else if(owner.Length>0&&active?.Owner==owner)
                mailbox.Write("daily","startup-permit.json",JsonSerializer.SerializeToUtf8Bytes(new StartupPermit(),DailyJson.Options));
        }
        try
        {
            report("practice.restarting");
            await RunAsync(context,host,()=>{
                token.ThrowIfCancellationRequested();
                if(accounts.Read().CurrentKey!=account)throw Failure("local_account_changed");
                GameLauncher.LaunchDirect(old.Executable);
            },game=>{mailbox=new(root,game);mailbox.Acquire("daily");},Permit,token);
            record["state"]="resumed";report("practice.restart_resumed");
        }
        catch(OperationCanceledException){record["state"]="stopped";throw;}
        catch(Exception e){record["state"]="failed";record["error"]=e.ToString();throw Failure(e.Message);}
        finally {DailyJson.Write(path,record);}
    }

    internal static async Task RunAsync(JsonObject context,IGameHost host,Action launch,Action<GameInstance> acquire,
        Action<StartupPermit?> permit,CancellationToken token,Func<DateTimeOffset>? clock=null,Func<TimeSpan,Task>? delay=null)
    {
        clock??=()=>DateTimeOffset.UtcNow;delay??=t=>Task.Delay(t,token);
        string account=S(context["actor"]?[3]),player=S(context["actor"]?[4]);
        var old=host.Find()??throw Failure("game_missing");
        if(old.ProcessId!=N(context["actor"]?[0])||old.StartTicks!=N(context["actor"]?[1]))throw Failure("process_changed");
        var before=host.ReadSnapshot(old);
        if(!DailyIdentityGuard.Ready(before,old,clock())||before!.Loading||before.AccountKey!=account||before.PlayerKey!=player)
            throw Failure("identity_not_ready");
        token.ThrowIfCancellationRequested();
        await host.CloseAsync(old,token); // Normal window close only, never kill.
        token.ThrowIfCancellationRequested();
        if(host.Find()!=null)throw Failure("game_not_closed");
        launch(); // Exactly once. Failure does not recursively restart or log in.
        var deadline=clock().AddMinutes(3);var elapsed=System.Diagnostics.Stopwatch.StartNew();
        GameInstance? game=null;
        while((game=host.Find())==null)
        {
            token.ThrowIfCancellationRequested();
            if(clock()>=deadline||elapsed.Elapsed>=TimeSpan.FromMinutes(3))throw Failure("launch_timeout");
            await delay(TimeSpan.FromMilliseconds(350));
        }
        if(game==old||!Path.GetFullPath(game.Executable).Equals(Path.GetFullPath(old.Executable),StringComparison.OrdinalIgnoreCase))throw Failure("different_executable");
        await host.ConnectAsync(game,_=>{},token);acquire(game);
        string owner=Guid.NewGuid().ToString("N");int title=0;bool attempted=false;
        var guard=new DailyIdentityGuard();
        try
        {
            while(clock()<deadline&&elapsed.Elapsed<TimeSpan.FromMinutes(3))
            {
                token.ThrowIfCancellationRequested();
                if(host.Find()!=game)throw Failure("process_changed");
                DailySnapshot? s;
                try{s=host.ReadSnapshot(game);}catch(Exception e)when(e is IOException or TimeoutException){s=null;}
                if(s==null||!StartupPolicy.Fresh(s,clock().UtcTicks)){permit(null);await delay(TimeSpan.FromMilliseconds(350));continue;}
                if(s.ProcessId!=game.ProcessId||s.ProcessStartTicks!=game.StartTicks||s.AccountKey.Length>0&&s.AccountKey!=account
                    ||s.PlayerKey.Length>0&&s.PlayerKey!=player||s.ErrorCode.Length>0||s.LeaseOwner.Length>0)throw Failure("identity_changed");
                if(s.Guild is{} g&&(!string.IsNullOrEmpty(g.ServerKey)&&g.ServerKey!=S(context["server"])
                    ||!string.IsNullOrEmpty(g.CycleKey)&&g.CycleKey!=S(context["cycle"])))throw Failure("period_changed");
                var t=s.Startup;
                if(t?.Visible==true)
                {
                    if(t.DownloadVisible||t.BlockReason.Contains("弹窗",StringComparison.Ordinal))throw Failure("login_requires_user");
                    if(title!=0&&t.TitleInstanceId!=0&&title!=t.TitleInstanceId)throw Failure("second_login");
                    if(t.Attempted){attempted=true;permit(null);}
                    else if(!attempted&&s.AccountKey==account&&t.Ready&&t.ClickEnabled&&t.Supported&&t.BlockReason.Length==0&&t.TitleInstanceId!=0)
                    {
                        title=t.TitleInstanceId;
                        permit(new StartupPermit{Owner=owner,ProcessId=game.ProcessId,ProcessStartTicks=game.StartTicks,
                            InstanceId=s.InstanceId,AccountKey=account,ExpiresUtcTicks=clock().AddSeconds(5).UtcTicks,ResumeOnly=true,TitleInstanceId=title});
                    }
                    else permit(null);
                }
                else
                {
                    permit(null);
                    if(!s.Loading&&guard.Observe(s,game,account,player,clock()))return;
                }
                await delay(TimeSpan.FromMilliseconds(350));
            }
            throw Failure("login_timeout");
        }
        finally {permit(null);}
    }
}
