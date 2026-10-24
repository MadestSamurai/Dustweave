using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async Task CheckSandboxManagementForSmoke()
    {
        if(smoke==null||sessions is not DemoEnvironment)throw new InvalidOperationException("Isolated fixture required");
        L.Audit();string language=L.Code;int theme=ThemeSelector.SelectedIndex;int checks=0;
        void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);checks++;}
        var account=new DailyAccount(1,"测试账号 · Account",new string('a',64),"…1234",true,false,"");
        try {
            foreach(string locale in DailyLanguage.Codes)foreach(int appearance in new[]{1,2})foreach(string stage in new[]{"empty","ready","running","removal","binding"}) {
                L.Select(locale);ThemeSelector.SelectedIndex=appearance;
                var fixture=new DailySandboxInfo("Dustweave_Test",@"C:\Users\测试用户\AppData\Local\Dustweave\instances\test\root",stage!="empty",
                    stage=="removal"?"sandbox.manage.removal_pending":stage=="binding"?"sandbox.repair.required":"",stage=="running"?[new(1234,"BrownDust II")]:[]);
                var window=new SandboxManagementWindow(this,account,fixture,[new("RenamedBox",fixture.Root,""),new("OtherBox",@"C:\Other","sandbox.repair.other_root")]);
                window.Show();await window.RefreshAsync();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                var buttons=SandboxButtons(window).Where(b=>b.Name.StartsWith("SandboxSpace")).ToDictionary(b=>b.Name);
                Check(window.ActualHeight<=window.MaxHeight&&window.ActualWidth>=460,"Management window is outside its bounded layout");
                Check(buttons.Values.All(b=>b.ActualWidth>30&&b.ActualHeight>=30||b.Visibility==Visibility.Collapsed),"Management action is too small");
                Check(buttons.Values.All(b=>!b.Content.ToString()!.StartsWith("sandbox.")),"Management action is untranslated");
                Check(buttons["SandboxSpaceCreate"].IsEnabled==(stage=="empty"),"Create action does not match sandbox state");
                Check(buttons["SandboxSpaceRemove"].IsEnabled==(stage is "ready" or "removal"),"Removal gate does not match running/recovery state");
                Check(!Descendants(window).OfType<RadioButton>().Any(),"Account management must not contain per-account modes");
                if(stage=="binding") {
                    int windows=Application.Current.Windows.Count;
                    var choose=Descendants(window).OfType<ComboBox>().Single();
                    var manual=Descendants(window).OfType<Button>().Single(b=>b.Name=="SandboxBindingManual");
                    Check(!choose.IsVisible&&choose.Items.Count==2,"Automatic inline match must precede manual binding");
                    Descendants(window).OfType<Button>().Single(b=>b.Name=="SandboxBindingWrong").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(choose.IsVisible&&manual.IsEnabled&&windows==Application.Current.Windows.Count,"Manual binding must expand in place");
                    choose.SelectedIndex=1;Check(!manual.IsEnabled,"Another account's sandbox must not be bindable");
                    choose.SelectedIndex=0;await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    CaptureSandboxDialog(window,"sandbox-inline-binding-"+locale+"-"+appearance);
                }
                if(stage is "empty" or "ready")CaptureSandboxDialog(window,"sandbox-management-"+locale+"-"+appearance+"-"+stage);
                if(stage=="ready") {
                    window.PresentRemoved();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    Check(buttons["SandboxSpaceCreate"].IsEnabled&&(string)buttons["SandboxSpaceCreate"].Content==L.Get("sandbox.manage.rebuild"),"Removal must offer an explicit rebuild action");
                    Check(!buttons["SandboxSpaceRemove"].IsVisible,"Removed sandbox must not retain a delete action");
                    CaptureSandboxDialog(window,"sandbox-rebuild-"+locale+"-"+appearance);
                }
                window.Close();
            }
            var waiting=new TaskCompletionSource<DailySandboxInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int probes=0;
            var slow=new SandboxManagementWindow(this,account,inspect:token=>{Interlocked.Increment(ref probes);started.TrySetResult();return waiting.Task;});
            var elapsed=System.Diagnostics.Stopwatch.StartNew();slow.Show();await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            double firstPaintMs=elapsed.Elapsed.TotalMilliseconds;
            Check(slow.IsVisible&&!waiting.Task.IsCompleted,"Slow status reading must not delay opening the management window");
            await slow.RefreshAsync();Check(probes==1,"Refresh must not multiply slow probes");
            elapsed.Restart();slow.Close();double closeMs=elapsed.Elapsed.TotalMilliseconds;
            Check(!slow.IsVisible&&closeMs<1000,"Closing must not wait for a read-only probe");
            waiting.SetResult(new("Fixture","",false,"",[]));await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            var pending=new TaskCompletionSource<DailySandboxInfo>(TaskCreationOptions.RunContinuationsAsynchronously);int slowProbes=0;
            var timed=new SandboxManagementWindow(this,account,inspect:_=>{Interlocked.Increment(ref slowProbes);return pending.Task;});
            timed.Show();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            var timeoutButtons=SandboxButtons(timed).ToDictionary(b=>b.Name);
            for(int i=0;i<110&&!timeoutButtons["SandboxSpaceRefresh"].IsEnabled;i++)await Task.Delay(100);
            Check(timeoutButtons["SandboxSpaceRefresh"].IsEnabled&&!timeoutButtons["SandboxSpaceCreate"].IsEnabled,"Timeout must enable retry without guessing sandbox state");
            Check(timeoutButtons["SandboxSpaceSettings"].IsVisible,"Timeout must offer the settings route in the same page");
            Check(Descendants(timed).OfType<TextBlock>().Any(t=>t.Text==L.Get("sandbox.manage.slow")),"Timeout must explain the next action");
            pending.SetResult(new("Fixture","",false,"",[]));await timed.RefreshAsync();
            Check(slowProbes==1&&timeoutButtons["SandboxSpaceCreate"].IsEnabled,"Retry must reuse a completed pending probe and recover normally");
            timed.Close();
            await ReadSandboxModeAsync();WorkspaceTabs.SelectedItem=SoftwareSettingsTab;
            SandboxCompatibility.IsChecked=true;await SaveSandboxModeAsync();
            Check(DailySandboxPreferences.Read(root,Path.Combine(root,"fixture-instances")).Mode==DailySandboxFileMode.Compatible,"Global mode must persist independently of the selected account");
            SandboxCompatibility.IsChecked=false;await SaveSandboxModeAsync();
            Check(DailySandboxPreferences.Read(root,Path.Combine(root,"fixture-instances")).Mode==DailySandboxFileMode.Synchronized,"Global mode must be reversible");
            SetBusy(true);Check(!SandboxCompatibility.IsEnabled,"Mode changes must wait for active operations");SetBusy(false);
            foreach(string locale in DailyLanguage.Codes)foreach(int appearance in new[]{1,2}) {
                L.Select(locale);ThemeSelector.SelectedIndex=appearance;SoftwareSettingsScroll.ScrollToBottom();
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture("sandbox-global-mode-"+locale+"-"+appearance);
            }
            DailyJson.Write(Path.Combine(smoke,"sandbox-management.json"),new{status="passed",checks,firstPaintMs,closeMs,realGameTouched=false,locales=DailyLanguage.Codes});
        } finally {L.Select(language);ThemeSelector.SelectedIndex=theme;}
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);i++) {
            var child=System.Windows.Media.VisualTreeHelper.GetChild(parent,i);yield return child;
            foreach(var descendant in Descendants(child))yield return descendant;
        }
    }
}
