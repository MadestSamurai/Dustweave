using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace BD2Daily.Desktop;

public sealed partial class DailyRunPanel
{
    internal void CheckRecoveryActionsForSmoke()
    {
        UpdateLayout();
        if(retry.Visibility!=Visibility.Visible || resume.Visibility!=Visibility.Visible || current.Visibility!=Visibility.Collapsed || selected.Visibility!=Visibility.Collapsed
            || !ReferenceEquals(retry.Parent,actions) || !ReferenceEquals(resume.Parent,actions) || choices.Children.Contains(retry) || choices.Children.Contains(resume))
            throw new Exception("Interrupted run actions are split between header and footer");
        if(!ReferenceEquals(resume.Style,Application.Current.FindResource("PrimaryButton")) || resume.Content?.ToString()!=L.Get("run.resume")
            || !ReferenceEquals(retry.Style,Application.Current.FindResource(typeof(Button))) || retry.Content?.ToString()!=L.Get("run.retry",SelectedTasks.Count))
            throw new Exception("Original-queue resume is not the primary recovery action");
        Rect Bounds(FrameworkElement element)=>element.TransformToAncestor(this).TransformBounds(new Rect(element.RenderSize));
        var left=Bounds(planButton);var select=Bounds(selectUnfinished);var clear=Bounds(clearSelection);
        if(Math.Abs((left.Top+left.Bottom-select.Top-select.Bottom)/2)>1 || Math.Abs(select.Top-clear.Top)>1 || Math.Abs(select.Height-clear.Height)>1)
            throw new Exception("Task selection buttons are not aligned with the view switch");
        if(selectUnfinished.BorderThickness!=new Thickness(1) || clearSelection.BorderThickness!=new Thickness(1))
            throw new Exception("Task selection buttons lost their standard button affordance");
        var controls=new FrameworkElement[]{chooseTasks,retry,resume};
        foreach(var control in controls)
        {
            var bounds=Bounds(control);
            if(bounds.Bottom>ActualHeight+1 || bounds.Top<Bounds(stages).Bottom || bounds.Right>ActualWidth+1
                || controls.Any(other=>other!=control && Bounds(other).IntersectsWith(bounds)))
                throw new Exception("Recovery actions are clipped or overlap");
        }
        if(!chooseTasks.IsVisible || !resume.IsVisible || !resume.IsEnabled || actionHint.Text!=L.Get("run.resume_footer"))
            throw new Exception("Primary resume or recovery guidance is missing");
        CheckViewportForSmoke();
    }
    internal static void CheckRecoveryRoutingForSmoke(string root)
    {
        var panel=new DailyRunPanel(root);
        string account=new('a',64);
        var view=new QueueView("paused","","fixture/recovery-actions.json",[
            new QueueStage("mirror","completed",""),new QueueStage("trade","blocked",""),new QueueStage("mail","pending","")],account);
        QueueRetryRequest? requested=null;int starts=0,resumes=0;
        panel.RetryRequested+=value=>requested=value;
        panel.StartRequested+=(multi,resume)=>{ if(!multi && resume)resumes++; else starts++; };
        panel.PlanRequested+=_=>starts++;
        panel.SetAccount("fixture",account);panel.ShowPlan(new DailyPreferences());panel.Show(view);
        panel.retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(requested!=null||panel.retry.IsEnabled)throw new Exception("Empty recovery selection dispatched work");
        panel.resume.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(resumes!=1 || starts!=0 || requested!=null || !panel.resume.IsEnabled)
            throw new Exception("Resume requires checkboxes or dispatches a different action");
        panel.SelectUnfinished();panel.retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(requested?.Account!=account || requested.Record!=view.Record || !requested.Tasks.SequenceEqual(new[]{"trade","mail"}) || starts!=0 || resumes!=1)
            throw new Exception("Recovery button dispatched the wrong tasks or started a new plan");
        panel.clearSelection.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(panel.retry.IsEnabled || !panel.resume.IsEnabled)throw new Exception("Clearing retry selection disabled original-queue resume");
        requested=null;panel.Busy(true);panel.retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));panel.resume.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(requested!=null||resumes!=1||panel.retry.Visibility!=Visibility.Collapsed||panel.resume.Visibility!=Visibility.Collapsed)
            throw new Exception("Busy recovery remains actionable");
        panel.Busy(false);panel.chooseTasks.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(!panel.ShowingPlan||starts!=0||requested!=null||resumes!=1)throw new Exception("Choosing tasks unexpectedly ran automation");
        panel.Show(view);panel.SelectUnfinished();panel.Show(view with{Expired=true});
        panel.retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));panel.resume.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(requested!=null||resumes!=1||panel.retry.Visibility!=Visibility.Collapsed||panel.resume.Visibility!=Visibility.Collapsed)
            throw new Exception("Expired history dispatched recovery");
        panel.Show(view with{Account=new string('b',64)});panel.SelectUnfinished();
        panel.retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));panel.resume.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(requested!=null||resumes!=1||panel.retry.IsEnabled||panel.resume.IsEnabled)throw new Exception("Another account history dispatched recovery");
        panel.Show(view with{Stages=[new QueueStage("trade","blocked","")]});panel.SelectUnfinished();
        panel.resume.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(panel.resume.IsEnabled||resumes!=1||!panel.retry.IsEnabled)throw new Exception("A terminal queue offers resume or prevents selected retry");
    }
    internal void ShowOutcomeExplanationsForSmoke()
    {
        Show(new("completed", "", "fixture/outcome-details.json", [
            new("weekly_fishing", "skipped", "weekly_fishing_complete"),
            new("management", "skipped", "no_accrued_rewards"),
            new("free_draws", "skipped", ""),
            new("square", "completed", "square_rewards_checked"),
            new("mail", "completed", "")], new string('a',64)));
    }
    internal void CheckOutcomeExplanationsForSmoke()
    {
        foreach(var row in rows)
        {
            stages.ScrollIntoView(row);UpdateLayout();
            var item=(ListBoxItem)stages.ItemContainerGenerator.ContainerFromItem(row);
            ContentPresenter? FindPresenter(DependencyObject node)
            {
                if(node is ContentPresenter p && p.Content==row)return p;
                for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
                    if(FindPresenter(VisualTreeHelper.GetChild(node,i)) is {} child)return child;
                return null;
            }
            var presenter=FindPresenter(item)??throw new Exception("Timeline result presenter is unavailable");
            var explanation=(TextBlock)stages.ItemTemplate.FindName("Explanation",presenter);
            bool expected=row.Task!="mail";
            if(explanation.IsVisible!=expected || expected && (explanation.Text!=row.Detail || string.IsNullOrWhiteSpace(explanation.Text)))
                throw new Exception("Terminal result explanation was hidden or lost: "+row.Task);
            if(row.Task=="free_draws" && row.Detail!=L.Get("run.skipped_unknown"))
                throw new Exception("Missing skip evidence was replaced by an invented reason");
            if(row.Task=="weekly_fishing" && row.Detail!=L.Get("message.weekly_fishing_complete"))
                throw new Exception("Skip explanation did not follow the software language");
        }
        stages.ScrollIntoView(rows[0]);UpdateLayout();
        CheckViewportForSmoke();
    }
    internal DailyTaskDetailsWindow OpenTaskDetailsForSmoke(bool longList=false)
    {
        IReadOnlyList<QueueTaskDetail> tasks=[new("weekly","每日登录",2,5,"pending"),
            new("weekly","向女神像许愿",2,3,"pending"),new("pass","累计完成任务",10,10,"claimable",Source:"回归通行证")];
        if(longList)tasks=Enumerable.Range(0,60).Select(i=>new QueueTaskDetail("event", "活动任务 "+(i+1)+" · "+new string('长',40),i,80,"pending")).ToArray();
        Show(new("completed","","fixture/pending-details.json",[
            new("rewards","completed","奖励检查完成；仍有未完成任务",FinishedAt:new DateTimeOffset(2026,10,6,8,0,0,TimeSpan.Zero),PendingTasks:tasks)],new string('a',64)));
        stages.ScrollIntoView(rows[0]);UpdateLayout();
        Button? Find(DependencyObject node)
        {
            if(node is Button {Name:"TaskDetails"} button)return button;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
                if(Find(VisualTreeHelper.GetChild(node,i)) is {} child)return child;
            return null;
        }
        var item=(ListBoxItem)stages.ItemContainerGenerator.ContainerFromIndex(0);
        var action=Find(item)??throw new Exception("Pending-task details action is missing");
        if(!action.IsVisible || !action.IsEnabled || action.Content?.ToString()!=L.Get("run.details_count",tasks.Count)
            || !TaskDetailsCommand.CanExecute(rows[0],action))throw new Exception("Pending details cannot be opened from a completed reward check");
        TaskDetailsCommand.Execute(rows[0],action);
        return taskDetailsWindow??throw new Exception("The details action did not open the task window");
    }
    internal int CheckAccessiblePlanForSmoke()
    {
        UpdateLayout();
        stages.ScrollIntoView(rows[0]); UpdateLayout();
        var item=(ListBoxItem)stages.ItemContainerGenerator.ContainerFromIndex(0);
        var peer=UIElementAutomationPeer.CreatePeerForElement(item)??throw new Exception("Timeline accessibility peer missing");
        if(peer.GetName()!=rows[0].AccessibleName)throw new Exception("Timeline item lacks its localized task and status");
        static T? Find<T>(DependencyObject node) where T:DependencyObject
        {
            if(node is T found)return found;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)if(Find<T>(VisualTreeHelper.GetChild(node,i)) is {} child)return child;
            return null;
        }
        var choice=Find<CheckBox>(item)??throw new Exception("Task toggle is not reachable");
        var choicePeer=new CheckBoxAutomationPeer(choice);
        if(choicePeer.GetName()!=rows[0].AccessibleName || choicePeer.GetHelpText()!=rows[0].Detail)
            throw new Exception("Task toggle has missing accessibility text");
        bool was=rows[0].Selected;
        var toggle=(IToggleProvider)choicePeer.GetPattern(PatternInterface.Toggle);
        toggle.Toggle();
        if(rows[0].Selected==was)throw new Exception("Accessible task toggle did not change the plan");
        toggle.Toggle();
        if(rows[0].Selected!=was)throw new Exception("Accessible task toggle did not restore the plan");
        if(!choice.Focusable || !choice.IsTabStop || !current.Focusable || !current.IsTabStop)
            throw new Exception("Task and start controls cannot receive keyboard focus");
        if (!planButton.IsEnabled || planButton.Tag as string != "selected")
            throw new Exception("Current plan is presented as a disabled action");
        if (Find<TextBlock>(item) == null || item.ActualHeight > 58)
            throw new Exception("Plan rows repeat nonessential status text");
        if (Find<System.Windows.Shapes.Path>(item)?.Data != rows[0].Icon || rows.Any(r => r.Icon.IsEmpty()))
            throw new Exception("Timeline tasks have missing vector symbols");
        return 8;
    }
    internal void CheckProgressRingForSmoke()
    {
        double previousValue = progress.Value, previousMaximum = progress.Maximum;
        try
        {
            foreach (var (value, maximum, label) in new[] { (0d, 3d, "0%"), (1d, 3d, "33%"), (999d, 1000d, "99%"), (3d, 3d, "100%") })
            {
                progress.Maximum = maximum;
                progress.Value = value;
                UpdateLayout();
                if (progress.Template.FindName("Percentage", progress) is not TextBlock text || text.Text != label)
                    throw new Exception("Circular progress does not reflect completed stages");
                var peer = new ProgressBarAutomationPeer(progress);
                var range = (IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue);
                if (range.Value != value || range.Maximum != maximum || string.IsNullOrEmpty(peer.GetName()))
                    throw new Exception("Circular progress lost native range accessibility");
            }
            var converter = new ProgressRingConverter();
            Geometry Shape(double value) => (Geometry)converter.Convert([value, 0d, 1d], typeof(Geometry), "", System.Globalization.CultureInfo.InvariantCulture);
            if (!Shape(0).IsEmpty() || Shape(1) is not EllipseGeometry || Shape(.5).IsEmpty())
                throw new Exception("Progress ring cannot render empty partial and complete states");
            var finished = new DateTimeOffset(2026, 10, 6, 11, 2, 37, TimeSpan.Zero);
            var row = new StageRow(new("guild", "completed", "", Carried: true, FinishedAt: finished));
            string clock = finished.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            if (!row.HasFinishedTime || row.FinishedTime != clock || !row.FinishedTimeHelp.Contains("37") || !row.AccessibleName.Contains(clock))
                throw new Exception("Completion time lost its local clock, full tooltip or accessible description");
            row.Update(new("guild", "running", "", FinishedAt: finished));
            if (row.HasFinishedTime || row.FinishedTime.Length != 0)
                throw new Exception("An active retry still shows the previous completion time");
            row.Update(new("guild", "completed", ""));
            if (row.HasFinishedTime || row.FinishedTimeHelp.Length != 0)
                throw new Exception("Missing completion evidence fabricated a timestamp");
        }
        finally
        {
            progress.Maximum = previousMaximum;
            progress.Value = previousValue;
            UpdateLayout();
        }
    }
    internal void CheckLongMessageForSmoke()
    {
        UpdateLayout();
        var scroller=detail.Parent as ScrollViewer??throw new Exception("Run message is not scrollable");
        if(scroller.ScrollableHeight<=0 || scroller.ActualHeight>96.1)throw new Exception("Long message can obscure the task controls");
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(this).TransformBounds(new Rect(element.RenderSize));
        foreach(var control in new FrameworkElement[]{current,stop,stages})
            if(control.IsVisible && (Bounds(control).Bottom>ActualHeight+1 || Bounds(control).Top<0))throw new Exception("Long message pushed controls out of view");
        if(stages.ActualHeight<100)throw new Exception("Long message left no usable timeline space");
    }
}

