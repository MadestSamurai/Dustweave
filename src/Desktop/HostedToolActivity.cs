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
    readonly DailyGameHost host=new();
    readonly DailyActivityLease activity;
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    readonly PropertyInfo running;
    readonly GameInstance game;
    readonly string account, player;
    readonly HashSet<string> starts=new(StringComparer.Ordinal){"StartButton","AutoButton","StepButton","ApplyButton","PreviewButton","ExecuteButton"};
    bool disposed, polling, reconnecting, resumeAttempted;
    public HostedToolActivity(Window window,string id)
    {
        this.window=window;this.id=id;root=DailyIdentity.DataRoot;suiteRoot=Path.Combine(root,"suite");
        activity=new(root);
        running=window.GetType().GetProperty("HostedAutomationEnabled")??throw new InvalidOperationException("工具未提供运行状态。");
        game=host.Find()??throw new InvalidOperationException("游戏已退出。");
        var snapshot=host.ReadSnapshot()??throw new InvalidOperationException("游戏身份尚未就绪。");
        account=snapshot.AccountKey;player=snapshot.PlayerKey;
        if(id=="equipment")starts.Add("ConnectButton");
        AppDomain.CurrentDomain.SetData(GuardKey,(Action<string,string,byte[]>)BeforeWrite);
        EventManager.RegisterClassHandler(typeof(Button),Button.ClickEvent,new RoutedEventHandler(BeforeClick),true);
        timer.Tick+=async(_,_)=>await Poll();timer.Start();
        window.Closed+=(_,_)=>Dispose();
    }
    bool Enabled => (bool)running.GetValue(window)! || Application.Current.Windows.Cast<Window>().Where(w=>w!=window).Any(w=>w.GetType().GetProperty("HostedAutomationEnabled")?.GetValue(w) is true);
    void BeforeClick(object sender,RoutedEventArgs args)
    {
        if(disposed||args.Handled||sender is not Button button||!starts.Contains(button.Name))return;
        try { activity.Enter(); AssertIdentity(); }
        catch(Exception error){args.Handled=true;MessageBox.Show(window,DailyLanguage.Current.Diagnostic(error.GetBaseException().Message, DailyUserText.Error(error.GetBaseException(),DailyLanguage.Current.Translate)),DailyLanguage.Current.Get("tools.start_error_title"),MessageBoxButton.OK,MessageBoxImage.Information);}
    }
    void AssertIdentity()
    {
        if(host.Find()!=game)throw new InvalidOperationException("游戏会话已结束，请返回日常助手重新连接。");
        var snapshot=host.ReadSnapshot();
        if(snapshot==null||snapshot.AccountKey!=account||snapshot.PlayerKey!=player||snapshot.FrameUtcTicks<DateTime.UtcNow.AddSeconds(-5).Ticks)
        {
            DailyJson.Write(Path.Combine(root,"tools",id+"-identity-error.json"),new{atUtc=DateTimeOffset.UtcNow,missing=snapshot==null,expectedAccount=account,expectedPlayer=player,actualAccount=snapshot?.AccountKey,actualPlayer=snapshot?.PlayerKey,frameAgeSeconds=snapshot==null?(double?)null:(DateTime.UtcNow.Ticks-snapshot.FrameUtcTicks)/(double)TimeSpan.TicksPerSecond,state=snapshot?.State,scene=snapshot?.Scene,error=snapshot?.ErrorCode});
            throw new InvalidOperationException("游戏身份已变化或连接已过期，请返回日常助手。");
        }
    }
    void BeforeWrite(string dataRoot,string name,byte[] value)
    {
        if(disposed)throw new ObjectDisposedException(nameof(HostedToolActivity));
        if(string.Equals(Path.GetFullPath(dataRoot),Path.GetFullPath(suiteRoot),StringComparison.OrdinalIgnoreCase))return;
        if(!DailyActivityRules.RequiresOwnership(name,value))return;
        activity.Enter();AssertIdentity();
        var state=DailySuite.Read(root,game);
        if(state?.State!="ready"||state.Tool!=id||state.Owner!=DailySuite.Owner||state.At<DateTime.UtcNow.AddSeconds(-5).Ticks)
            throw new InvalidOperationException("此工具尚未恢复就绪，请等待日常助手完成交接。");
    }
    async Task Poll()
    {
        if(disposed||polling)return;polling=true;
        try
        {
            bool enabled=Enabled;
            var current=await Task.Run(()=>{
                var found=host.Find();
                SuiteStatus? state=null;
                if(found==game)try{state=DailySuite.Read(root,game);}catch(IOException){}
                return (alive:found==game,state);
            });
            if(disposed)return;
            bool fresh=current.state!=null&&current.state.At>=DateTime.UtcNow.AddSeconds(-5).Ticks;
            activity.Observe(enabled||reconnecting,current.alive,fresh,DailyActivityRules.ExecutionPending(current.state?.Pending));
            bool occupied=!activity.Held&&DailyToolControl.IsOccupied(root);
            window.IsEnabled=!occupied;
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
        AppDomain.CurrentDomain.SetData(GuardKey,null);activity.Dispose();
    }
}
