using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Automation;
using Dustweave.Soul;

namespace Dustweave.Desktop;

internal sealed class SoulToolWindow : Window
{
    readonly SoulSession session;
    readonly TextBlock state=Text(25), account=Text(14), stats=Text(16), detail=Text(14), history=Text(13);
    readonly Button start=new(),resume=new(),pause=new();
    readonly ComboBox performance=new(){MinWidth=220};
    readonly ComboBox[] scales=Enumerable.Range(0,5).Select(_=>new ComboBox{MinWidth=95}).ToArray();
    readonly TextBlock performanceLabel=Text(14), settingsHint=Text(13), settingsError=Text(13);
    readonly TextBlock[] scaleLabels=Enumerable.Range(0,5).Select(_=>Text(14)).ToArray();
    readonly Expander settingsPanel=new(){IsExpanded=true,Margin=new Thickness(0,4,0,12)};
    bool changingSettings;
    readonly Border[] steps=new Border[5];
    readonly Queue<string> lines=new();
    readonly DailyLanguage L=DailyLanguage.Current;
    bool closing;
    bool? reportedRunning;
    SoulStatus? pendingProgress;
    readonly DispatcherTimer progressTimer=new(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(200)};
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
        foreach(var b in new[]{start,pause,resume}){b.Margin=new Thickness(8,0,0,0);b.Padding=new Thickness(20,11,20,11);footer.Children.Add(b);}
        resume.Style=(Style)Application.Current.FindResource("PrimaryButton");
        DockPanel.SetDock(footer,Dock.Bottom);body.Children.Add(footer);
        var content=new StackPanel();body.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        content.Children.Add(account);content.Children.Add(state);
        var settings=new StackPanel{Margin=new Thickness(0,12,0,0)};
        var performanceRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,12)};
        performanceLabel.VerticalAlignment=VerticalAlignment.Center;performanceLabel.Margin=new Thickness(0,0,18,0);
        performanceRow.Children.Add(performanceLabel);performanceRow.Children.Add(performance);settings.Children.Add(performanceRow);
        var scaleRow=new System.Windows.Controls.Primitives.UniformGrid{Columns=5};
        for(int i=0;i<5;i++){
            var cell=new StackPanel{Margin=new Thickness(i==0?0:10,0,0,0)};
            scaleLabels[i].Margin=new Thickness(0,0,0,6);cell.Children.Add(scaleLabels[i]);cell.Children.Add(scales[i]);scaleRow.Children.Add(cell);
            foreach(int count in SoulSolverSettings.CandidateOptions)scales[i].Items.Add(new ComboBoxItem{Tag=count});
            scales[i].SelectionChanged+=(_,_)=>SaveSettings();
        }
        settings.Children.Add(scaleRow);settingsHint.Margin=new Thickness(0,10,0,0);settings.Children.Add(settingsHint);
        settingsError.Visibility=Visibility.Collapsed;settingsError.SetResourceReference(TextBlock.ForegroundProperty,"Error");settings.Children.Add(settingsError);
        settingsPanel.Content=settings;content.Children.Add(settingsPanel);
        foreach(int count in new[]{1,2,3})performance.Items.Add(new ComboBoxItem{Tag=count});
        performance.SelectionChanged+=(_,_)=>SaveSettings();
        RefreshSettings();
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
        session.Changed+=QueueStatus;
        progressTimer.Tick+=(_,_)=>{if(Interlocked.Exchange(ref pendingProgress,null) is {} latest)Render(latest);};
        progressTimer.Start();
        L.PropertyChanged+=LanguageChanged;
        Closing+=async(_,e)=>{if(closing)return;e.Cancel=true;await CloseSafelyAsync();};
        Closed+=(_,_)=>{progressTimer.Stop();session.Changed-=QueueStatus;L.PropertyChanged-=LanguageChanged;};
        Render(session.Status);
    }
    void QueueStatus(SoulStatus status)
    {
        // Intermediate progress is replaceable. Never accumulate high-priority
        // dispatcher work ahead of mouse/keyboard input on a slower computer.
        if(status.State=="planning"){Interlocked.Exchange(ref pendingProgress,status);return;}
        Interlocked.Exchange(ref pendingProgress,null);
        Dispatcher.BeginInvoke(DispatcherPriority.Background,()=>{if(!closing)Render(status);});
    }
    void ReportActivity(bool running)
    {
        if(reportedRunning==running)return;
        reportedRunning=running;ActivityChanged?.Invoke();
    }
    void LanguageChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs args){RefreshSettings();Render(session.Status);}
    void RefreshSettings()
    {
        changingSettings=true;
        try{
            var value=session.Settings;settingsPanel.Header=L.Get("soul.settings.title");performanceLabel.Text=L.Get("soul.settings.performance");
            AutomationProperties.SetName(performance,performanceLabel.Text);
            foreach(ComboBoxItem item in performance.Items)item.Content=L.Get("soul.settings.workers."+(int)item.Tag);
            performance.SelectedIndex=value.WorkerLimit-1;
            for(int i=0;i<5;i++){
                scaleLabels[i].Text=L.Get("soul.chapter",i+1);AutomationProperties.SetName(scales[i],L.Get("soul.settings.chapter",i+1));
                foreach(ComboBoxItem item in scales[i].Items){item.Content=L.Get("soul.settings.count",((int)item.Tag).ToString("N0"));if((int)item.Tag==value.ChapterCandidates[i])scales[i].SelectedItem=item;}
            }
            settingsHint.Text=L.Get("soul.settings.help");
            if(settingsError.Visibility==Visibility.Visible)settingsError.Text=L.Get("soul.settings.save_failed");
        }finally{changingSettings=false;}
    }
    void SaveSettings()
    {
        if(changingSettings||performance.SelectedItem is not ComboBoxItem selected||scales.Any(c=>c.SelectedItem is not ComboBoxItem))return;
        try{
            session.Configure(new(){WorkerLimit=(int)selected.Tag,ChapterCandidates=scales.Select(c=>(int)((ComboBoxItem)c.SelectedItem).Tag).ToArray()});
            settingsError.Visibility=Visibility.Collapsed;
        }catch(Exception error){
            RefreshSettings();settingsError.Text=L.Get("soul.settings.save_failed");settingsError.ToolTip=error.Message;settingsError.Visibility=Visibility.Visible;
        }
    }
    internal async Task StartAsync(bool resumeRun,string? expected=null,string? output=null)
    {
        settingsPanel.IsExpanded=false;
        start.IsEnabled=resume.IsEnabled=false;
        try{var task=session.StartAsync(resumeRun,expected,output);ReportActivity(session.Running);await task;}
        catch(Exception error){Render(session.Status with{State="error",Message=error.Message});}
        finally{Interlocked.Exchange(ref pendingProgress,null);Render(session.Status);}
    }
    internal void Render(SoulStatus s)
    {
        Title=L.Get("soul.title");start.Content=L.Get("soul.start");resume.Content=L.Get("soul.resume");pause.Content=L.Get("soul.pause");
        bool busy=session.Running;start.IsEnabled=resume.IsEnabled=!busy;pause.IsEnabled=busy;
        account.Text=s.Player.Length>0?L.Get("soul.account",s.Player):L.Get("soul.before");
        state.Text=L.Get("soul.state."+s.State);
        int chapter=s.Dungeons is {Length:5}?Array.IndexOf(s.Dungeons,s.Dungeon)+1:Math.Clamp(s.Dungeon-10,1,5);
        stats.Text=s.Dungeon>0?L.Get("soul.stats",chapter,s.Wave,s.WaveCount,s.Life,s.Coins):L.Get("soul.progress",s.Completed,s.Wins,s.Losses);
        if(s.Dungeon>0)stats.Text+=" · "+L.Get("soul.attempt",s.Attempt);
        detail.Text=s.Message.Length>0?s.Message:busy?L.Get("soul.active_hint"):L.Get("soul.ready_hint");
        if(s.State=="planning"&&s.DecisionPhase.Length>0)
            detail.Text=L.Get("soul.phase."+s.DecisionPhase)+"\n"+(s.CandidateTarget>0
                ?L.Get("soul.search_candidates",s.ProposedCandidates.ToString("N0"),s.CandidateTarget.ToString("N0"),s.EvaluatedPlans.ToString("N0"),s.DecisionSeconds.ToString("F1"))
                :L.Get("soul.search_progress",s.EvaluatedPlans,s.DecisionSeconds.ToString("F1")));
        if(s.State=="refreshing"&&s.SimulatedWinRate is {} weakRate)
            detail.Text=L.Get("soul.shopping",weakRate.ToString("P1"),s.Coins,s.RefreshPrice,s.RefreshCount);
        if(s.State is "applying" or "battling" && s.SimulatedWinRate is double rate){
            detail.Text=L.Get("soul.decision",rate.ToString("P1"),s.PlannedUnits);
            detail.Text+="\n"+L.Get("soul.actions",s.BuyCount,s.UpgradeCount,s.PlannedSpend,s.CoinsAfter);
            if(s.RiskyPlan)detail.Text+="\n"+L.Get("soul.decision_risk");
        }
        for(int i=0;i<5;i++){
            int dungeon=s.Dungeons is {Length:5}?s.Dungeons[i]:i+11;
            bool completed=s.CompletedDungeons?.Contains(dungeon)??i<s.Completed;
            var label=(TextBlock)steps[i].Child;label.Text=L.Get("soul.chapter",i+1)+(completed?"  ✓":"");
            steps[i].Opacity=completed||dungeon==s.Dungeon?1:.55;
            steps[i].BorderThickness=new Thickness(dungeon==s.Dungeon?2:1);
        }
        if(s.State is "won" or "lost" or "complete" or "defeated" or "retrying" or "error"){
            string line=$"{DateTime.Now:HH:mm:ss}  {state.Text}  ·  "+L.Get("soul.progress",s.Completed,s.Wins,s.Losses);
            if(lines.LastOrDefault()!=line){lines.Enqueue(line);while(lines.Count>7)lines.Dequeue();history.Text=string.Join("\n",lines);}
        }
        // The host needs control ownership transitions, not every candidate
        // counter. SetBusy refreshes bindings, task rows and connection state.
        ReportActivity(busy);
    }
    internal void BringForward(){if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Show();Activate();}
    internal async Task CheckProgressForSmoke(string output)
    {
        int hostNotifications=0;
        void Changed()=>hostNotifications++;
        ActivityChanged+=Changed;
        try{
            var sample=session.Status with{State="planning",DecisionPhase="combinations",CandidateTarget=3000};
            for(int i=0;i<1000;i++)Render(sample with{ProposedCandidates=i});
            if(hostNotifications!=0)throw new Exception("Solver progress refreshed host activity controls");
            ReportActivity(true);ReportActivity(true);ReportActivity(false);ReportActivity(false);
            if(hostNotifications!=2)throw new Exception("Host did not receive exactly the start/stop transitions");
            await Task.Run(()=>{for(int i=0;i<10000;i++)QueueStatus(sample with{ProposedCandidates=i});});
            bool inputRan=false;
            await Dispatcher.InvokeAsync(()=>inputRan=true,DispatcherPriority.Input).Task.WaitAsync(TimeSpan.FromSeconds(5));
            if(!inputRan)throw new Exception("Solver progress starved input");
            // Drain the actual timer callback, independent of scheduler jitter.
            await Task.Delay(350);
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            if(pendingProgress is not null||!detail.Text.Contains(9999.ToString("N0")))throw new Exception("Latest search progress was not displayed");
            QueueStatus(sample with{ProposedCandidates=10000});
            QueueStatus(session.Status with{State="paused"});
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            await Task.Delay(250);
            if(pendingProgress is not null||state.Text!=L.Get("soul.state.paused"))throw new Exception("Stale progress replaced a terminal state");
            DailyJson.Write(Path.Combine(output,"progress-check.json"),new{status="passed",renderedProgress=1000,queuedProgress=10000,hostNotifications,inputRan,terminalStatePreserved=true,realGameTouched=false});
        }finally{ActivityChanged-=Changed;}
    }
    internal void CheckSettingsForSmoke(string root)
    {
        performance.SelectedIndex=0;
        for(int i=0;i<5;i++)scales[i].SelectedIndex=i%SoulSolverSettings.CandidateOptions.Count;
        var saved=SoulSolverSettings.Load(root);
        if(saved.WorkerLimit!=1||!saved.ChapterCandidates.SequenceEqual(Enumerable.Range(0,5).Select(i=>SoulSolverSettings.CandidateOptions[i%4])))
            throw new Exception("Soul settings did not persist independent chapter choices");
        var restored=new SoulSession(root).Settings;
        if(restored.WorkerLimit!=saved.WorkerLimit||!restored.ChapterCandidates.SequenceEqual(saved.ChapterCandidates)||session.Running)
            throw new Exception("Soul settings reload changed choices or started gameplay");
        RefreshSettings();UpdateLayout();
        foreach(var combo in scales.Prepend(performance)){
            if(string.IsNullOrWhiteSpace(AutomationProperties.GetName(combo))||!combo.IsEnabled||combo.ActualWidth<90)
                throw new Exception("Soul settings lost accessible names or usable controls");
            var bounds=combo.TransformToAncestor(this).TransformBounds(new Rect(combo.RenderSize));
            if(bounds.Right>ActualWidth-16)throw new Exception("Soul settings overflow the compact window");
        }
        var footer=start.TransformToAncestor(this).TransformBounds(new Rect(start.RenderSize));
        if(footer.Bottom>ActualHeight||!start.IsVisible||!resume.IsVisible)throw new Exception("Soul settings obscured execution controls");
        if(resume.Style!=Application.Current.FindResource("PrimaryButton")||start.Style==resume.Style
            ||resume.TranslatePoint(new Point(),this).X<=start.TranslatePoint(new Point(),this).X)
            throw new Exception("Resume must be the primary rightmost action");
        session.Configure(new());RefreshSettings();
        UpdateLayout();
    }
    internal Task<bool> CloseSafelyAsync()=>closeTask??=CloseCore();
    async Task<bool> CloseCore(){await Task.Yield();await session.StopAsync();closing=true;Close();return true;}
}