public partial class MainWindow
{
    private async Task<int> CheckDiagnosticsForSmoke()
    {
        if(host is not DemoEnvironment demo)throw new Exception("Diagnostics check requires the isolated host");
        int checks=0;
        bool observeGuild=demo.HandleGuildCommands, title=demo.TitleVisible;
        timer.Stop();
        try
        {
            demo.HandleGuildCommands=false;demo.TitleVisible=false;
            WorkspaceTabs.SelectedItem=DiagnosticsTab;
            int commands=demo.Calls.Count;
            RefreshDiagnostics_Click(this,new RoutedEventArgs());
            if(commands!=demo.Calls.Count)throw new Exception("Refreshing diagnostics sent a game command");
            DiagnosticCopyFeedback.Visibility=Visibility.Collapsed;
            var game=new GameInstance(100,100,"fixture");
            foreach(int language in new[]{0,1,2})
            {
                LanguageSelector.SelectedIndex=language;
                foreach(string state in new[]{"closed","unconnected","stale","login","ready","unreadable"})
                {
                    var frame=new DailySnapshot{State=state=="login"?"waiting_start":"identified",Scene="演示主城",
                        AccountKey="PRIVATE-ACCOUNT-ID",PlayerKey="PRIVATE-PLAYER-ID",PlayerName="PRIVATE-NAME"};
                    PresentDiagnostics(state=="closed"?null:game,state=="unconnected"?null:frame,state is "ready" or "login",state=="unreadable");
                    if(diagnosticState!=state || DiagnosticStatus.Text!=L.Get("diagnostics.status."+state) || string.IsNullOrEmpty(DiagnosticAdvice.Text))
                        throw new Exception("Diagnostics status and guidance do not match the connection");
                    if((DiagnosticConnect.Visibility==Visibility.Visible)!=(state is "unconnected" or "stale"))
                        throw new Exception("Diagnostics offers reconnect in the wrong state");
                    string summary=DiagnosticSummary();
                    if(summary.Contains("PRIVATE-")||summary.Contains(root))throw new Exception("Diagnostic summary leaks account data");
                    if(language==2)
                    {
                        DiagnosticsScroll.ScrollToTop();
                        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                        Capture("diagnostics-state-"+state);
                    }
                    checks++;
                }
            }
            LanguageSelector.SelectedIndex=0;
            PresentDiagnostics(game,new DailySnapshot{State="identified",Scene="演示主城"},true);
            if(GuildDiagnostics.IsExpanded || ActionFooter.IsVisible)throw new Exception("Advanced checks obscure the diagnostic overview");
            foreach(int appearance in new[]{1,2})
            {
                ThemeSelector.SelectedIndex=appearance;
                Width=1180;Height=800;DiagnosticsScroll.ScrollToTop();
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                Capture("diagnostics-"+(appearance==1?"light":"dark"));
                Width=920;Height=650;
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                if(CopyDiagnosticButton.TransformToAncestor(DiagnosticsScroll).TransformBounds(new Rect(CopyDiagnosticButton.RenderSize)).Bottom>DiagnosticsScroll.ActualHeight)
                    throw new Exception("The compact diagnostic overview hides feedback actions");
            }
            GuildDiagnostics.IsExpanded=true;DiagnosticsScroll.ScrollToBottom();
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Capture("diagnostics-advanced-compact");
            if(!ObserveButton.IsVisible || !GuildRunButton.IsVisible || commands!=demo.Calls.Count)
                throw new Exception("Advanced diagnostics are unavailable or expanding them performed an action");
            checks+=4;
        }
        finally
        {
            GuildDiagnostics.IsExpanded=false;demo.HandleGuildCommands=observeGuild;demo.TitleVisible=title;
            UpdateConnection();timer.Start();
        }
        return checks;
    }

