using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dustweave.Desktop;

internal sealed class DailyTaskDetailsWindow : Window
{
    private static DailyLanguage L => DailyLanguage.Current;
    internal ScrollViewer TaskScroll { get; }
    internal Button CloseAction { get; }
    internal int TaskCount { get; }
    internal IReadOnlyList<TextBlock> TaskTitles => titles;
    private readonly List<TextBlock> titles = [];
    public DailyTaskDetailsWindow(string stage, IReadOnlyList<QueueTaskDetail> tasks, DateTimeOffset? recordedAt)
    {
        TaskCount=tasks.Count;
        Width=680;Height=560;MinWidth=460;MinHeight=360;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty,"AppBackground");
        L.Bind(this,TitleProperty,()=>L.Get("run.details_title",L.Stage(stage)));
        var layout=new DockPanel();
        layout.SetResourceReference(Panel.BackgroundProperty,"AppBackground");
        var heading=new StackPanel{Margin=new(0,0,0,18)};
        heading.Children.Add(Label(()=>L.Stage(stage),21,true));
        heading.Children.Add(Label(()=>L.Get("run.pending_count",tasks.Count),13,false,true,new(0,6,0,0)));
        heading.Children.Add(Label(()=>recordedAt.HasValue
            ? L.Get("run.pending_recorded",recordedAt.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture))
            : L.Get("run.pending_note"),12,false,true,new(0,8,0,0)));
        DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        var footer=new StackPanel{Margin=new(0,16,0,0)};
        footer.Children.Add(new Border{Height=1,Background=(Brush)FindResource("Line"),Margin=new(0,0,0,12)});
        CloseAction=new Button{MinWidth=90,HorizontalAlignment=HorizontalAlignment.Right,IsCancel=true};
        L.Bind(CloseAction,ContentControl.ContentProperty,"run.details_close");
        CloseAction.Click+=(_,_)=>Close();footer.Children.Add(CloseAction);
        DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);
        var list=new StackPanel();
        foreach(var group in tasks.GroupBy(t=>(t.Group,t.Source)))
        {
            list.Children.Add(Label(()=>L.Get("run.pending_group."+group.Key.Group)
                +(group.Key.Source.Length>0?" · "+L.Translate(group.Key.Source):""),13,true,true,new(0,12,0,6)));
            foreach(var task in group)
            {
                var grid=new Grid();
                grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
                grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
                var body=new StackPanel{Margin=new(0,0,18,0)};
                var title=Label(()=>string.IsNullOrWhiteSpace(task.Title)?L.Get("run.pending_unnamed"):L.Translate(task.Title),14,true);
                titles.Add(title);body.Children.Add(title);
                body.Children.Add(Label(()=>Reason(task),12,false,true,new(0,6,0,0)));
                grid.Children.Add(body);
                var progress=Label(()=>Progress(task),13,false,true);progress.MaxWidth=170;
                Grid.SetColumn(progress,1);grid.Children.Add(progress);
                var row=new Border{Child=grid,Padding=new(0,12,0,14),BorderThickness=new(0,0,0,1)};
                row.SetResourceReference(Border.BorderBrushProperty,"Line");list.Children.Add(row);
            }
        }
        if(tasks.Count==0)list.Children.Add(Label(()=>L.Get("run.pending_missing"),14));
        TaskScroll=new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new(0,0,8,0)};
        layout.Children.Add(TaskScroll);
        var frame=new Border{Padding=new(24),Child=layout};
        frame.SetResourceReference(Border.BackgroundProperty,"Surface");Content=frame;
        DailyDialogs.Prepare(this);
    }
    private static TextBlock Label(Func<string> text,double size,bool bold=false,bool muted=false,Thickness? margin=null)
    {
        var label=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=size,
            FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,Margin=margin??new Thickness(0)};
        label.SetResourceReference(TextBlock.ForegroundProperty,muted?"MutedInk":"Ink");
        L.Bind(label,TextBlock.TextProperty,text);return label;
    }
    internal static string Progress(QueueTaskDetail task) => task.Progress.HasValue && task.Required.HasValue
        ? L.Get("run.pending_progress",task.Progress.Value,task.Required.Value)
        : L.Get("run.pending_progress_unknown");
    internal static string Reason(QueueTaskDetail task)
    {
        if(!string.IsNullOrWhiteSpace(task.Reason))return L.Describe(task.Reason);
        if(task.Status=="claimable")return L.Get("run.pending_claimable");
        if(task.Status=="locked")return L.Get("run.pending_locked");
        if(task.Status=="pending" && task.Progress.HasValue && task.Required.HasValue && task.Required>task.Progress)
            return L.Get("run.pending_remaining",task.Required.Value-task.Progress.Value);
        return L.Get("run.pending_unknown");
    }
}
