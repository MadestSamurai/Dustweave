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

    private async Task CheckPresentationForSmoke()
    {
        int checks=0;
        try
        {
            WorkspaceTabs.SelectedItem=RunTab;
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
                    // Every navigation destination must remain visible above appearance settings.
                    Rect Bounds(FrameworkElement e) => e.TransformToAncestor(this).TransformBounds(new Rect(e.RenderSize));
                    foreach (var nav in new[]{RunTab,SettingsTab,ToolsTab,AccountsTab,DiagnosticsTab})
                        if(Bounds(nav).Bottom>Bounds(ThemeSelector).Top-16)
                            throw new Exception("Compact navigation overlaps appearance controls");
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