    private async Task<int> CheckWindowChromeForSmoke()
    {
        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
        if (chrome == null || chrome.UseAeroCaptionButtons || chrome.GlassFrameThickness != new Thickness(0))
            throw new Exception("Native title bar was not replaced");
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        static nint Packed(Point point) => (nint)((((int)point.Y & 0xffff) << 16) | ((int)point.X & 0xffff));
        int Hit(Point point) => (int)ChromeSendMessage(handle, 0x0084, 0, Packed(point));
        Point Center(FrameworkElement element) => element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        if (Hit(Center(PageTitle)) != 2) throw new Exception("Page heading cannot drag the window");
        foreach (var button in new[] { ConnectButton, MinimizeButton, MaximizeButton, CloseButton })
        {
            if (Hit(Center(button)) != 1) throw new Exception("Caption swallowed a button click: " + button.Name);
            var peer = new ButtonAutomationPeer(button);
            if (peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider)
                throw new Exception("Window control is not accessible: " + button.Name);
        }
        var oldBounds = new Rect(Left, Top, Width, Height);
        var content = (FrameworkElement)Content;
        if (Hit(content.PointToScreen(new Point(1, ActualHeight / 2))) != 10)
            throw new Exception("Window resize edge is missing");
        MaximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (WindowState != WindowState.Maximized || (string?)MaximizeButton.ToolTip != L.Get("window.restore"))
            throw new Exception("Maximize did not update state and restore label");
        var info = new ChromeMonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<ChromeMonitorInfo>() };
        if (!ChromeGetMonitorInfo(ChromeMonitorFromWindow(handle, 2), ref info))
            throw new Exception("Could not verify maximized work area");
        var topLeft = content.PointToScreen(new Point());
        var bottomRight = content.PointToScreen(new Point(content.ActualWidth, content.ActualHeight));
        if (topLeft.X < info.Work.Left - 1 || topLeft.Y < info.Work.Top - 1 ||
            bottomRight.X > info.Work.Right + 1 || bottomRight.Y > info.Work.Bottom + 1)
            throw new Exception($"Maximized content extends beyond the monitor work area: content {topLeft} to {bottomRight}; work {info.Work.Left},{info.Work.Top} to {info.Work.Right},{info.Work.Bottom}; dpi {VisualTreeHelper.GetDpi(this)}");
        Capture("chrome-maximized");
        MinimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (WindowState != WindowState.Minimized) throw new Exception("Minimize button failed");
        SystemCommands.RestoreWindow(this);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (WindowState == WindowState.Maximized) MaximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (WindowState != WindowState.Normal || Math.Abs(Width - oldBounds.Width) > 1 ||
            Math.Abs(Height - oldBounds.Height) > 1 || (string?)MaximizeButton.ToolTip != L.Get("window.maximize"))
            throw new Exception("Restore did not recover the normal window");
        return 13;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint ChromeSendMessage(nint window, uint message, nint wParam, nint lParam);

