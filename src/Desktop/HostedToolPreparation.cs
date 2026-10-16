using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Dustweave.Desktop;

// The actual tool window is rendered before any connection or compilation work.
// Preparation owns the same lease as daily execution; an idle open window does not.
internal sealed class HostedToolPreparation
{
    readonly Window window;
    readonly FrameworkElement body;
    readonly string root, id;
    readonly Func<Action<string>,CancellationToken,Task> prepare;
    readonly Action ready;
    readonly Border banner;
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};
    readonly ProgressBar progress=new(){Height=4,Margin=new Thickness(0,12,0,0)};
    readonly Button action=new(){MinWidth=90,Margin=new Thickness(16,0,0,0),VerticalAlignment=VerticalAlignment.Center};
    CancellationTokenSource? cancellation;
    bool started, closed, closeRequested;
    internal Task? Pending {get;private set;}
    internal bool Preparing=>cancellation!=null;
    internal bool Ready {get;private set;}
    internal FrameworkElement Body=>body;
    internal Button ActionButton=>action;
    internal ProgressBar Progress=>progress;

    internal HostedToolPreparation(Window window,string id,string root,
        Func<Action<string>,CancellationToken,Task> prepare,Action ready)
    {
        this.window=window;this.id=id;this.root=root;this.prepare=prepare;this.ready=ready;
        // Capture the content before applying the shared title bar, so close,
        // minimize and maximize remain usable while the tool content is locked.
        body=(FrameworkElement)window.Content;window.Content=null;
        var layout=new DockPanel();
        var panel=new DockPanel();DockPanel.SetDock(action,Dock.Right);panel.Children.Add(action);panel.Children.Add(status);
        var stack=new StackPanel();stack.Children.Add(panel);stack.Children.Add(progress);
        banner=new Border{Child=stack,Padding=new Thickness(16),Margin=new Thickness(24,8,24,12),CornerRadius=new CornerRadius(8)};
        banner.SetResourceReference(Border.BackgroundProperty,"SurfaceMuted");
        status.SetResourceReference(TextBlock.ForegroundProperty,"Ink");
        progress.SetResourceReference(ProgressBar.ForegroundProperty,"Primary");
        DockPanel.SetDock(banner,Dock.Top);layout.Children.Add(banner);layout.Children.Add(body);
        window.Content=layout;body.IsEnabled=false;
        SetLoading(true);SetStatus("tools.preparing_window");
        action.Click+=async(_,_)=>{if(Preparing)Cancel();else await StartAsync();};
        window.ContentRendered+=OnRendered;
        window.Closing+=OnClosing;
        window.Closed+=(_,_)=>{closed=true;cancellation?.Cancel();};
    }

    async void OnRendered(object? sender,EventArgs args)
    {
        if(started)return;started=true;
        await Dispatcher.Yield(DispatcherPriority.Background);
        if(!closed&&!closeRequested)await StartAsync();
    }
    internal Task StartAsync()
    {
        if(Preparing||closed||closeRequested||Ready)return Pending??Task.CompletedTask;
        Pending=RunAsync();return Pending;
    }
    internal void Cancel()
    {
        if(!Preparing)return;
        action.IsEnabled=false;SetStatus("tools.preparation_stopping");cancellation!.Cancel();
    }
    async Task RunAsync()
    {
        using var cancel=new CancellationTokenSource();cancellation=cancel;
        body.IsEnabled=false;banner.Visibility=Visibility.Visible;SetLoading(true);SetStatus("tools.preparing_window");
        try
        {
            // The menu keeps a short reservation until it has recorded this child.
            // Never wait indefinitely behind a daily run or another active tool.
            for(int i=0;i<60&&DailyToolControl.IsOccupied(root);i++)await Task.Delay(50,cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();
            using(var lease=DailyToolControl.Acquire(root))
            {
                var messages=new Progress<string>(message=>{if(!closed&&!cancel.IsCancellationRequested)SetStatus(message);});
                await Task.Run(()=>prepare(message=>((IProgress<string>)messages).Report(message),cancel.Token),cancel.Token);
                cancel.Token.ThrowIfCancellationRequested();
            }
            if(closed||closeRequested)return;
            // No await between releasing preparation control and installing the
            // command guard. The subsequent read/connect click takes its own lease.
            body.IsEnabled=true;ready();Ready=true;banner.Visibility=Visibility.Collapsed;
        }
        catch(OperationCanceledException) when(cancel.IsCancellationRequested){SetStatus("tools.preparation_cancelled");}
        catch(Exception error)
        {
            body.IsEnabled=false;
            SetStatus(DailyUserText.Error(error.GetBaseException(),DailyLanguage.Current.Translate));
            try{DailyJson.Write(Path.Combine(root,"tools",id+"-preparation-error.json"),new{atUtc=DateTimeOffset.UtcNow,tool=id,error=error.ToString()});}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        finally
        {
            cancellation=null;SetLoading(false);
            if(closeRequested&&!closed)window.Close();
        }
    }
    void SetStatus(string message)=>DailyLanguage.Current.Bind(status,TextBlock.TextProperty,()=>DailyLanguage.Current.Translate(message));
    void SetLoading(bool loading)
    {
        progress.Visibility=loading?Visibility.Visible:Visibility.Collapsed;
        progress.IsIndeterminate=loading&&SystemParameters.ClientAreaAnimation;progress.Value=loading?50:0;
        DailyLanguage.Current.Bind(action,ContentControl.ContentProperty,loading?"common.cancel":"tools.preparation_retry");
        AutomationProperties.SetName(progress,DailyLanguage.Current.Get("tools.preparing_window"));
        action.IsEnabled=true;
    }
    void OnClosing(object? sender,CancelEventArgs args)
    {
        if(!Preparing)return;
        args.Cancel=true;closeRequested=true;Cancel();
    }

    internal static async Task PrepareAsync(string id,string root,Action<string> report,CancellationToken cancel)
    {
        var host=new DailyGameHost(root);
        var game=host.Find()??throw new InvalidOperationException("请先启动游戏。");
        if(!int.TryParse(Environment.GetEnvironmentVariable("BD2_DAILY_GAME_PID"),out int pid)||
            !long.TryParse(Environment.GetEnvironmentVariable("BD2_DAILY_GAME_START"),out long start)||
            pid!=game.ProcessId||start!=game.StartTicks)
            throw new InvalidOperationException(DailyLanguage.Current.Get("tools.preparation_game_changed"));
        await host.ConnectAsync(game,report,cancel);
        report("tools.preparation_identity");
        for(int i=0;i<20;i++)
        {
            cancel.ThrowIfCancellationRequested();
            if(host.Find()!=game)throw new InvalidOperationException(DailyLanguage.Current.Get("tools.preparation_game_changed"));
            if(host.ReadSnapshot(game) is {State:"identified"} snapshot &&snapshot.FrameUtcTicks>=DateTime.UtcNow.AddSeconds(-5).Ticks)break;
            await Task.Delay(250,cancel);
        }
        await DailySuite.ActivateAsync(host,root,id,report,cancel);
        cancel.ThrowIfCancellationRequested();
        if(host.Find()!=game)throw new InvalidOperationException(DailyLanguage.Current.Get("tools.preparation_game_changed"));
        var state=DailySuite.Read(root,game)??throw new InvalidOperationException("统一会话未就绪。");
        var environment=new ProcessStartInfo();DailySuite.SetToolEnvironment(environment,state,game,id);
        // Change only this child, never the desktop process or persistent settings.
        foreach(string key in new[]{"BD2_DAILY_HOSTED_TOOL","BD2_DAILY_GAME_PID","BD2_DAILY_GAME_START","BD2_DAILY_TOOL_FINGERPRINT"})
            Environment.SetEnvironmentVariable(key,environment.Environment[key]);
    }
}
