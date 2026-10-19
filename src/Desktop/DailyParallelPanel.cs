using System.Windows;
using System.Windows.Controls;
namespace Dustweave.Desktop;

public sealed class DailyParallelPanel : ScrollViewer
{
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly StackPanel rows = new();
    private readonly TextBlock notice=new() { TextWrapping=TextWrapping.Wrap,Visibility=Visibility.Collapsed,Margin=new(0,0,0,12) };
    public void Feedback(string message) { notice.Text=L.Describe(message);notice.Visibility=message.Length==0?Visibility.Collapsed:Visibility.Visible; }
    private readonly TextBlock summary = new() { FontSize=20, FontWeight=FontWeights.SemiBold, TextWrapping=TextWrapping.Wrap };
    private readonly Button pause, resume, stop, back;
    private string selected="", renderKey="";
    private DailyParallelRun? displayed;
    public event Action<string,string>? ControlRequested;
    public event Action<DailyParallelItem>? GameRequested;
    public event Action? BackRequested;
    public DailyParallelPanel()
    {
        VerticalScrollBarVisibility=ScrollBarVisibility.Auto; HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        var layout=new StackPanel { Margin=new(0,0,8,0) };
        var header=new Grid(); header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var introduction=new StackPanel { VerticalAlignment=VerticalAlignment.Center }; introduction.Children.Add(summary);
        var help=Text("parallel.overview_help");help.Margin=new(0,6,0,0);help.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");introduction.Children.Add(help);header.Children.Add(introduction);
        back=Button("parallel.back",()=>{if(back?.IsEnabled==true)BackRequested?.Invoke();});
        back.Style=(Style)Application.Current.FindResource("PrimaryButton");back.Margin=new(18,0,0,0);back.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(back,1);header.Children.Add(back);
        layout.Children.Add(new Border { Style=(Style)Application.Current.FindResource("Panel"),Padding=new(18,16,18,16),Margin=new(0,0,0,14),Child=header });
        var actions=new WrapPanel { Margin=new(0,0,0,12) };
        pause=Button("parallel.pause_all",()=>ControlRequested?.Invoke("","pause"));
        resume=Button("parallel.resume_all",()=>ControlRequested?.Invoke("","resume"));
        stop=Button("parallel.stop_all",()=>ControlRequested?.Invoke("","stop"));
        actions.Children.Add(pause);actions.Children.Add(resume);actions.Children.Add(stop);
        layout.Children.Add(actions);notice.SetResourceReference(TextBlock.ForegroundProperty,"Error");layout.Children.Add(notice);layout.Children.Add(rows);Content=layout;
        WeakEventManager<DailyLanguage,EventArgs>.AddHandler(L,nameof(DailyLanguage.Changed),(_,_)=>{renderKey="";Show(displayed);});
    }
    private static TextBlock Text(string key)
    {
        var text=new TextBlock { TextWrapping=TextWrapping.Wrap };L.Text(text,key);return text;
    }
    private static Button Button(string key,Action action)
    {
        var button=new Button { Margin=new(0,0,8,6),MinHeight=32 };L.Bind(button,ContentControl.ContentProperty,key);
        button.Click+=(_,_)=>action();return button;
    }
    public void Show(DailyParallelRun? run)
    {
        displayed=run;var items=run?.Items??[];
        int finished=items.Count(x=>x.State is "completed" or "partial" or "failed" or "stopped" or "interrupted");
        L.Text(summary,"parallel.summary",finished,items.Count,run?.Options.Maximum??2);
        pause.IsEnabled=items.Any(x=>x.State is "waiting" or "starting" or "connecting" or "running");
        resume.IsEnabled=items.Any(x=>x.State is "paused" or "held");
        stop.IsEnabled=items.Any(x=>DailyParallelSession.Occupies(x.State)||x.State is "waiting" or "held");
        back.IsEnabled=!stop.IsEnabled;
        L.Bind(back,FrameworkElement.ToolTipProperty,back.IsEnabled?"parallel.return_help":"parallel.return_busy");
        string key=string.Join("|",items.Select(x=>x.Job.Id+x.State+x.Detail+string.Join(',',x.Status?.Queue?.Stages.Select(s=>s.Task+s.State+DailyResultText.Detail(s)+(s.PendingTasks?.Count??0))??[])))+selected+L.Code;
        if(renderKey==key)return;renderKey=key;rows.Children.Clear();
        foreach(var item in items)AddRow(item);
    }
    private void AddRow(DailyParallelItem item)
    {
        var body=new StackPanel { Margin=new(0,12,0,12) };
        var header=new Grid();header.ColumnDefinitions.Add(new(){Width=new GridLength(64)});header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var title=new TextBlock { Text=item.Account.Name,FontSize=16,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=item.Account.Name,Margin=new(0,0,16,0) };
        bool interruptedPause=item.State=="paused" && !string.IsNullOrWhiteSpace(item.Detail) && !item.Detail.StartsWith("parallel.");
        var state=Text("parallel.state."+(interruptedPause?"interrupted":item.State));state.SetResourceReference(TextBlock.ForegroundProperty,interruptedPause || item.State is "failed" or "interrupted"?"Error":"MutedInk");Grid.SetColumn(state,2);state.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(title,1);title.VerticalAlignment=VerticalAlignment.Center;
                if(item.Status is {} final && !DailyParallelSession.Occupies(item.State) && item.State is not ("waiting" or "held"))
        {state.Text += "  ·  " + final.AtUtc.LocalDateTime.ToString("HH:mm");state.ToolTip=final.AtUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");}
        header.Children.Add(title);header.Children.Add(state);body.Children.Add(header);
        var stages=item.Status?.Queue?.Stages??[];int done=stages.Count(s=>s.State is "completed" or "skipped");
        header.Children.Add(new ProgressBar { Width=52,Height=52,Style=(Style)Application.Current.FindResource("CircularProgress"),Minimum=0,Maximum=Math.Max(1,stages.Count),Value=done,Margin=new(0,0,12,0),ToolTip=$"{done} / {stages.Count}" });
        string description=item.Detail.StartsWith("parallel.")?L.Get(item.Detail):L.Describe(item.Detail);
        if(interruptedPause && DailyIssues.Classify(item.Detail).Code!="unknown") description=DailyIssuePresentation.WithAdvice(item.Detail,description);
        var detail=new TextBlock { Text=description,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,8) };detail.SetResourceReference(TextBlock.ForegroundProperty,"MutedInk");detail.Visibility=string.IsNullOrWhiteSpace(detail.Text)?Visibility.Collapsed:Visibility.Visible;detail.Margin=new(64,4,0,8);body.Children.Add(detail);
        var controls=new WrapPanel { Margin=new(64,4,0,0) };
        var game=Button("parallel.game",()=>GameRequested?.Invoke(item));game.IsEnabled=item.Status?.GameId>0;controls.Children.Add(game);
        var toggle=Button(item.State is "paused" or "held"?"parallel.resume":"parallel.pause",()=>ControlRequested?.Invoke(item.Account.AccountKey,item.State is "paused" or "held"?"resume":"pause"));
        toggle.IsEnabled=item.State is "waiting" or "starting" or "connecting" or "running" or "paused" or "held";controls.Children.Add(toggle);
        var end=Button("parallel.stop",()=>ControlRequested?.Invoke(item.Account.AccountKey,"stop"));end.IsEnabled=DailyParallelSession.Occupies(item.State)||item.State is "waiting" or "held";controls.Children.Add(end);
        controls.Children.Add(Button(selected==item.Account.AccountKey?"parallel.hide_details":"parallel.details",()=>{selected=selected==item.Account.AccountKey?"":item.Account.AccountKey;renderKey="";Show(displayed);}));body.Children.Add(controls);
        if(selected==item.Account.AccountKey)
        {
            foreach(var stage in stages)
            {
                string text=DailyResultText.Detail(stage);
                var row=new StackPanel { Margin=new(8,6,8,8) };
                row.Children.Add(new TextBlock { Text=L.Stage(stage.Task)+"  ·  "+L.State(stage.State)+(text.Length==0?"":"\n"+text),TextWrapping=TextWrapping.Wrap });
                if(stage.Draws is { Draws: > 0 } || stage.PendingTasks?.Count>0)
                {
                    var details=Button("run.details",()=>{
                        Window dialog=stage.Draws is { Draws: > 0 } draw?DailyResultWindow.DrawDetails(draw):new DailyTaskDetailsWindow(stage.Task,stage.PendingTasks??[],stage.FinishedAt);
                        dialog.Owner=Window.GetWindow(this);dialog.Show();
                    });
                    details.HorizontalAlignment=HorizontalAlignment.Left;details.Margin=new(0,8,0,0);row.Children.Add(details);
                }
                if (stage.Absorptions is { Maps: > 0 } day) row.Children.Add(DailyResultWindow.AbsorptionButton(day, Window.GetWindow(this)));
                body.Children.Add(row);
            }
            if(stages.Count==0)body.Children.Add(Text("parallel.no_steps"));
        }
        var separator=new Border { BorderThickness=new(0,0,0,1),Child=body };separator.SetResourceReference(Border.BorderBrushProperty,"Line");rows.Children.Add(separator);
    }
}