    private async Task CheckPresentationForSmoke()
    {
        int checks=0;
        try
        {
            WorkspaceTabs.SelectedItem=RunTab;
            checks += await CheckWindowChromeForSmoke();
            foreach(int language in new[]{0,1,2})
            {
                LanguageSelector.SelectedIndex=language;
                dailyPanel.Busy(false);dailyPanel.ShowCurrentPlan();
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                checks+=dailyPanel.CheckAccessiblePlanForSmoke();
            }
            Width=920;Height=650;
            foreach (int language in new[]{0,1,2})
            foreach (int theme in new[]{1,2})
            {
                LanguageSelector.SelectedIndex=language;ThemeSelector.SelectedIndex=theme;
                foreach (var tab in new[]{RunTab,SettingsTab,ToolsTab,AccountsTab,DiagnosticsTab})
                {
                    WorkspaceTabs.SelectedItem=tab;
                    await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    foreach (var control in new[] { MinimizeButton, MaximizeButton, CloseButton })
                        if (string.IsNullOrWhiteSpace(new ButtonAutomationPeer(control).GetName()) ||
                            control.TransformToAncestor(this).TransformBounds(new Rect(control.RenderSize)).Right > ActualWidth)
                            throw new Exception("Window control label or compact placement is invalid");
                    if (WindowControls.TransformToAncestor(PageHeader).TransformBounds(new Rect(WindowControls.RenderSize)).Left <
                        ConnectButton.TransformToAncestor(PageHeader).TransformBounds(new Rect(ConnectButton.RenderSize)).Right)
                        throw new Exception("Connection action overlaps the window controls");
                    // Every navigation destination must remain visible above appearance settings.
                    Rect Bounds(FrameworkElement e) => e.TransformToAncestor(this).TransformBounds(new Rect(e.RenderSize));
                    foreach (var nav in new[]{RunTab,SettingsTab,ToolsTab,AccountsTab,DiagnosticsTab})
                        if(Bounds(nav).Bottom>Bounds(ThemeSelector).Top-16)
                            throw new Exception("Compact navigation overlaps appearance controls");
                    if (PageTitle.Text != tab.Header as string || PageTitle.ActualWidth < 80)
                        throw new Exception("Page heading lost its translated navigation context");
                    if (tab.Template.FindName("NavIcon", tab) is not System.Windows.Shapes.Path navIcon || navIcon.Data?.IsEmpty() != false)
                        throw new Exception("Navigation destination has no vector icon");
                    // Selection color belongs to navigation, never to the page's inherited text.
                    if (tab.Foreground != FindResource("Ink"))
                        throw new Exception("Navigation selection changed page text color");
                    checks += 3;
                    if(tab==AccountsTab)
                    {
                        if(Math.Abs(Search.ActualHeight-RefreshButton.ActualHeight)>1)
                            throw new Exception("Search and refresh controls have mismatched heights");
                        if(StopButton.IsVisible && Bounds(StopButton).Right>ActualWidth-12 || Bounds(SelectionText).Width<80)
                            throw new Exception("Account actions crowd out the selection summary");
                        if(AccountsGrid.ActualHeight<196)
                        {
                            Capture("polish-accounts-tight");
                            throw new Exception($"Compact accounts list has no room for three accounts: {AccountsGrid.ActualHeight:F1}");
                        }
                    }
                    Capture($"polish-{language}-{theme}-{tab.Name}");
                    checks++;
                }
            }
            WorkspaceTabs.SelectedItem=RunTab;
            foreach(int index in new[]{0,1,2})
            {
                var choice=(ListBoxItem)ThemeSelector.Items[index];
                var peer=new ListBoxItemAutomationPeer(choice,new ListBoxAutomationPeer(ThemeSelector));
                ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
                choice.Focus();UpdateLayout();
                var tile=choice.Template.FindName("Tile",choice) as Border;
                if(ThemeSelector.SelectedIndex!=index || theme.Preference!=(DailyAppearance)index || string.IsNullOrEmpty(peer.GetName())
                    || tile?.BorderBrush!=FindResource("Focus"))
                    throw new Exception("Appearance segments do not support accessible selection and keyboard focus");
                checks++;
            }
            LanguageSelector.Focus();UpdateLayout();
            var focus=LanguageSelector.Template.FindName("ComboFocus",LanguageSelector) as Border;
            if(focus==null || !LanguageSelector.IsKeyboardFocusWithin || focus.BorderBrush!=FindResource("Focus"))
                throw new Exception("Language selector lacks a visible keyboard focus");
            double selectorHeight=LanguageSelector.ActualHeight;
            LanguageSelector.IsDropDownOpen=true;
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            var popup=LanguageSelector.Template.FindName("PART_Popup",LanguageSelector) as Popup;
            if(popup?.Child is not Border menu || menu.ActualWidth+1<LanguageSelector.ActualWidth)
                throw new Exception("Dropdown is narrower than its selector");
            if(LanguageSelector.ActualHeight!=selectorHeight)throw new Exception("Opening a selector changed layout");
            // The WPF popup is a separate presentation source, so capture it separately.
            Capture("polish-language-menu",menu);
            LanguageSelector.IsDropDownOpen=false;checks+=3;
            checks+=await CheckDiagnosticsForSmoke();
            WorkspaceTabs.SelectedItem=AccountsTab;
            Search.Focus();Search.Text="演示搜索";UpdateLayout();
            var searchHeight=Search.ActualHeight;
            Search.SelectAll();Search.SelectedText="主账号";UpdateLayout();
            if(Search.Text!="主账号" || searchHeight!=Search.ActualHeight)
                throw new Exception("Styled textbox broke text selection or input geometry");
            Search.Clear();checks++;
            WorkspaceTabs.SelectedItem=RunTab;
            Width=920;Height=650;ThemeSelector.SelectedIndex=2;
            string longMessage=string.Join("\n",Enumerable.Repeat("screen_changed",60));
            dailyPanel.Show(new("running",longMessage,"fixture/long-message.json",
                [new("guild","completed",""),new("mirror","running","screen_changed"),new("mail","pending","")],new string('a',64)));
            dailyPanel.Busy(true);
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            dailyPanel.CheckLongMessageForSmoke();checks+=3;
            dailyPanel.CheckProgressRingForSmoke();checks+=12;
            Capture("long-message-compact");
            DailyJson.Write(Path.Combine(smoke!,"presentation.json"),new{
                status="passed",checks,localizedAccessibleNames=true,toggleProviderPreservesPlan=true,
                longMessageKeepsControlsVisible=true,keyboardTabStops=true,
                physicalKeyboardAndScreenReaderVerified=false,realGameTouched=false
            });
        }
        finally
        {
            Width=1180;Height=800;LanguageSelector.SelectedIndex=0;ThemeSelector.SelectedIndex=1;
            dailyPanel.Busy(false);dailyPanel.ShowCurrentPlan();dailyPanel.SelectPlanAll();
        }
    }
}
