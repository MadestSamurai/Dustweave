using System.Collections.ObjectModel;
using BD2InfiniteGacha.Localization;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace BD2InfiniteGacha.Desktop;
public partial class MainWindow:Window
{
 public bool HostedAutomationEnabled => controller.Running || connecting;

    private readonly WindowLanguage language;
    private readonly IClientPort port;private readonly ClientController controller;private readonly Preferences preferences;private readonly string root;private readonly bool smoke;
    private readonly ObservableCollection<CostumeRow> costumes=new();private readonly ObservableCollection<RuleRowModel> conditions=new();
    private ListCollectionView? bView,aView;
    private RuleDraft draft=new();private Snapshot? snapshot;private Pool? pool;private string account="",catalogKey="",poolListKey="",resultKey="";private int shownTop,lastActivePool;private bool updating,connecting;private readonly DispatcherTimer timer;
    private readonly CancellationTokenSource lifetime=new();
    private readonly NotificationPreferences notificationPreferences;
    private readonly CompletionNotifications notifications;
    private NotificationOptions notificationOptions=new();
    private readonly List<NotificationChannel> smokeNotifications=new();
    public MainWindow(IClientPort port,string root,bool smoke)
    {
        InitializeComponent();BD2.Distribution.DistributionNotice.Attach(this,LanguageChoice);language=new WindowLanguage(this,LanguagePreference.Read(root));Ui.Catalog=language.Catalog;LanguageChoice.SelectedIndex=language.Catalog.Language=="zh-CN"?0:1;this.port=port;this.root=root;this.smoke=smoke;controller=new(port,record:(state,message)=>Diagnostics.Write(root,"desktop",state,message));preferences=new(root);controller.Stop();
        notificationPreferences=new(root);notifications=new(this);
        try{notificationOptions=notificationPreferences.Load();}catch(Exception e){Error(e);}
        NotifyWindows.IsChecked=notificationOptions.Windows;NotifyPopup.IsChecked=notificationOptions.Popup;NotifySound.IsChecked=notificationOptions.Sound;TestNotification.IsEnabled=notificationOptions.Any;
        bView=new ListCollectionView(costumes){Filter=x=>((CostumeRow)x).Group==2&&Matches(x)};aView=new ListCollectionView(costumes){Filter=x=>((CostumeRow)x).Group==1&&Matches(x)};
        BList.ItemsSource=bView;AList.ItemsSource=aView;Conditions.ItemsSource=conditions;
        timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(400)};timer.Tick+=(_,_)=>Poll();
        Loaded+=async(_,_)=>{Poll();timer.Start();if(smoke)await SmokeAsync();};
        Closing+=(_,_)=>{notifications.Dispose();language.Dispose();timer.Stop();lifetime.Cancel();try{controller.Stop();}catch{}};
    }
    private void Language_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(language==null||controller==null)return;
        string selected=AppDomain.CurrentDomain.GetData("BD2Daily.HostedLanguage") as string ?? (LanguageChoice.SelectedIndex==0?"zh-CN":"en-US");
        try{LanguagePreference.Save(root,selected);language.Select(selected);if(snapshot!=null)RefreshPoolList(snapshot);RebuildCostumes();updating=true;foreach(var rule in conditions)rule.Refresh();updating=false;ShowResult();Validate();}
        catch(Exception ex){Error(ex);}
    }
    private bool Matches(object item)
    {
        if(item is not CostumeRow row)return false;
        string q=Search.Text.Trim();if(q.Length>0&&!(row.Character+row.Name+row.Costume.Id).Contains(q,StringComparison.OrdinalIgnoreCase))return false;
        return Filter.SelectedIndex switch{1=>row.Available&&row.Costume.Level>=-1&&row.Costume.Level<5,2=>row.Costume.Level>=5,_=>true};
    }
    private void FilterView()
    {
        if(bView==null||aView==null)return;bView.Refresh();aView.Refresh();
        BEmpty.Visibility=bView.IsEmpty?Visibility.Visible:Visibility.Collapsed;AEmpty.Visibility=aView.IsEmpty?Visibility.Visible:Visibility.Collapsed;
        BEmpty.Text=draft.B.Length==0?"点击上方「未满服装 → B」，或添加服装。":"没有符合搜索／筛选的 B 组服装。";
        AEmpty.Text=draft.A.Length==0?"从 B 组选择服装，再点击「移入 A」。":"没有符合搜索／筛选的 A 组服装。";
        UpdateSummary();UpdateTransfers();
    }
    private void Filter_Changed(object sender,TextChangedEventArgs e)=>FilterView();
    private void Filter_Selection(object sender,SelectionChangedEventArgs e)=>FilterView();
    private void Poll()
    {
        try
        {
            var next=port.Read();controller.Poll(next);snapshot=next;
            if(!ClientController.Fresh(next,port.Find(),DateTime.UtcNow.Ticks)){ConnectionText.Text="未连接，或游戏状态尚未更新";if(controller.Notice.Length>0)StatusText.Text=controller.Notice;SetEnabled();return;}
            ConnectionText.Text=$"当前账号 · {(next!.Player.Length>0?next.Player:"等待登录")} · {(next.InventoryReady?"服装库已读取":"等待服装库")} · 库存 {next.InventoryCount} 件";
            if(next.Account.Length==64)
            {
                if(account!=next.Account){if(controller.Running)controller.Stop();account=next.Account;poolListKey="";catalogKey="";pool=null;lastActivePool=0;}
                RefreshPoolList(next);
                if(next.PoolId>0&&next.PoolId!=lastActivePool&&!controller.Running){var active=next.Pools.FirstOrDefault(p=>p.Id==next.PoolId);if(active!=null){lastActivePool=next.PoolId;Pools.SelectedItem=((IEnumerable<PoolChoice>)Pools.ItemsSource).FirstOrDefault(p=>p.Pool.Id==active.Id);}}
                if(pool!=null)
                {
                    var updated=next.Pools.FirstOrDefault(p=>p.Id==pool.Id);if(updated!=null){pool=updated;string inventory=pool.Key+"|"+string.Join(",",pool.Costumes.Select(c=>c.Id+":"+c.Level+":"+c.Character+":"+c.Name));if(catalogKey!=inventory){catalogKey=inventory;RebuildCostumes();ShowResult();}}
                }
            }
            if(!connecting)StatusText.Text=(controller.Notice.Length>0?controller.Notice:next.Message)+(next.Owner.Length>0?$"  本次 {next.Rolls} 次 · A {next.A} / B {next.B}":"");
            StatusText.Foreground=(Brush)FindResource(next.State=="error"?"Error":next.State=="matched"?"Success":"Ink");
            if(resultKey!=next.ResultKey){resultKey=next.ResultKey;ShowResult();if(next.ResultReady&&(controller.Running||next.State=="matched"))ResultExpander.IsExpanded=true;}
            SetEnabled();
            var matched=controller.TakeMatch();
            if(matched!=null){ResultExpander.IsExpanded=true;ShowResult();DeliverNotification(false,matched);}
        }catch(Exception e){Error(e);try{controller.Stop();}catch{}SetEnabled();}
    }
    private void Notification_Changed(object sender,RoutedEventArgs e)
    {
        if(notificationPreferences==null)return;
        var next=new NotificationOptions{Windows=NotifyWindows.IsChecked==true,Popup=NotifyPopup.IsChecked==true,Sound=NotifySound.IsChecked==true};
        try{notificationPreferences.Save(next);notificationOptions=next;TestNotification.IsEnabled=next.Any;}
        catch(Exception ex){NotifyWindows.IsChecked=notificationOptions.Windows;NotifyPopup.IsChecked=notificationOptions.Popup;NotifySound.IsChecked=notificationOptions.Sound;Error(ex);}
    }
    private void TestNotification_Click(object sender,RoutedEventArgs e)=>DeliverNotification(true,null);
    private void DeliverNotification(bool test,MatchNotice? matched)
    {
        string title=Ui.Text(test?"测试提醒":"抽卡目标已达成");
        string message=test?Ui.Text("这是一条测试提醒，不会开始抽卡或改变当前任务。"):
            string.Format(Ui.Text("已停止刷新。A {0} / B {1}，本次刷新 {2} 次。请在游戏内确认保留结果。"),matched!.A,matched.B,matched.Rolls);
        INotificationSink sink=smoke?new RecordingNotifications(smokeNotifications):notifications;
        var failures=NotificationDelivery.Send(notificationOptions,sink,title,message);
        foreach(var failure in failures)Diagnostics.Write(root,"notification",failure.Channel.ToString(),failure.Error);
        if(failures.Length>0){StatusText.Text=Ui.Text("部分提醒未能发送：")+string.Join(" / ",failures.Select(f=>Ui.Text(f.Error)));StatusText.Foreground=(Brush)FindResource("Error");}
    }
    private sealed class RecordingNotifications(List<NotificationChannel> channels):INotificationSink
    {public void Send(NotificationChannel channel,string title,string message)=>channels.Add(channel);}
    private void RefreshPoolList(Snapshot next)
    {
        var choices=PoolChoice.Create(next.Pools,next.PoolId,Ui.Catalog,TimeZoneInfo.Local);
        string key=string.Join("|",choices.Select(p=>p.Pool.Id+":"+p.Pool.Key+":"+p.Title+":"+p.Subtitle+":"+p.Detail));
        if(key==poolListKey)return;
        int old=pool?.Id??next.PoolId;
        var selected=choices.FirstOrDefault(p=>p.Pool.Id==old)??choices.FirstOrDefault();
        updating=true;Pools.ItemsSource=choices;Pools.SelectedItem=selected;updating=false;poolListKey=key;
        if(selected!=null&&pool?.Id==selected.Pool.Id)pool=selected.Pool;else LoadPool();
    }
    private void LoadPool()
    {
        if(updating||Pools.SelectedItem is not PoolChoice choice||account.Length!=64)return;
        pool=choice.Pool;draft=preferences.Load(account,pool.Id);updating=true;
        Threshold.Text=draft.Threshold;CountCopies.IsChecked=draft.CountCopies;ExcludeOverflow.IsChecked=draft.ExcludeOverflow;Interval.Text=draft.Interval;shownTop=0;conditions.Clear();
        updating=false;CountingVisibility();catalogKey="";RebuildCostumes();RenderConditions();Validate();ShowResult();
    }
    private void Pool_Changed(object sender,SelectionChangedEventArgs e){if(preferences!=null)try{LoadPool();}catch(Exception ex){Error(ex);}}
    private void RebuildCostumes()
    {
        if(pool==null)return;var selectedB=BList.SelectedItems.Cast<CostumeRow>().Select(c=>c.Costume.Id).ToHashSet();var selectedA=AList.SelectedItems.Cast<CostumeRow>().Select(c=>c.Costume.Id).ToHashSet();updating=true;costumes.Clear();
        var available=pool.Costumes.ToDictionary(c=>c.Id);
        foreach(var c in pool.Costumes.Concat(draft.A.Concat(draft.B).Distinct().Where(id=>!available.ContainsKey(id)).Select(id=>new Costume{Id=id,Character="不可抽服装",Name="ID "+id,Level=-2})))
        {var row=new CostumeRow{Costume=c,Available=available.ContainsKey(c.Id),Group=draft.A.Contains(c.Id)?1:draft.B.Contains(c.Id)?2:0};row.PropertyChanged+=GroupChanged;costumes.Add(row);}
        updating=false;FilterView();foreach(var row in costumes){if(row.Group==2&&selectedB.Contains(row.Costume.Id)&&bView!.Contains(row))BList.SelectedItems.Add(row);if(row.Group==1&&selectedA.Contains(row.Costume.Id)&&aView!.Contains(row))AList.SelectedItems.Add(row);}
    }
    private void GroupChanged(object? sender,PropertyChangedEventArgs e){if(!updating)ChangedGroups();}
    private void ChangedGroups()
    {draft.A=costumes.Where(c=>c.Group==1).Select(c=>c.Costume.Id).ToArray();draft.B=costumes.Where(c=>c.Group==2).Select(c=>c.Costume.Id).ToArray();Save();FilterView();UpdateSummary();Validate();ShowResult();}
    private void UpdateSummary()
    {
        if(bView==null||aView==null)return;BCount.Text=$"{bView.Count} / {draft.B.Length} 件 · 已选 {BList.SelectedItems.Count}";ACount.Text=$"{aView.Count} / {draft.A.Length} 件 · 已选 {AList.SelectedItems.Count}";
        GroupSummary.Text=$"未分组 {costumes.Count(c=>c.Group==0)} 件 · 双击互移；Ctrl／Shift 多选，Delete 移出。";
    }
    private void UpdateTransfers(){if(controller==null)return;bool edit=!controller.Running&&!connecting;PromoteButton.IsEnabled=edit&&BList.SelectedItems.Count>0;DemoteButton.IsEnabled=edit&&AList.SelectedItems.Count>0;RemoveBButton.IsEnabled=PromoteButton.IsEnabled;RemoveAButton.IsEnabled=DemoteButton.IsEnabled;}
    private void TargetSelection_Changed(object sender,SelectionChangedEventArgs e){if(bView==null||aView==null)return;UpdateSummary();UpdateTransfers();}
    private void Save(){if(updating||pool==null||account.Length!=64)return;try{preferences.Save(account,pool.Id,draft);}catch(Exception e){Error(e);}}
    private void RenderConditions()
    {
        if(!int.TryParse(draft.Threshold,out int top)||top<1||top>10||top==shownTop)return;
        shownTop=top;conditions.Clear();for(int a=top-1;a>=0;a--){var row=draft.Rows.First(r=>r.A==a);var model=new RuleRowModel{Row=row};model.PropertyChanged+=(_,_)=>{Save();Validate();};conditions.Add(model);}
    }
    private void Threshold_Changed(object sender,TextChangedEventArgs e){if(updating||TopError==null)return;draft.Threshold=Threshold.Text;RenderConditions();Save();Validate();}
    private void Interval_Changed(object sender,TextChangedEventArgs e){if(updating||ValidationText==null)return;draft.Interval=Interval.Text;Save();Validate();}
    private void CountingVisibility(){if(ExcludeOverflow==null)return;ExcludeOverflow.Visibility=OverflowHelp.Visibility=draft.CountCopies?Visibility.Visible:Visibility.Collapsed;}
    private void Counting_Changed(object sender,RoutedEventArgs e){if(updating)return;draft.CountCopies=CountCopies.IsChecked==true;if(draft.CountCopies){draft.ExcludeOverflow=true;ExcludeOverflow.IsChecked=true;}CountingVisibility();Save();Validate();ShowResult();}
    private void Overflow_Changed(object sender,RoutedEventArgs e){if(updating)return;draft.ExcludeOverflow=ExcludeOverflow.IsChecked==true;Save();Validate();ShowResult();}
    private string Validate()
    {
        bool goodTop=int.TryParse(draft.Threshold,out int n)&&n>=1&&n<=10;
        TopError.Text=draft.Threshold.Length==0?"":goodTop?"":"请输入 1–10；已保留原输入";
        Threshold.BorderBrush=(Brush)FindResource(goodTop||draft.Threshold.Length==0?"LineStrong":"Error");
        string error=pool==null?"":Rules.Validate(draft,pool.Costumes);ValidationText.Text=error;SetEnabled();return error;
    }
    private bool GameFresh(){try{return ClientController.Fresh(snapshot,port.Find(),DateTime.UtcNow.Ticks);}catch{return false;}}
    private void SetEnabled()
    {
        if(StartButton==null||controller==null)return;bool running=controller.Running;
        bool edit=!running&&!connecting;ConnectButton.IsEnabled=edit;Pools.IsEnabled=edit;GroupsPanel.IsEnabled=edit;TopSettings.IsEnabled=edit;Conditions.IsEnabled=edit;OtherSettings.IsEnabled=edit;GroupActions.IsEnabled=edit;AddUnfilled.IsEnabled=edit&&snapshot?.InventoryReady==true;
        string error=pool==null?"no pool":Rules.Validate(draft,pool.Costumes);
        StartButton.IsEnabled=edit&&error==""&&snapshot?.ResultReady==true&&!snapshot.Locked&&snapshot.PoolId==pool?.Id&&GameFresh();StopButton.IsEnabled=running;UpdateTransfers();
    }
    private void ChangeSelected(ListBox list,int group)
    {
        if(controller.Running||connecting)return;var selected=list.SelectedItems.Cast<CostumeRow>().ToArray();updating=true;
        foreach(var row in selected)row.Group=group;updating=false;ChangedGroups();
    }
    private void ToA_Click(object sender,RoutedEventArgs e)=>ChangeSelected(BList,1);
    private void ToB_Click(object sender,RoutedEventArgs e)=>ChangeSelected(AList,2);
    private void RemoveB_Click(object sender,RoutedEventArgs e)=>ChangeSelected(BList,0);
    private void RemoveA_Click(object sender,RoutedEventArgs e)=>ChangeSelected(AList,0);
    private void SelectB_Click(object sender,RoutedEventArgs e)=>BList.SelectAll();
    private void SelectA_Click(object sender,RoutedEventArgs e)=>AList.SelectAll();
    private void B_DoubleClick(object sender,MouseButtonEventArgs e){if(ItemsControl.ContainerFromElement(BList,e.OriginalSource as DependencyObject)!=null)ChangeSelected(BList,1);}
    private void A_DoubleClick(object sender,MouseButtonEventArgs e){if(ItemsControl.ContainerFromElement(AList,e.OriginalSource as DependencyObject)!=null)ChangeSelected(AList,2);}
    private void Target_KeyDown(object sender,KeyEventArgs e){if(sender is not ListBox list)return;if(e.Key==Key.Delete){ChangeSelected(list,0);e.Handled=true;}else if(e.Key==Key.Enter){ChangeSelected(list,list==BList?1:2);e.Handled=true;}}
    private void AddIds(IEnumerable<int> ids,int group){if(controller.Running||connecting)return;var selected=ids.ToHashSet();updating=true;foreach(var row in costumes.Where(c=>c.Available&&selected.Contains(c.Costume.Id)))row.Group=group;updating=false;ChangedGroups();}
    private void AddCostumes_Click(object sender,RoutedEventArgs e)
    {if(controller.Running||connecting)return;var picker=new AddCostumesWindow(costumes.Where(c=>c.Available&&c.Group==0),Resources){Owner=this};if(picker.ShowDialog()==true)AddIds(picker.SelectedIds,picker.TargetGroup);}
    private void Unfilled_Click(object sender,RoutedEventArgs e)
    {
        if(snapshot?.InventoryReady!=true)return;updating=true;
        foreach(var row in costumes.Where(c=>c.Available&&c.Costume.Level>=-1&&c.Costume.Level<5&&c.Group!=1))row.Group=2;
        updating=false;ChangedGroups();StatusText.Text="已将未满可抽服装加入 B 组，保留已有 A 组。";
    }
    private void RemoveFull_Click(object sender,RoutedEventArgs e){updating=true;foreach(var row in costumes.Where(c=>c.Costume.Level>=5))row.Group=0;updating=false;ChangedGroups();}
    private async void Connect_Click(object sender,RoutedEventArgs e)
    {connecting=true;SetEnabled();try{await port.ConnectAsync(m=>StatusText.Text=m,lifetime.Token);catalogKey="";Poll();}catch(OperationCanceledException){}catch(Exception ex){Error(ex);}finally{connecting=false;SetEnabled();}}
    private void Start_Click(object sender,RoutedEventArgs e){try{if(snapshot==null||pool==null)return;controller.Start(snapshot,pool,draft);StatusText.Text="开始检查当前结果；达标时不会再刷新。";SetEnabled();}catch(Exception ex){Error(ex);}}
    private void Stop_Click(object sender,RoutedEventArgs e){try{controller.Stop();StatusText.Text="已请求停止；已发出的抽取会正常返回，不再发起下一次。";SetEnabled();}catch(Exception ex){Error(ex);}}
    private void Error(Exception ex){StatusText.Text=ex.Message;StatusText.Foreground=(Brush)FindResource("Error");}
    private void ShowResult()
    {
        if(snapshot==null)return;var active=snapshot.Pools.FirstOrDefault(p=>p.Id==snapshot.PoolId);var data=active?.Costumes.ToDictionary(c=>c.Id)??new();
        MatchResult? counted=null;
        if(snapshot.Result.Length==10&&pool?.Id==snapshot.PoolId&&active!=null){try{counted=Rules.Count(draft,snapshot.Result,active.Costumes);}catch(InvalidOperationException){}}
        ResultExpander.Header=counted==null?"当前十连结果（可展开）":$"当前十连 · A {counted.A} / B {counted.B}"+(counted.Overflow>0?$" · 溢出 {counted.Overflow} 张不计":"");
        ResultCards.ItemsSource=snapshot.Result.Select((id,i)=>{data.TryGetValue(id,out var c);int group=pool?.Id==snapshot.PoolId?(draft.A.Contains(id)?1:draft.B.Contains(id)?2:0):0;bool excess=counted?.OverflowSlots.ElementAtOrDefault(i)==true;return new ResultCard((excess?Ui.Text("溢出 · "):group==1?"A · ":group==2?"B · ":"")+(string.IsNullOrWhiteSpace(c?.Character)?Ui.Text("服装 #")+id:c.Character),string.IsNullOrWhiteSpace(c?.Name)?Ui.Text("名称尚未读取"):c.Name,(Brush)new BrushConverter().ConvertFrom(excess?"#F0F2F4":group==1?"#E8F0FA":group==2?"#EAF6F1":"#F0F2F4")!);}).ToArray();
    }
    private async Task SmokeAsync()
    {
        var checks=new List<string>();void Check(bool pass,string name){if(!pass)throw new Exception(name);checks.Add(name);}
        try{
            LanguageChoice.SelectedIndex=0;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Check(costumes.Count==10&&conditions.Count==0,"initial pool and progressive conditions");
            Check(NotifyWindows.IsChecked==true&&NotifyPopup.IsChecked==false&&NotifySound.IsChecked==false,"Windows-only alert default");
            NotifyPopup.IsChecked=true;NotifySound.IsChecked=true;Notification_Changed(this,new());
            TestNotification_Click(this,new());Check(smokeNotifications.Count==3&&!controller.Running,"test alerts route all channels without starting");
            Check(new NotificationPreferences(root).Load() is {Windows:true,Popup:true,Sound:true},"alert preferences persist");
            smokeNotifications.Clear();
            Check(BList.Items.Count==0&&AList.Items.Count==0,"two empty target lists");
            Check(Pools.Items.Cast<PoolChoice>().Select(p=>p.Pool.Id).SequenceEqual(new[]{66,55,44,77})&&pool!.Id==55,"latest date first while retaining native current pool");
            Pools.IsDropDownOpen=true;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture("pool-dropdown",(FrameworkElement)((System.Windows.Controls.Primitives.Popup)Pools.Template.FindName("PART_Popup",Pools)).Child);Pools.IsDropDownOpen=false;
            Threshold.Text="3";Check(conditions.Select(r=>r.Row.A).SequenceEqual(new[]{2,1,0}),"descending A rows");
            Unfilled_Click(this,new());Check(BList.Items.Count==9&&AList.Items.Count==0&&!draft.B.Contains(1),"unfilled goes to B list");
            BList.SelectedItems.Add(costumes[1]);BList.SelectedItems.Add(costumes[2]);ToA_Click(this,new());Check(AList.Items.Count==2&&BList.Items.Count==7&&!draft.B.Intersect(draft.A).Any(),"multi-select B to A transfer");
            AList.SelectedItem=costumes[2];ToB_Click(this,new());Check(AList.Items.Count==1&&BList.Items.Count==8,"A back to B");
            BList.SelectedItem=costumes[3];RemoveB_Click(this,new());Check(costumes[3].Group==0&&BList.Items.Count==7,"remove from target list");
            var picker=new AddCostumesWindow(costumes.Where(c=>c.Available&&c.Group==0),Resources){Owner=this};
            picker.Loaded+=(_,_)=>picker.Dispatcher.BeginInvoke(()=>picker.SmokeSelection(4,2));
            Check(picker.ShowDialog()==true&&picker.SelectedIds.SequenceEqual(new[]{4})&&picker.TargetGroup==2,"add picker searches and selects unassigned costume");
            AddIds(picker.SelectedIds,picker.TargetGroup);AddIds(new[]{1},2);Check(BList.Items.Count==9,"manual addition from unassigned");
            BList.SelectedItem=costumes[0];ToA_Click(this,new());Unfilled_Click(this,new());Check(draft.A.Contains(1)&&draft.A.Contains(2)&&draft.B.Length==8,"bulk add preserves A");
            conditions[0].Enabled=true;conditions[0].B="9";Check(conditions[0].HasError&&!StartButton.IsEnabled&&conditions[0].B=="9","invalid total marked without reset");Capture("invalid");
            conditions[0].B="2";Check(!conditions[0].HasError,"valid B threshold");
            Threshold.Text="11";Check(Threshold.Text=="11"&&conditions.Count==3&&!StartButton.IsEnabled,"invalid top preserves input and rows");
            Threshold.Text="3";Check(conditions[0].B=="2","condition inputs survive threshold editing");
            Filter.SelectedIndex=2;Check(aView!.Count+bView!.Count==1,"full costume filter");Filter.SelectedIndex=0;
            Search.Text="黛安娜";Check(aView!.Count+bView!.Count==1,"name search");Search.Text="";
            BList.SelectedItem=costumes[2];RemoveB_Click(this,new());Check(!draft.B.Contains(3),"remove after filter clear");
            CountCopies.IsChecked=false;Counting_Changed(this,new());Check(!draft.CountCopies&&ExcludeOverflow.Visibility==Visibility.Collapsed,"distinct counting selectable");CountCopies.IsChecked=true;Counting_Changed(this,new());
            Check(draft.ExcludeOverflow&&ExcludeOverflow.IsChecked==true,"copy mode defaults to excluding overflow");
            ExcludeOverflow.IsChecked=false;Overflow_Changed(this,new());Check(!draft.ExcludeOverflow,"overflow can be unchecked");ExcludeOverflow.IsChecked=true;Overflow_Changed(this,new());
            conditions.Last().Enabled=false;Check(!draft.Rows.Single(r=>r.A==0).Enabled,"A zero disabled");
            Check(Pools.SelectedItem is PoolChoice shown&&shown.Pool.Id==pool!.Id&&shown.Title.StartsWith("截止 ")&&shown.Subtitle.Contains("当前结果"),"pool expiry and current result binding");
            ResultExpander.IsExpanded=false;Capture("normal");ResultExpander.IsExpanded=true;Capture("results");ResultExpander.IsExpanded=false;Width=1060;Height=760;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture("compact");
            Threshold.Text="1";Start_Click(this,new());Check(controller.Running&&!GroupsPanel.IsEnabled,"start freezes config");Stop_Click(this,new());Check(!controller.Running,"stop releases control");
            Check(preferences.Load(account,pool!.Id).Threshold=="1","account scoped settings saved");
            var savedTargets=draft.A.ToArray();Pools.SelectedItem=Pools.Items.Cast<PoolChoice>().Single(p=>p.Pool.Id==44);Check(pool!.Id==44,"manual pool selection by identity");
            Poll();Check(pool!.Id==44,"catalog refresh respects manual selection");
            Pools.SelectedItem=Pools.Items.Cast<PoolChoice>().Single(p=>p.Pool.Id==55);Check(draft.A.SequenceEqual(savedTargets)&&Threshold.Text=="1","switching back restores pool rules");
            Start_Click(this,new());string owner=((DemoPort)port).Last.Owner;int writes=((DemoPort)port).Commands;
            ((DemoPort)port).Snapshot.Pools.Single(p=>p.Id==55).EndTimeUnixMilliseconds+=TimeSpan.FromDays(40).Ticks/TimeSpan.TicksPerMillisecond;Poll();
            Check(pool!.Id==55&&Pools.Items.Cast<PoolChoice>().First().Pool.Id==55&&draft.A.SequenceEqual(savedTargets)&&controller.Running&&((DemoPort)port).Last.Owner==owner,"updated end date reorders without changing rules or active owner");
            LanguageChoice.SelectedIndex=1;Check(controller.Running&&((DemoPort)port).Last.Owner==owner&&((DemoPort)port).Commands==writes,"language switch preserves active command");
            Check(Title=="BD2 Infinite Gacha"&&StartButton.Content.ToString()=="Start rerolling"&&BHeading.Text=="B · Secondary targets","English static UI");
            Check(costumes[0].LevelLabel=="+5"&&costumes[5].LevelLabel=="Not owned"&&conditions[0].Label.Contains("at least"),"English bound model labels");
            Check(((PoolChoice)Pools.SelectedItem).Title.StartsWith("Ends ")&&((PoolChoice)Pools.SelectedItem).Subtitle.Contains("Current result"),"English date labels update during active run");
            Check(NotifyWindows.Content.ToString()=="Windows notification"&&NotifyPopup.Content.ToString()=="Popup"&&TestNotification.Content.ToString()=="Test alerts","English alert controls");
            NotifyWindows.IsChecked=false;NotifyPopup.IsChecked=false;NotifySound.IsChecked=false;Notification_Changed(this,new());
            Check(!TestNotification.IsEnabled&&controller.Running&&((DemoPort)port).Last.Owner==owner,"disable alerts without changing active run");
            NotifyWindows.IsChecked=true;NotifyPopup.IsChecked=true;NotifySound.IsChecked=true;Notification_Changed(this,new());
            TestNotification_Click(this,new());Check(controller.Running&&smokeNotifications.Count==3,"test during running preserves run");smokeNotifications.Clear();
            Check(LanguagePreference.Read(root)=="en-US","language persisted separately");Stop_Click(this,new());Width=1260;Height=900;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture("english");
            var englishPicker=new AddCostumesWindow(costumes.Where(c=>c.Available&&c.Group==0),Resources){Owner=this};
            englishPicker.Loaded+=(_,_)=>englishPicker.Dispatcher.BeginInvoke(()=>{Check(englishPicker.Title=="Add costumes","English picker title");englishPicker.Close();});englishPicker.ShowDialog();
            Width=1060;Height=760;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture("english-compact");
            LanguageChoice.SelectedIndex=0;Check(Title=="BD2 无限抽抽乐助手"&&StartButton.Content.ToString()=="开始刷新","Chinese round trip");
            Check(!controller.Running,"language switch does not auto-start");
            Start_Click(this,new());var demo=(DemoPort)port;demo.Snapshot.Owner=demo.Last.Owner;demo.Snapshot.State="matched";
            Poll();Check(!controller.Running&&!demo.Last.Enabled&&smokeNotifications.Count==3,"matched result stops then alerts once");
            Poll();Poll();Check(smokeNotifications.Count==3,"same snapshot does not repeat notifications");
            notifications.Send(NotificationChannel.Popup,Ui.Text("抽卡目标已达成"),Ui.Text("这是一条测试提醒，不会开始抽卡或改变当前任务。"));
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Check(notifications.ActivePopup?.IsVisible==true,"real WPF alert opens without a modal dispatcher");
            Capture("notification-popup",(FrameworkElement)notifications.ActivePopup!.Content);notifications.ActivePopup.Close();
            LanguageChoice.SelectedIndex=1;
            notifications.Send(NotificationChannel.Popup,Ui.Text("抽卡目标已达成"),Ui.Text("这是一条测试提醒，不会开始抽卡或改变当前任务。"));
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture("notification-popup-english",(FrameworkElement)notifications.ActivePopup!.Content);notifications.ActivePopup.Close();
            JsonFiles.Write(Path.Combine(root,"smoke.json"),new{status="passed",checks,realGameTouched=false});Application.Current.Shutdown();
        }catch(Exception ex){JsonFiles.Write(Path.Combine(root,"smoke.json"),new{status="failed",checks,error=ex.ToString()});Application.Current.Shutdown(1);}
    }
    internal async void NativeNotificationSmoke()
    {
        try{
            var failures=NotificationDelivery.Send(new(){Windows=true,Sound=true,Popup=false},notifications,Ui.Text("测试提醒"),Ui.Text("这是一条测试提醒，不会开始抽卡或改变当前任务。"));
            JsonFiles.Write(Path.Combine(root,"notification-smoke.json"),new{status=failures.Length==0?"accepted":"failed",failures,realGameTouched=false,visibleAndAudibleDeliveryNotAssumed=true});
            await Task.Delay(4000);Application.Current.Shutdown(failures.Length==0?0:1);
        }catch(Exception ex){JsonFiles.Write(Path.Combine(root,"notification-smoke.json"),new{status="failed",error=ex.ToString()});Application.Current.Shutdown(1);}
    }
    internal void SnapshotSmoke()
    {
        try{
            if(pool==null||snapshot==null||pool.Id!=snapshot.PoolId||costumes.Count==0||costumes.Any(c=>string.IsNullOrWhiteSpace(c.Costume.Name)))throw new Exception("Real snapshot roster not displayed");
            var unfilled=costumes.Count(c=>c.Costume.Level<5);Unfilled_Click(this,new());if(draft.B.Length!=unfilled)throw new Exception("Unfilled inventory mismatch");
            if(unfilled>0){BList.SelectedItem=costumes.First(c=>c.Costume.Level<5);ToA_Click(this,new());}if(AList.Items.Count!=1||BList.Items.Count!=unfilled-1)throw new Exception("Real snapshot transfer failed");Threshold.Text="1";
            ResultExpander.IsExpanded=true;ShowResult();var cards=ResultCards.ItemsSource.Cast<ResultCard>().ToArray();
            if(cards.Length!=10||cards.Any(c=>c.Name=="名称尚未读取"))throw new Exception("Real snapshot results not displayed");
            Capture("captured-results");ResultExpander.IsExpanded=false;Capture("captured-roster");
            JsonFiles.Write(Path.Combine(root,"snapshot-smoke.json"),new{status="passed",pool=pool.Id,costumes=costumes.Count,unfilled,resultCards=cards.Length,overflow=Rules.Count(draft,snapshot.Result,pool.Costumes).Overflow,realGameTouched=false});Application.Current.Shutdown();
        }catch(Exception ex){JsonFiles.Write(Path.Combine(root,"snapshot-smoke.json"),new{status="failed",error=ex.ToString()});Application.Current.Shutdown(1);}
    }
    private void Capture(string name,FrameworkElement? element=null)
    {UpdateLayout();var content=element??(FrameworkElement)Content;var target=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawRectangle(Background,null,new Rect(0,0,content.ActualWidth,content.ActualHeight));dc.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,content.ActualWidth,content.ActualHeight));}target.Render(visual);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(target));Directory.CreateDirectory(root);using var f=File.Create(Path.Combine(root,name+".png"));png.Save(f);}
}
