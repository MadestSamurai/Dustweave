using System.IO;
using BD2Sichuan.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using BD2Sichuan;
namespace BD2Sichuan.Desktop;
public partial class SichuanWindow : Window
{
 public bool HostedAutomationEnabled => automation.Active || automationPreparing || connecting;
    private bool automationPreparing;

    private WindowLanguage? language;
    private readonly SichuanLiveFiles files;
    private readonly string settingsPath;
    private bool connecting;
    private readonly SichuanControlLink link;
    private string drawKey="";
    private readonly SichuanAutomationController automation=new();
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    private readonly Dictionary<string,BitmapImage> images=new();
    private bool enabled,reading,closed;private string activeKey="",workKey="";
    private CancellationTokenSource? solve;
    private SichuanSnapshot? current,solvedBoard;private SichuanSolution? result,solvedResult;
    public SichuanWindow(string? root=null)
    {
        InitializeComponent();BD2.Distribution.DistributionNotice.Attach(this,LanguageBox);files=new(root??SichuanIdentity.BoardRoot);
        settingsPath=System.IO.Path.Combine(files.Root,"settings.json");
        files.IntervalMilliseconds=SichuanSettings.Read(settingsPath).IntervalMilliseconds;
        IntervalBox.Text=files.IntervalMilliseconds.ToString();
        link=new(files);
        language=new(this,LanguagePreference.Read(files.Root));LanguageBox.SelectedIndex=language.Catalog.Language=="zh-CN"?0:1;
        timer.Tick+=async(_,_)=>await RefreshAsync();
        Closing+=(_,e)=>{if(connecting){e.Cancel=true;ConnectionText.Text="正在完成连接，请稍后关闭。";}};
        Closed+=(_,_)=>{StopAutomation("window_closed");closed=true;enabled=false;timer.Stop();solve?.Cancel();link.Dispose();language?.Dispose();};
    }
    private void LanguageChanged(object sender,SelectionChangedEventArgs e)
    {
        if(language==null || LanguageBox.SelectedItem is not ComboBoxItem choice)return;
        try{LanguagePreference.Save(files.Root,(string)choice.Tag);language.Select((string)choice.Tag);}
        catch(Exception ex){ConnectionText.Text=ex.Message;}
    }
    private async void Connect_Click(object sender,RoutedEventArgs e)
    {
        if(connecting)return;connecting=true;ConnectButton.IsEnabled=false;
        try
        {
            ConnectionText.Text="正在连接游戏…";
            ConnectionText.Text=await Task.Run(()=>new SichuanConnection().Connect(message=>Dispatcher.Invoke(()=>ConnectionText.Text=message)));
            if(!closed&&!enabled)await EnableHints();
        }
        catch(Exception ex){ConnectionText.Text=ex.GetBaseException().Message;}
        finally{connecting=false;if(!closed)ConnectButton.IsEnabled=true;}
    }
    private async Task EnableHints()
    {
        try{link.Enable(true);enabled=true;ToggleButton.Content="暂停提示";timer.Start();await RefreshAsync();}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){enabled=false;timer.Stop();StatusText.Text="无法启动提示："+e.Message;StopAutomation("capture_enable_failed");}
    }
    private bool ApplyInterval()
    {
        if(!int.TryParse(IntervalBox.Text,out var value)||!SichuanRunEngine.ValidInterval(value))
        {IntervalText.Text="请输入 50–60000 毫秒的整数，原间隔保持。";return false;}
        try
        {
            new SichuanSettings{IntervalMilliseconds=value}.Save(settingsPath);link.SetInterval(value);
            IntervalText.Text=$"已设为 {value} ms · 运行中可调整";return true;
        }
        catch(Exception e){IntervalText.Text="保存失败："+e.Message;return false;}
    }
    private void Interval_Click(object sender,RoutedEventArgs e)=>ApplyInterval();
    private void Interval_KeyDown(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Enter){ApplyInterval();e.Handled=true;}}
    private void Topmost_Changed(object sender,RoutedEventArgs e)=>Topmost=((CheckBox)sender).IsChecked==true;
    private async void Toggle_Click(object sender,RoutedEventArgs e)
    {
        if(!enabled){await EnableHints();return;}
        enabled=false;ToggleButton.Content="开始提示";timer.Stop();solve?.Cancel();workKey="";
        StopAutomation("capture_disabled");
        try{link.Enable(false);Invalidate("提示已暂停。");}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){StatusText.Text="写入停用状态失败，租约将自然到期："+ex.Message;}
    }
    private async void Step_Click(object sender,RoutedEventArgs e)=>await StartAutomation(false);
    private async void Auto_Click(object sender,RoutedEventArgs e)=>await StartAutomation(true);
    private void Stop_Click(object sender,RoutedEventArgs e)=>StopAutomation("user_stop");
    private async Task StartAutomation(bool automatic)
    {
        if(automationPreparing)return;automationPreparing=true;
        try { await StartAutomationCore(automatic); } finally { automationPreparing=false; }
    }
    private async Task StartAutomationCore(bool automatic)
    {
        if(!ApplyInterval())return;
        await EnableHints();
        if(!enabled)return;
        if(current==null||SichuanLiveFiles.Invalid(current,DateTime.UtcNow)!=null){AutomationText.Text="请先连接游戏，等待最新盘面后再开启。";return;}
        try
        {
            solve?.Cancel();result=null;workKey="";
            var command=automation.Start(automatic,current,DateTime.UtcNow);command.IntervalMilliseconds=files.IntervalMilliseconds;link.Start(command);ShowAutomation();
        }
        catch(Exception e){StopAutomation("启动失败："+e.Message);}
    }
    private void StopAutomation(string reason)
    {
        automation.Stop(reason);link.Stop(reason);ShowAutomation();
    }
    private void ShowAutomation()
    {
        AutomationText.Text=$"{automation.Status}"+(automation.CompletedPairs>0?$" · 已确认 {automation.CompletedPairs} 对":"");
        StepButton.IsEnabled=AutoButton.IsEnabled=!automation.Active;StopButton.IsEnabled=automation.Active;
    }
    private void AdvanceAutomation(SichuanSnapshot snapshot)
    {
        if(!automation.Active)return;
        automation.Observe(snapshot,DateTime.UtcNow);
        if(!automation.Active)link.Stop("run_finished");
        ShowAutomation();
    }
    private void TransientFailure(string reason)
    {
        // A single file race or delayed UI read cannot revoke a healthy in-process run.
        StatusText.Text=reason;HintText.Text="等待新盘面，自动重试中";RouteText.Text="";
        solve?.Cancel();result=null;workKey="";Draw(null);
    }
    private void Invalidate(string reason)
    {
        if(automation.Active)StopAutomation(reason);
        StatusText.Text=reason;HintText.Text="等待有效盘面";DetailText.Text="旧提示已撤销。";RouteText.Text="";
        solve?.Cancel();result=null;workKey="";Draw(null);
    }
    private async Task RefreshAsync()
    {
        if(reading||!enabled||closed)return;reading=true;
        try
        {
            var snapshot=await Task.Run(files.Read);
            if(closed||!enabled)return;
            if(snapshot==null){TransientFailure("等待新版采集组件。");return;}
            var invalid=SichuanLiveFiles.Invalid(snapshot,DateTime.UtcNow);
            if(invalid!=null){TransientFailure(invalid);return;}
            current=snapshot;AdvanceAutomation(snapshot);await AcceptAsync(snapshot);
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {TransientFailure(e is FileNotFoundException?"请先连接游戏，并进入连连看。":"盘面暂未读到，将自动重试："+e.Message);}
        finally{reading=false;}
    }
    private Task AcceptAsync(SichuanSnapshot s)
    {
        var key=SichuanLiveFiles.Key(s);
        if(key!=activeKey){solve?.Cancel();activeKey=key;result=null;workKey="";}
        var state=s.State switch {"ready"=>"实时同步", "paused"=>"游戏已暂停", "shuffling"=>"正在洗牌", "waiting_animation"=>"等待动画结束", "finished"=>"本盘结束", "disabled"=>"等待采集启用", "unsupported_client"=>"采集组件不支持当前客户端", "capture_error"=>"采集重试中", _=>"等待连连看盘面"};
        StatusText.Text=s.Cells.Length==0?state:$"{state} · 关卡 {s.LevelGroup}/{s.Level} · 剩余 {s.RemainingSeconds:F1} 秒";
        MetaText.Text=$"采集 {s.CaptureMilliseconds:F1} ms · {s.Width} × {s.Height} · 每对立即确认 · 间隔 {files.IntervalMilliseconds} ms。{s.NetworkState}";
        if(s.State!="ready")
        {
            HintText.Text=state;DetailText.Text=s.Error.Length>0?s.Error:"盘面可操作后自动更新下一对。";RouteText.Text="";Draw(null);return Task.CompletedTask;
        }
        if(s.Cells.Length==0){Invalidate("等待完整盘面。");return Task.CompletedTask;}
        if(automation.Active)
        {
            solve?.Cancel();result=null;workKey="";
            HintText.Text="游戏内自动执行中";
            DetailText.Text=$"已确认 {automation.CompletedPairs} 对 · 最近求解 {s.Run?.LastPlanningMilliseconds:F2} ms · 点击至回读 {s.Run?.LastPairMilliseconds:F2} ms";
            RouteText.Text="暂停、洗牌和输入锁定会自动等待；普通消除动画可以重叠。";Draw(null);return Task.CompletedTask;
        }
        if(result!=null){ShowResult(result);return Task.CompletedTask;}
        Draw(null);
        if(workKey==key)return Task.CompletedTask;
        if(solvedBoard!=null&&solvedResult!=null&&s.SessionId==solvedBoard.SessionId&&s.Generation==solvedBoard.Generation&&s.Width==solvedBoard.Width&&s.Height==solvedBoard.Height)
        {
            var continued=SichuanSolver.Continue(s.Width,s.Height,solvedBoard.Cells,s.Cells,solvedResult);
            if(continued!=null){Complete(s,key,continued);return Task.CompletedTask;}
        }
        workKey=key;solve?.Cancel();solve=new();var token=solve.Token;
        HintText.Text="正在寻找下一对…";DetailText.Text="先返回合法步骤，再尝试完整路线。";
        _=Task.Run(()=>SichuanSolver.Solve(s.Width,s.Height,s.Cells,750,token,move=>Dispatcher.BeginInvoke(()=>
        {if(!closed&&!token.IsCancellationRequested&&activeKey==key&&current?.State=="ready"){result=new("searching",new[]{move},0,0);ShowResult(result);}}),s.ElapsedSeconds,s.TimerBonusSeconds),token)
            .ContinueWith(t=>Dispatcher.BeginInvoke(()=>
            {
                if(closed||token.IsCancellationRequested||activeKey!=key)return;
                if(t.IsCompletedSuccessfully)Complete(s,key,t.Result);
                else if(t.Exception!=null){workKey="";Invalidate("求解未完成："+t.Exception.GetBaseException().Message);}
            }),TaskScheduler.Default);
        return Task.CompletedTask;
    }
    private void Complete(SichuanSnapshot s,string key,SichuanSolution value)
    {
        if(activeKey!=key)return;result=value;solvedBoard=s;solvedResult=value;
        if(current?.State=="ready")ShowResult(value);
    }
    private string Position(int index)=>$"{index/(current?.Width??1)+1}行{index%(current?.Width??1)+1}列";
    private void ShowResult(SichuanSolution value)
    {
        var m=value.Moves.FirstOrDefault();
        HintText.Text=m==null?"当前没有可提示的配对":$"① {Position(m.First)}  →  ② {Position(m.Second)}";
        DetailText.Text=value.Status switch {
            "route_found"=>value.Moves.Length==0?"盘面已清空。":$"已找到当前静态盘面的 {value.Moves.Length} 步路线。连击奖励或洗牌发生后自动重算。",
            "searching"=>"这一对合法；正在检查后续路线。",
            "budget_exhausted"=>"限时内尚未找到完整路线，当前仅为合法下一步。",
            _=>"当前静态盘面没有完整路线；连击奖励或游戏洗牌后会重新检查。"};
        RouteText.Text=string.Join("    ",value.Moves.Skip(1).Take(3).Select((v,i)=>$"随后{i+1}：{Position(v.First)} ↔ {Position(v.Second)}"));
        if(value.Reused)MetaText.Text+=" · 已接续原路线";else if(value.Milliseconds>0)MetaText.Text+=$" · 求解 {value.Milliseconds:F0} ms / {value.Nodes} 节点";
        Draw(m);
    }
    private BitmapImage? ImageFor(string file)
    {
        if(file.Length==0||System.IO.Path.GetFileName(file)!=file)return null;
        if(images.TryGetValue(file,out var bitmap))return bitmap;
        try{var path=System.IO.Path.Combine(files.Root,"images",file);using var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);var img=new BitmapImage();img.BeginInit();img.CacheOption=BitmapCacheOption.OnLoad;img.StreamSource=f;img.EndInit();img.Freeze();if(images.Count>256)images.Clear();return images[file]=img;}catch(Exception e) when(e is IOException or UnauthorizedAccessException or NotSupportedException){return null;}
    }
    private void Draw(SichuanMove? move)
    {
        var s=current;
        string nextDraw=(s==null?"":SichuanLiveFiles.Key(s)+":"+s.State+":"+s.SelectedIndex+":"+string.Join(",",s.ImageFiles))+":"+(move==null?"":string.Join(",",move.Path));
        if(nextDraw==drawKey)return;drawKey=nextDraw;
        language?.Forget(BoardCanvas);BoardCanvas.Children.Clear();
        if(s==null||s.Cells.Length==0){EmptyText.Visibility=Visibility.Visible;return;}
        EmptyText.Visibility=Visibility.Collapsed;double cell=68,pad=28;
        BoardCanvas.Width=s.Width*cell+pad;BoardCanvas.Height=s.Height*cell+pad;
        for(int x=0;x<s.Width;x++)Label((x+1).ToString(),pad+x*cell,0,cell,24,12,Brushes.SlateGray);
        for(int y=0;y<s.Height;y++)Label((y+1).ToString(),0,pad+y*cell,24,cell,12,Brushes.SlateGray);
        for(int i=0;i<s.Cells.Length;i++)
        {
            var id=s.Cells[i];double x=pad+(i%s.Width)*cell,y=pad+(i/s.Width)*cell;
            bool marked=move!=null&&(i==move.First||i==move.Second);
            var bg=id==0?"#F6F8FB":id/1000 is 10 or 12?"#E7EBF0":"#F8FAFD";
            var border=new Border{Width=cell-4,Height=cell-4,CornerRadius=new(6),Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(marked?"#DBEDFF":bg)),BorderBrush=marked?new SolidColorBrush(Color.FromRgb(0,90,180)):Brushes.LightGray,BorderThickness=new(marked?3:1)};
            Canvas.SetLeft(border,x+2);Canvas.SetTop(border,y+2);BoardCanvas.Children.Add(border);
            if(id==0)continue;
            var img=i<s.ImageFiles.Length?ImageFor(s.ImageFiles[i]):null;
            if(img!=null){var image=new Image{Source=img,Width=cell-14,Height=cell-14,Stretch=Stretch.Uniform};Canvas.SetLeft(image,x+7);Canvas.SetTop(image,y+7);BoardCanvas.Children.Add(image);}
            else{string label=(id/1000) switch{1=>"服装",2=>"食物",10=>"岩石",11=>"钥匙",12=>"锁",13=>"计时",_=>"?"};Label(label+"\n"+(id%1000),x+3,y+4,cell-6,36,12,Brushes.DarkSlateGray);}
            if(marked)Label(i==move!.First?"①":"②",x+4,y+cell-28,26,26,20,new SolidColorBrush(Color.FromRgb(0,70,145)));
            else if(i==s.SelectedIndex)Label("已选",x,y+cell-20,cell,20,11,Brushes.DarkBlue);
        }
        if(move!=null){var line=new Polyline{Stroke=new SolidColorBrush(Color.FromRgb(0,104,200)),StrokeThickness=3,StrokeDashArray=new DoubleCollection{2,1},IsHitTestVisible=false};foreach(var i in move.Path)line.Points.Add(new(pad+(i%s.Width+.5)*cell,pad+(i/s.Width+.5)*cell));BoardCanvas.Children.Add(line);}
    }
    private void Label(string value,double x,double y,double w,double h,int font,Brush color)
    {var t=new TextBlock{Text=value,Width=w,Height=h,FontSize=font,Foreground=color,TextAlignment=TextAlignment.Center,Padding=new(0,2,0,0)};Canvas.SetLeft(t,x);Canvas.SetTop(t,y);BoardCanvas.Children.Add(t);language?.Include(t);}
    public async Task SmokeAsync(string output)
    {
        timer.Stop(); TestTransport.Start(files.Root,"latest.json|enabled-until.txt|run-command.json|execution-lease.json|network-status.json");
        LanguageBox.SelectedIndex=0;language!.Select("zh-CN");
        Directory.CreateDirectory(output);Show();current=new(){SessionId="smoke",Generation=1,Width=8,Height=6,Cells=new[]{0,0,0,0,0,0,0,0,0,1001,1002,11001,11001,1002,1001,0,0,2001,12001,10001,10001,12001,2001,0,0,2002,13001,2003,2003,13001,2002,0,0,1003,1004,1005,1005,1004,1003,0,0,0,0,0,0,0,0,0},State="ready",LevelGroup=1,Level=3,RemainingSeconds=86.4f};
        activeKey=SichuanLiveFiles.Key(current);var value=SichuanSolver.Solve(current.Width,current.Height,current.Cells);Complete(current,activeKey,value);await AcceptAsync(current);
        foreach(var size in new[]{(1050d,800d),(720d,640d)}){Width=size.Item1;Height=size.Item2;UpdateLayout();await Task.Delay(100);var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var f=File.Create(System.IO.Path.Combine(output,$"sichuan-{size.Item1}.png"));encoder.Save(f);}
        var assertions=new List<string>();
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);assertions.Add(label);}
        Check(IntervalBox.Text=="1000"&&files.IntervalMilliseconds==1000,"fresh standalone default is 1000ms");
        IntervalBox.Text="250";Check(ApplyInterval()&&files.IntervalMilliseconds==250,"custom interval applied");
        Check(SichuanSettings.Read(settingsPath).IntervalMilliseconds==250,"interval survives settings reload");
        IntervalBox.Text="oops";Check(!ApplyInterval()&&files.IntervalMilliseconds==250,"invalid interval preserves active setting");
        IntervalBox.Text="1000";ApplyInterval();
        foreach(var state in new[]{"paused","shuffling","waiting_animation","finished","waiting_board"})
        {
            current.State=state;await AcceptAsync(current);
            Check(!BoardCanvas.Children.OfType<Polyline>().Any(),state+" withdraws path");
            Check(!HintText.Text.Contains("①"),state+" withdraws coordinate prompt");
        }
        current.State="ready";await AcceptAsync(current);Check(BoardCanvas.Children.OfType<Polyline>().Any(),"same-board resume restores valid route");
        Invalidate("盘面心跳已过期");Check(!BoardCanvas.Children.OfType<Polyline>().Any()&&result==null,"stale snapshot clears hint and pending result");
        Check(!enabled,"UI opens with capture disabled");
        Check(!automation.Active&&!StopButton.IsEnabled,"automation disabled by default");
        current.ExecutionProtocol=3;var smokeCommand=automation.Start(true,current,DateTime.UtcNow);link.Start(smokeCommand);ShowAutomation();Check(StopButton.IsEnabled&&!AutoButton.IsEnabled,"active run exposes stop and prevents duplicate start");
        TransientFailure("模拟短暂读取失败");Check(automation.Active,"transient file failure does not revoke active run");
        current.Run=new(){OwnerId=automation.OwnerId,State="waiting",Reason="paused",ConfirmedPairs=3};AdvanceAutomation(current);
        Check(automation.Active&&AutomationText.Text.Contains("恢复后"),"game pause remains armed in UI");
        var ownerBeforeLanguage=automation.OwnerId;
        drawKey="";Draw(null);var tracked=language!.TrackedControlCount;
        for(int i=0;i<20;i++){drawKey="";Draw(null);}
        Check(language.TrackedControlCount==tracked,"board redraw releases obsolete localization listeners");
        LanguageBox.SelectedIndex=1;ShowAutomation();drawKey="";Draw(null);UpdateLayout();
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        Check(BoardCanvas.Children.OfType<TextBlock>().Any(t=>t.Text.StartsWith("Costume")),"dynamic board labels localize after creation: "+string.Join(" | ",BoardCanvas.Children.OfType<TextBlock>().Select(t=>t.Text)));
        Check(BoardCanvas.Children.OfType<TextBlock>().Where(t=>t.Text.StartsWith("Costume")).All(t=>
        {
            var text=new FormattedText(t.Text,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(t.FontFamily,t.FontStyle,t.FontWeight,t.FontStretch),t.FontSize,t.Foreground,1.0);
            return t.Text.Contains('\n') && text.WidthIncludingTrailingWhitespace<=t.Width && text.Height+t.Padding.Top<=t.Height;
        }),"English fallback tile category and identifier fit without clipping");
        Check(ConnectButton.Content.ToString()=="Connect game" && AutoButton.Content.ToString()=="Auto-play round","English action labels");
        Check(AutomationText.Text.Contains("continues automatically") && automation.Active && automation.OwnerId==ownerBeforeLanguage,"live language switch preserves active run and translates pause");
        Check(LanguagePreference.Read(files.Root)=="en-US" && files.IntervalMilliseconds==1000,"language preference saved independently of interval");
        foreach(var size in new[]{(1050d,800d),(720d,640d)}){Width=size.Item1;Height=size.Item2;UpdateLayout();await Task.Delay(100);var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var f=File.Create(System.IO.Path.Combine(output,$"sichuan-en-{size.Item1}.png"));encoder.Save(f);}
        LanguageBox.SelectedIndex=0;ShowAutomation();Check(ConnectButton.Content.ToString()=="连接游戏" && automation.OwnerId==ownerBeforeLanguage,"switch back preserves active run");
        StopAutomation("user_stop");Check(!automation.Active&&!StopButton.IsEnabled,"stop releases execution immediately");
        Check(System.Text.Json.JsonSerializer.Deserialize<SichuanActionLease>(TestTransport.Text(System.IO.Path.Combine(files.Root,"execution-lease.json")))!.UntilUtcTicks==0,"stop writes revoked lease");
        Close();Check(closed&&!timer.IsEnabled,"closing child stops only local polling");
        Check(TestTransport.Text(System.IO.Path.Combine(files.Root,"enabled-until.txt"))=="0","closing child releases local capture lease");
        File.WriteAllText(System.IO.Path.Combine(output,"results.json"),System.Text.Json.JsonSerializer.Serialize(new{status="pass",assertions,gameRequests=0,injection=false},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    }
}
