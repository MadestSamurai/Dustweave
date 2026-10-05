using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BD2SecretVision.Localization;
namespace BD2SecretVision.Desktop;

public partial class MainWindow : Window
{
 public bool HostedAutomationEnabled => running || connecting;

    private readonly string root;
    private readonly bool smoke;
    private readonly Connection connection;
    private readonly DispatcherTimer timer;
    private UserPreferences preferences=new();
    private Catalog language=new(new UserPreferences().DisplayLanguage);
    private Progress progress=new();
    private Snapshot? snapshot;
    private GameProcess? game;
    private bool initializing=true, connecting, running, fresh, closing, closeAllowed;
    private CancellationTokenSource? cancellation;
    private Task? currentTask;
    private string status="idle", diagnostic="", validation="";
    private DateTimeOffset nextFind;
    private readonly CheckBox[] stages;

    public MainWindow(string root,bool smoke)
    {
        this.root=root;this.smoke=smoke;connection=new(root);
        InitializeComponent();stages=[Stage1,Stage2,Stage3,Stage4,Stage5];
        try{preferences=UserPreferences.Load(root);}catch(Exception e){diagnostic=e.ToString();status=e.Message;}
        language.Select(preferences.DisplayLanguage);LanguageChoice.SelectedIndex=preferences.DisplayLanguage=="zh-CN"?0:1;
        foreach(var pair in stages.Select((box,i)=>(box,i)))pair.box.IsChecked=preferences.Settings.Stages.Contains(pair.i+1);
        Target.SelectedIndex=preferences.Settings.TargetPercent==100?0:1;Retry.IsChecked=preferences.Settings.Retry;
        MaxAttempts.Text=preferences.Settings.MaxAttempts.ToString();DataPath.Text=root;
        initializing=false;ApplyLanguage();ValidateSettings(false);
        timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};timer.Tick+=(_,_)=>Poll();
        Loaded+=async(_,_)=>{Poll();timer.Start();if(smoke)await SmokeAsync();};
        Closing+=WindowClosing;
    }

    private void ApplyLanguage()
    {
        Title=language["title"]+" "+App.Version;Subtitle.Text=language["subtitle"];
        ConnectButton.Content=language["connect"];StartButton.Content=language["start"];StopButton.Content=language["stop"];
        BoardTitle.Text=language["board"];EmptyBoard.Text=language["empty"];
        ClaimedLegend.Text=language["claimed"];PlayerLegend.Text=language["player"];EnemyLegend.Text=language["enemy"];ItemLegend.Text=language["item"];
        StagesTitle.Text=language["stages"];StagesHelp.Text=language["stages-help"];TargetTitle.Text=language["target"];
        Target100.Content=language["target100"];Target80.Content=language["target80"];Retry.Content=language["retry"];
        AttemptsTitle.Text=language["attempts"];AttemptsHelp.Text=language["attempts-help"];OperationTitle.Text=language["operation"];OperationHelp.Text=language["operation-help"];
        Diagnostics.Header=language["diagnostics"];DiagnosticHelp.Text=language["diagnostic-help"];Attribution.Text=language["attribution"];AboutButton.Content=language["about"];
        System.Windows.Automation.AutomationProperties.SetName(MaxAttempts,language["attempts"]);
        System.Windows.Automation.AutomationProperties.SetName(Target,language["target"]);
        System.Windows.Automation.AutomationProperties.SetName(Board,language["board"]);
        foreach(var pair in stages.Select((box,i)=>(box,i)))System.Windows.Automation.AutomationProperties.SetName(pair.box,language["stages"]+" "+(pair.i+1));
        Render();
    }
    private void LanguageChanged(object sender,SelectionChangedEventArgs e)
    {
        if(initializing)return;
        string selected=AppDomain.CurrentDomain.GetData("BD2Daily.HostedLanguage") as string ?? (LanguageChoice.SelectedIndex==0?"zh-CN":"en-US");language.Select(selected);ApplyLanguage();
        try{preferences.SelectLanguage(root,selected);}catch(Exception error){ShowError(error);}
    }
    private void SettingsChanged(object sender,RoutedEventArgs e){if(!initializing)ValidateSettings(true);}
    private Settings? ValidateSettings(bool save)
    {
        try{
            if(!int.TryParse(MaxAttempts.Text,out int attempts))throw new ArgumentException("invalid-attempts");
            var settings=new Settings{Stages=stages.Select((box,i)=>(box,i)).Where(x=>x.box.IsChecked==true).Select(x=>x.i+1).ToArray(),
                Retry=Retry.IsChecked==true,TargetPercent=Target.SelectedIndex==0?100:80,MaxAttempts=attempts};
            settings.Validate();validation="";
            if(save){preferences.Settings=settings;preferences.Save(root);}
            Render();return settings;
        }catch(Exception error){validation=error is ArgumentException?error.Message:ErrorKind.Status(error);if(error is not ArgumentException)ShowError(error);SettingsError.Text=language[validation];SetEnabled();return null;}
    }
    private void Poll()
    {
        if(smoke)return;
        try{
            if(DateTimeOffset.UtcNow>=nextFind){nextFind=DateTimeOffset.UtcNow.AddSeconds(2);game=Connection.Find();}
            snapshot=connection.Read()??snapshot;fresh=Connection.Fresh(snapshot,game,DateTimeOffset.UtcNow);
            if(!fresh&&running)diagnostic="Waiting for a fresh component heartbeat; the runner validates the lease and process identity.";
            Render();
        }catch(Exception error){fresh=false;ShowError(error);}
    }
    private void Render()
    {
        if(initializing)return;
        Board.Board=snapshot?.Board;EmptyBoard.Visibility=snapshot?.Board==null?Visibility.Visible:Visibility.Collapsed;
        LiveState.Text=language[fresh?"live":snapshot==null?"waiting":"stale"];
        BoardStats.Text=snapshot?.Board is Board b?language.Format("stats",b.Percent,b.Remaining):"—";
        RunStatus.Text=language[status];RunStatus.Foreground=(Brush)FindResource(status is "blocked" or "file-access" or "permission"||diagnostic.Length>0&&!running?"Error":status=="completed"?"Success":"Ink");
        RunDetail.Text=progress.Stage>0?language.Format("progress",progress.Stage,progress.Attempt,progress.Cleared.Count==0?language["none"]:string.Join(", ",progress.Cleared)):language["open-secret-vision"];
        DiagnosticText.Text=diagnostic;SettingsError.Text=validation.Length>0?language[validation]:"";SetEnabled();
    }
    private void SetEnabled()
    {
        bool busy=running||connecting;SettingsPanel.IsEnabled=!busy;ConnectButton.IsEnabled=!busy;
        StartButton.IsEnabled=!busy&&fresh&&validation.Length==0&&(snapshot?.Board!=null||snapshot?.UIs.Any(u=>u.Type=="HopscotchMainUI")==true);
        StopButton.IsEnabled=busy;MaxAttempts.IsEnabled=Retry.IsChecked==true;
    }
    private void ShowError(Exception error){status=ErrorKind.Status(error);diagnostic=error.ToString();Files.Diagnose(root,"desktop",error);Render();}
    private async void ConnectClick(object sender,RoutedEventArgs e)
    {
        if(smoke||running||connecting)return;
        connecting=true;cancellation=new();diagnostic="";SetEnabled();
        try{
            currentTask=connection.Connect(text=>Dispatcher.Invoke(()=>{status=text;Render();}),cancellation.Token);
            await currentTask;status="connected";nextFind=default;Poll();
        }catch(OperationCanceledException){status="stopped";}catch(Exception error){ShowError(error);}
        finally{connecting=false;cancellation.Dispose();cancellation=null;currentTask=null;Render();}
    }
    private static string ProgressDiagnostics(Progress value)=>string.Join("\n\n",new[]{value.Detail,value.Warning}.Where(x=>!string.IsNullOrWhiteSpace(x)));
    private async void StartClick(object sender,RoutedEventArgs e)
    {
        if(smoke||running||connecting)return;
        var settings=ValidateSettings(true);if(settings==null)return;
        running=true;cancellation=new();diagnostic="";status="starting";progress=new();SetEnabled();
        var automation=new Automation(connection);
        automation.Changed+=value=>Dispatcher.BeginInvoke(()=>{progress=value;status=value.Status;diagnostic=ProgressDiagnostics(value);Render();});
        try{currentTask=Task.Run(()=>automation.Run(settings,cancellation.Token));await currentTask;progress=automation.Progress;status=progress.Status;diagnostic=ProgressDiagnostics(progress);}
        catch(Exception error){ShowError(error);}
        finally{running=false;cancellation.Dispose();cancellation=null;currentTask=null;Render();}
    }
    private void StopClick(object sender,RoutedEventArgs e){cancellation?.Cancel();status="stopping";Render();}
    private async void WindowClosing(object? sender,CancelEventArgs e)
    {
        if(closeAllowed){timer.Stop();return;}
        if(currentTask==null){timer.Stop();return;}
        e.Cancel=true;if(closing)return;closing=true;StopClick(this,new());
        try{await currentTask;}catch{}
        closeAllowed=true;Close();
    }
    private Window AboutWindow()
    {
        var body=new StackPanel{Margin=new Thickness(22)};
        body.Children.Add(new TextBlock{Text=language["attribution"],FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
        body.Children.Add(new TextBlock{Text=language["notice"],TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,14)});
        body.Children.Add(new TextBox{Text="https://github.com/MadestSamurai/bd2-secret-vision",IsReadOnly=true});
        body.Children.Add(new TextBox{Text="https://github.com/MadestSamurai/bd2-secret-vision/releases",IsReadOnly=true,Margin=new Thickness(0,6,0,0)});
        var window=new Window{Owner=this,Title=language["about"],Width=560,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=body,Background=(Brush)FindResource("Surface"),ResizeMode=ResizeMode.NoResize};
        var button=new Button{Content=language["close"],HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};button.Click+=(_,_)=>window.Close();body.Children.Add(button);return window;
    }
    private void AboutClick(object sender,RoutedEventArgs e)=>AboutWindow().ShowDialog();

    private async Task SmokeAsync()
    {
        var checks=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
        try{
            timer.Stop();Check(!StartButton.IsEnabled&&!StopButton.IsEnabled,"offline cannot start");Check(preferences.Settings.Stages.SequenceEqual(new[]{1})&&Target.SelectedIndex==0,"conservative first-stage defaults");
            foreach(var code in new[]{"zh-CN","en-US"}){
                LanguageChoice.SelectedIndex=code=="zh-CN"?0:1;Check(language.Language==code,"language switch "+code);
                Capture(code+"-empty");
                int width=100,height=80;var rows=Enumerable.Range(0,height).Select(y=>new string(Enumerable.Range(0,width).Select(x=>x==0||y==0||x==width-1||y==height-1?'#':x<28||y<12?'C':'.').ToArray())).ToArray();
                snapshot=new(){Protocol=1,AtUtc=DateTimeOffset.UtcNow,Session="synthetic",Stage=2,Board=new(){Id=1,Width=width,Height=height,CellSize=4,Rows=rows,State="Playing",Percent=42.7,Remaining=86.3,Player=new(){X=28,Y=18,U=-88,V=-88},Bodies=[new(){Position=[30,35],Radius=18},new(){Position=[50,35],Radius=16}],Items=[new(){Position=[50,-60],Type="HopscotchItemSpeedUp"}]}};
                fresh=true;progress=new(){Stage=2,Attempt=3,Cleared=[1]};status="playing";Render();Check(StartButton.IsEnabled,"fresh playable board enables start "+code);
                Stage2.IsChecked=true;SettingsChanged(this,new());Check(UserPreferences.Load(root).Settings.Stages.SequenceEqual(new[]{1,2}),"selected stages persist "+code);
                MaxAttempts.Text="typing";Check(validation=="invalid-attempts"&&!StartButton.IsEnabled&&MaxAttempts.Text=="typing","invalid input retained "+code);MaxAttempts.Text="2";
                Target.SelectedIndex=1;Check(preferences.Settings.TargetPercent==80,"80 target persisted "+code);Target.SelectedIndex=0;
                running=true;Render();Check(!StartButton.IsEnabled&&StopButton.IsEnabled&&!SettingsPanel.IsEnabled,"controls locked during run "+code);
                var chosen=preferences.Settings.Stages.ToArray();LanguageChoice.SelectedIndex=code=="zh-CN"?1:0;Check(running&&preferences.Settings.Stages.SequenceEqual(chosen),"language does not restart "+code);LanguageChoice.SelectedIndex=code=="zh-CN"?0:1;
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture(code+"-running");
                running=false;Width=920;Height=720;Render();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture(code+"-compact");
                Check(Board.ActualWidth>200&&Board.ActualHeight>200,"board remains usable at minimum size "+code);
                var about=AboutWindow();about.Show();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture(code+"-about",(FrameworkElement)about.Content);about.Close();
                Width=1100;Height=860;
            }
            ShowError(new LocalFileException("publish file",Path.Combine(root,"control.json"),new UnauthorizedAccessException("denied")));
            Check(status=="file-access"&&DiagnosticText.Text.Contains("control.json"),"file failure classified with path");
            ShowError(new Exception("open process",new Win32Exception(5)));
            Check(status=="permission","process access failure remains distinct");
            fresh=false;Render();Check(!StartButton.IsEnabled,"stale board cannot start");
            cancellation=new();StopClick(this,new());Check(cancellation.IsCancellationRequested,"stop cancels immediately");cancellation.Dispose();cancellation=null;
            Check(!File.Exists(Path.Combine(root,"control.json")),"UI tests never command game");
            Files.Write(Path.Combine(root,"smoke.json"),new{status="passed",checks,gameConnected=false});Application.Current.Shutdown();
        }catch(Exception error){Files.Write(Path.Combine(root,"smoke.json"),new{status="failed",checks,error=error.ToString()});Application.Current.Shutdown(1);}
    }
    private void Capture(string name,FrameworkElement? element=null)
    {
        UpdateLayout();var view=element??(FrameworkElement)Content;view.UpdateLayout();var image=new RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32);
        var visual=new DrawingVisual();using(var drawing=visual.RenderOpen()){drawing.DrawRectangle(Background,null,new Rect(0,0,view.ActualWidth,view.ActualHeight));drawing.DrawRectangle(new VisualBrush(view),null,new Rect(0,0,view.ActualWidth,view.ActualHeight));}image.Render(visual);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));Directory.CreateDirectory(root);using var file=File.Create(Path.Combine(root,name+".png"));png.Save(file);
    }
}
