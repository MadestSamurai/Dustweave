using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Dustweave.Desktop;

/// <summary>Window-independent execution lease, shared by every tool's command transport.</summary>
internal sealed class HostedToolActivity : IDisposable
{
    const string GuardKey="BD2Daily.HostedWriteGuard";
    readonly Window window;
    readonly string id, root, suiteRoot;
    readonly IGameHost host;
    readonly Func<SuiteStatus?> readState;
    readonly Action<Exception>? reportRejection;
    readonly DailyActivityLease activity;
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    readonly PropertyInfo running;
    readonly GameInstance game;
    readonly string account, player;
    readonly HashSet<string> starts=new(StringComparer.Ordinal){"ConnectButton","StartButton","AutoButton","StepButton","ApplyButton","PreviewButton","ExecuteButton"};
    readonly Dictionary<Button,bool> blockedButtons=new();
    bool disposed, polling, reconnecting, resumeAttempted;
    string identityProblem = "";
    public HostedToolActivity(Window window,string id) : this(window,id,new DailyGameHost(),DailyIdentity.DataRoot,null,null) { }
    internal HostedToolActivity(Window window,string id,IGameHost host,string root,Func<SuiteStatus?>? readState,Action<Exception>? reportRejection)
    {
        this.window=window;this.id=id;this.host=host;this.root=root;suiteRoot=Path.Combine(root,"suite");
        this.reportRejection=reportRejection;
        activity=new(root);
        running=window.GetType().GetProperty("HostedAutomationEnabled")??throw new InvalidOperationException("工具未提供运行状态。");
        game=host.Find()??throw new InvalidOperationException("游戏已退出。");
        var snapshot=host.ReadSnapshot(game)??throw new InvalidOperationException("游戏身份尚未就绪。");
        account=snapshot.AccountKey;player=snapshot.PlayerKey;
        this.readState=readState??(()=>DailySuite.Read(root,game));
        AppDomain.CurrentDomain.SetData(GuardKey,(Action<string,string,byte[]>)BeforeWrite);
        EventManager.RegisterClassHandler(typeof(Button),Button.ClickEvent,new RoutedEventHandler(BeforeClick),true);
        timer.Tick+=async(_,_)=>await Poll();timer.Start();
        window.Closed+=(_,_)=>Dispose();
    }
    bool Enabled => (bool)running.GetValue(window)! || Application.Current.Windows.Cast<Window>().Where(w=>w!=window).Any(w=>w.GetType().GetProperty("HostedAutomationEnabled")?.GetValue(w) is true);
    void SetOccupied(bool occupied)
    {
        // Only commands which need control are disabled. Settings, pause and close
        // remain reachable; the click guard also rejects a tool's timer re-enabling
        // a command between refreshes.
        if(!occupied)
        {
            foreach(var pair in blockedButtons)pair.Key.SetCurrentValue(UIElement.IsEnabledProperty,pair.Value);
            blockedButtons.Clear();return;
        }
        foreach(var view in Application.Current.Windows.Cast<Window>().Where(w=>w==window||w.Owner==window))
        foreach(var name in starts)
        if(view.FindName(name) is Button button)
        {
            if(!blockedButtons.ContainsKey(button)||button.IsEnabled)blockedButtons[button]=button.IsEnabled;
            button.SetCurrentValue(UIElement.IsEnabledProperty,false);
        }
    }
    void BeforeClick(object sender,RoutedEventArgs args)
    {
        if(disposed||args.Handled||sender is not Button button||!starts.Contains(button.Name))return;
        try { activity.Enter(); AssertIdentity(); }
        catch(Exception error){args.Handled=true;if(reportRejection!=null)reportRejection(error);else MessageBox.Show(window,DailyLanguage.Current.Diagnostic(error.GetBaseException().Message, DailyUserText.Error(error.GetBaseException(),DailyLanguage.Current.Translate)),DailyLanguage.Current.Get("tools.start_error_title"),MessageBoxButton.OK,MessageBoxImage.Information);}
    }
    void AssertIdentity()
    {
        DailyIdentityRead? observed = null;
        try
        {
            DailyToolIdentity.Require(game, account, player, host.Find, () => observed = host.ReadIdentity(game));
            if (identityProblem.Length != 0)
            {
                RecordIdentity("recovered", observed, "", "");
                identityProblem = "";
            }
        }
        catch (DailyObservationPendingException error)
        {
            if (identityProblem != error.Reason) RecordIdentity("waiting", observed, error.Reason, error.Detail);
            identityProblem = error.Reason;
            throw;
        }
        catch (InvalidOperationException error)
        {
            RecordIdentity("rejected", observed, "identity-changed", error.Message);
            throw;
        }
    }
    void RecordIdentity(string phase, DailyIdentityRead? observed, string problem, string detail)
    {
        var snapshot = observed?.Snapshot;
        try { DailyJson.Write(Path.Combine(root,"tools",id+"-identity-error.json"),new{atUtc=DateTimeOffset.UtcNow,phase,problem,detail,missing=snapshot==null,expectedAccount=account,expectedPlayer=player,actualAccount=snapshot?.AccountKey,actualPlayer=snapshot?.PlayerKey,frameAgeSeconds=snapshot==null?(double?)null:(DateTime.UtcNow.Ticks-snapshot.FrameUtcTicks)/(double)TimeSpan.TicksPerSecond,state=snapshot?.State,scene=snapshot?.Scene,error=snapshot?.ErrorCode}); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    void BeforeWrite(string dataRoot,string name,byte[] value)
    {
        if(disposed)throw new ObjectDisposedException(nameof(HostedToolActivity));
        if(string.Equals(Path.GetFullPath(dataRoot),Path.GetFullPath(suiteRoot),StringComparison.OrdinalIgnoreCase))return;
        if(!DailyActivityRules.RequiresOwnership(name,value))return;
        activity.Enter();AssertIdentity();
        var state=readState();
        if(state==null||state.At<DateTime.UtcNow.AddSeconds(-5).Ticks||state.At>DateTime.UtcNow.AddSeconds(2).Ticks)
            throw new DailyObservationPendingException("suite-snapshot-stale");
        if(state.Owner!=DailySuite.Owner)
            throw new DailyToolSessionChangedException("此工具尚未恢复就绪，请等待日常助手完成交接。");
        if(state.State!="ready")throw new DailyObservationPendingException("suite-not-ready",state.State);
        if(state.Tool!=id)
            throw new DailyToolSessionChangedException("此工具尚未恢复就绪，请等待日常助手完成交接。");
    }
    internal async Task Poll()
    {
        if(disposed||polling)return;polling=true;
        try
        {
            var current=await Task.Run(()=>{
                var found=host.Find();
                SuiteStatus? state=null;
                if(found==game)try{state=readState();}catch(IOException){}
                return (alive:found==game,state);
            });
            if(disposed)return;
            // A connect/start click may have arrived while the observation was in flight.
            bool enabled=Enabled;
            bool fresh=current.state!=null&&current.state.At>=DateTime.UtcNow.AddSeconds(-5).Ticks;
            activity.Observe(enabled||reconnecting,current.alive,fresh,DailyActivityRules.ExecutionPending(current.state?.Pending));
            bool occupied=!activity.Held&&DailyToolControl.IsOccupied(root);
            SetOccupied(occupied);
            if(current.state?.Tool==id&&current.state.State=="ready"){resumeAttempted=false;return;}
            // An idle window may outlive a daily run. Rebind only after that run releases control,
            // once per handoff; a failed connection never becomes an automatic reconnect loop.
            if(!current.alive||!fresh||enabled||occupied||reconnecting||resumeAttempted)return;
            resumeAttempted=true;reconnecting=true;activity.Enter();AssertIdentity();
            await DailySuite.ActivateAsync(host,root,id,_=>{},CancellationToken.None);
            if(disposed)return;
            if(window.FindName("ConnectButton") is Button button&&id!="equipment")
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        catch(Exception error)
        {
            DailyJson.Write(Path.Combine(root,"tools",id+"-activity-error.json"),new{atUtc=DateTimeOffset.UtcNow,error=error.GetBaseException().Message});
        }
        finally {reconnecting=false;polling=false;}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;timer.Stop();
        SetOccupied(false);
        AppDomain.CurrentDomain.SetData(GuardKey,null);activity.Dispose();
    }
}
