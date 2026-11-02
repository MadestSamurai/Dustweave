using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Dustweave.Soul;

namespace Dustweave.Desktop;

internal sealed class SoulToolWindow : Window
{
    readonly SoulSession session;
    readonly TextBlock state=Text(25), account=Text(14), stats=Text(16), detail=Text(14), history=Text(13);
    readonly Button start=new(),resume=new(),pause=new();
    readonly Border[] steps=new Border[5];
    readonly Queue<string> lines=new();
    readonly DailyLanguage L=DailyLanguage.Current;
    bool closing;
    Task<bool>? closeTask;
    internal event Action? ActivityChanged;
    internal SoulSession Session=>session;
    static TextBlock Text(double size)=>new(){FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
    internal SoulToolWindow(string root,Window? owner=null)
    {
        session=new(root);Owner=owner;Icon=owner?.Icon;Width=900;Height=690;MinWidth=720;MinHeight=580;
        WindowStartupLocation=owner is null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;
        var body=new DockPanel{Margin=new Thickness(24,0,24,24)};
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};
        foreach(var b in new[]{resume,pause,start}){b.Margin=new Thickness(8,0,0,0);b.Padding=new Thickness(20,11,20,11);footer.Children.Add(b);}
        start.Style=(Style)Application.Current.FindResource("PrimaryButton");
        DockPanel.SetDock(footer,Dock.Bottom);body.Children.Add(footer);
        var content=new StackPanel();body.Children.Add(content);
        content.Children.Add(account);content.Children.Add(state);
        var row=new System.Windows.Controls.Primitives.UniformGrid{Columns=5,Margin=new Thickness(0,10,0,22)};
        for(int i=0;i<5;i++){
            var label=Text(16);label.Text=L.Get("soul.chapter",i+1);label.Margin=new Thickness(12,18,12,18);label.TextAlignment=TextAlignment.Center;
            var card=new Border{CornerRadius=new CornerRadius(8),Margin=new Thickness(i==0?0:8,0,0,0),BorderThickness=new Thickness(1),Child=label};
            card.SetResourceReference(Border.BackgroundProperty,"Surface");card.SetResourceReference(Border.BorderBrushProperty,"Line");steps[i]=card;row.Children.Add(card);
        }
        content.Children.Add(row);content.Children.Add(stats);content.Children.Add(detail);
        var rule=new Border{Height=1,Margin=new Thickness(0,10,0,18)};rule.SetResourceReference(Border.BackgroundProperty,"Line");content.Children.Add(rule);
        content.Children.Add(history);history.Opacity=.76;
        Content=body;DailyDialogs.Prepare(this,toolWindow:true);DesktopWindowIdentity.Initialize(this);
        start.Click+=async(_,_)=>await StartAsync(false);resume.Click+=async(_,_)=>await StartAsync(true);pause.Click+=(_,_)=>session.Pause();
        session.Changed+=status=>Dispatcher.BeginInvoke(()=>Render(status));
        L.PropertyChanged+=LanguageChanged;
        Closing+=async(_,e)=>{if(closing)return;e.Cancel=true;await CloseSafelyAsync();};
        Closed+=(_,_)=>L.PropertyChanged-=LanguageChanged;
        Render(session.Status);
    }
    void LanguageChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs args)=>Render(session.Status);
    internal async Task StartAsync(bool resumeRun,string? expected=null,string? output=null)
    {
        start.IsEnabled=resume.IsEnabled=false;
        try{await session.StartAsync(resumeRun,expected,output);}
        catch(Exception error){Render(session.Status with{State="error",Message=error.Message});}
        finally{Render(session.Status);ActivityChanged?.Invoke();}
    }
    internal void Render(SoulStatus s)
    {
        Title=L.Get("soul.title");start.Content=L.Get("soul.start");resume.Content=L.Get("soul.resume");pause.Content=L.Get("soul.pause");
        bool busy=session.Running;start.IsEnabled=resume.IsEnabled=!busy;pause.IsEnabled=busy;
        account.Text=s.Player.Length>0?L.Get("soul.account",s.Player):L.Get("soul.before");
        state.Text=L.Get("soul.state."+s.State);
        stats.Text=s.Dungeon>0?L.Get("soul.stats",Math.Clamp(s.Dungeon-10,1,5),s.Wave,s.WaveCount,s.Life,s.Coins):L.Get("soul.progress",s.Completed,s.Wins,s.Losses);
        if(s.Dungeon>0)stats.Text+=" · "+L.Get("soul.attempt",s.Attempt);
        detail.Text=s.Message.Length>0?s.Message:busy?L.Get("soul.active_hint"):L.Get("soul.ready_hint");
        if(s.State=="planning"&&s.DecisionPhase.Length>0)
            detail.Text=L.Get("soul.phase."+s.DecisionPhase)+"\n"+L.Get("soul.search_progress",s.EvaluatedPlans,s.DecisionSeconds.ToString("F1"));
        if(s.State=="refreshing"&&s.SimulatedWinRate is {} weakRate)
            detail.Text=L.Get("soul.shopping",weakRate.ToString("P1"),s.Coins,s.RefreshPrice,s.RefreshCount);
        if(s.State is "applying" or "battling" && s.SimulatedWinRate is double rate){
            detail.Text=L.Get("soul.decision",rate.ToString("P1"),s.PlannedUnits);
            detail.Text+="\n"+L.Get("soul.actions",s.BuyCount,s.UpgradeCount,s.PlannedSpend,s.CoinsAfter);
            if(s.RiskyPlan)detail.Text+="\n"+L.Get("soul.decision_risk");
        }
        for(int i=0;i<5;i++){
            var label=(TextBlock)steps[i].Child;label.Text=L.Get("soul.chapter",i+1)+(i<s.Completed?"  ✓":"");
            steps[i].Opacity=i<s.Completed||i==s.Dungeon-11?1:.55;
            steps[i].BorderThickness=new Thickness(i==s.Dungeon-11?2:1);
        }
        if(s.State is "won" or "lost" or "complete" or "defeated" or "retrying" or "error"){
            string line=$"{DateTime.Now:HH:mm:ss}  {state.Text}  ·  "+L.Get("soul.progress",s.Completed,s.Wins,s.Losses);
            if(lines.LastOrDefault()!=line){lines.Enqueue(line);while(lines.Count>7)lines.Dequeue();history.Text=string.Join("\n",lines);}
        }
        ActivityChanged?.Invoke();
    }
    internal void BringForward(){if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Show();Activate();}
    internal Task<bool> CloseSafelyAsync()=>closeTask??=CloseCore();
    async Task<bool> CloseCore(){await Task.Yield();await session.StopAsync();closing=true;Close();return true;}
}
