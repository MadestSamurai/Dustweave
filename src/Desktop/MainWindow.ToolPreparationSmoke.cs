using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    async Task CheckToolPreparationForSmoke()
    {
        L.Audit();
        var checks=new List<string>();
        void Check(bool value,string message){if(!value)throw new Exception(message);checks.Add(message);}
        async Task Until(Func<bool> predicate){for(int i=0;i<100&&!predicate();i++)await Task.Delay(25);Check(predicate(),"bounded UI preparation completed");}
        var equipment=Assembly.Load("BD2EquipmentAssistant").GetType("BD2Equipment.MainWindow")!;
        var gacha=Assembly.Load("BD2InfiniteGacha");
        var demos=new Dictionary<Window,object>();
        Window Create(string id){
            if(id=="mansion-runaway")return new MansionWindow(smoke:true,deferChrome:true);
            if(id=="equipment")return (Window)Activator.CreateInstance(equipment,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{false},null)!;
            if(id=="fiend-hunter")return (Window)Activator.CreateInstance(Assembly.Load("BD2FiendHunter").GetType("BD2FiendHunter.Desktop.MainWindow")!,new object[]{true})!;
            var demo=Activator.CreateInstance(gacha.GetType("BD2InfiniteGacha.Desktop.DemoPort")!)!;
            var window=(Window)Activator.CreateInstance(gacha.GetType("BD2InfiniteGacha.Desktop.MainWindow")!,demo,Path.Combine(smoke!,"gacha-settings"),false)!;
            demos[window]=demo;return window;
        }
        string? prior=Environment.GetEnvironmentVariable("BD2_DAILY_HOSTED_TOOL");
        try
        {
            foreach(string id in new[]{"mansion-runaway","equipment","infinite-gacha","fiend-hunter"})
            foreach(string language in new[]{"zh-CN","zh-TW","en-US"})
            foreach(string appearance in new[]{"light","dark"})
            {
                L.Select(language);theme.Select(appearance=="light"?DailyAppearance.Light:DailyAppearance.Dark);
                using var locale=HostedToolLocale.Begin(language);
                Environment.SetEnvironmentVariable("BD2_DAILY_HOSTED_TOOL",id);
                var window=Create(id);
                if(id=="fiend-hunter"&&window.FindName("LanguageBox") is ComboBox languageBox)languageBox.Visibility=Visibility.Collapsed;
                window.Owner=this;window.ShowInTaskbar=false;window.ShowActivated=false;window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-12000;window.Top=0;
                window.Width=id=="fiend-hunter"?620:id=="infinite-gacha"?1260:id=="equipment"?1040:820;window.Height=id=="fiend-hunter"?620:id=="infinite-gacha"?940:id=="equipment"?800:700;
                int prepares=0,ready=0;bool painted=false,rendered=false;
                window.ContentRendered+=(_,_)=>rendered=true;
                var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var loading=new HostedToolPreparation(window,id,root,async(progress,cancel)=>{
                    prepares++;painted=await Dispatcher.InvokeAsync(()=>window.IsVisible&&window.IsLoaded&&((FrameworkElement)window.Content).ActualHeight>0);
                    progress("tools.preparation_identity");entered.TrySetResult();await release.Task.WaitAsync(cancel);
                },()=>ready++);
                DailyDialogs.Prepare(window,toolWindow:true);window.Show();
                try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch(TimeoutException error) {
                    Capture($"preparation-timeout-{id}",(FrameworkElement)window.Content);
                    throw new InvalidOperationException($"{id}/{language}/{appearance}: preparation did not enter; loaded={window.IsLoaded}, visible={window.IsVisible}, rendered={rendered}, preparing={loading.Preparing}, pending={loading.Pending?.Status}, occupied={DailyToolControl.IsOccupied(root)}",error);
                }
                Check(painted&&!loading.Body.IsEnabled&&loading.Preparing&&window.IsEnabled,id+": real window paints first and only tool content is locked");
                Check(DailyToolControl.IsOccupied(root)&&ready==0,id+": connection lease blocks concurrent automation, without starting it");
                Check(loading.Progress.IsVisible&&loading.Progress.IsIndeterminate==SystemParameters.ClientAreaAnimation,id+": progress follows reduced motion preference");
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                if(SystemParameters.ClientAreaAnimation){
                    var sweep=(Border)loading.Progress.Template.FindName("LoadingSweep",loading.Progress);
                    Check(sweep.IsVisible&&sweep.OpacityMask.RelativeTransform.HasAnimatedProperties,id+": loading template animates rather than showing a static filled bar");
                }
                Capture($"loading-{id}-{language}-{appearance}",(FrameworkElement)window.Content);
                loading.Cancel();await loading.Pending!;
                Check(!loading.Preparing&&!loading.Ready&&!DailyToolControl.IsOccupied(root)&&loading.ActionButton.IsEnabled,id+": cancellation releases lease and permits explicit retry");
                var retry=loading.StartAsync();await Until(()=>prepares==2);release.TrySetResult();await retry;
                Check(ready==1&&loading.Ready&&loading.Body.IsEnabled&&!DailyToolControl.IsOccupied(root),id+": retry makes window ready without automation");
                if(id=="fiend-hunter"){
                    var selection=(ComboBox)window.FindName("BossChoice");
                    Check(selection.Items.Count==2&&selection.SelectedIndex==0,"fiend hunter defaults to Berserker and offers both monsters");
                    selection.SelectedIndex=1;
                    Check(selection.SelectedIndex==1&&!((Button)window.FindName("StartButton")).IsEnabled,"monster selection never starts a round without a connected session");
                    var character=(ComboBox)window.FindName("CharacterChoice");var difficulty=(ComboBox)window.FindName("DifficultyChoice");
                    Check(character.Items.Count==3&&character.SelectedIndex==0&&difficulty.Items.Count==3&&difficulty.SelectedIndex==2,"three characters and three solo difficulties retain legacy defaults");
                    character.SelectedIndex=2;difficulty.SelectedIndex=0;
                    window.GetType().GetField("preparing",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,true);
                    window.GetType().GetMethod("UpdateControls",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
                    Check(!character.IsEnabled&&!difficulty.IsEnabled&&!selection.IsEnabled,"all round choices lock while preparing");
                    window.GetType().GetField("preparing",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,false);
                    window.GetType().GetMethod("UpdateControls",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
                    Check(!((bool)window.GetType().GetProperty("HostedAutomationEnabled")!.GetValue(window)!),"fiend hunter readiness remains idle");
                    Capture($"ready-{id}-{language}-{appearance}",(FrameworkElement)window.Content);
                }
                if(id=="equipment")
                {
                    var five=(CheckBox)window.FindName("FiveInput");
                    Check(five.FontSize>=14&&five.ActualHeight>=36&&five.Content is TextBlock {FontSize:>=14,TextWrapping:TextWrapping.Wrap},"5-potion option is readable, wrapped and easy to click");
                    equipment.GetMethod("Busy",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{true});
                    ((TextBlock)window.FindName("StatusText")).Text=L.Translate("正在读取库存…");
                    Check(((ProgressBar)window.FindName("LoadingProgress")).IsVisible&&!((Button)window.FindName("ExecuteButton")).IsEnabled,"inventory loading remains animated and execution stays disabled");
                    Capture($"inventory-{language}-{appearance}",(FrameworkElement)window.Content);
                    equipment.GetMethod("Busy",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{false});
                }
                if(id=="infinite-gacha"){
                    var demo=demos[window];var snapshot=demo.GetType().GetField("Snapshot")!.GetValue(demo)!;
                    Check(Convert.ToInt64(snapshot.GetType().GetField("Sequence")!.GetValue(snapshot))==1,"gacha does not poll transport before preparation and explicit connection complete");
                    ((Button)window.FindName("ConnectButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(Convert.ToInt64(snapshot.GetType().GetField("Sequence")!.GetValue(snapshot))>1,"gacha reads inventory after connection becomes ready");
                    Check((int)demo.GetType().GetField("Commands")!.GetValue(demo)! == 0,"gacha readiness never rolls automatically");
                    Check(!((bool)window.GetType().GetProperty("HostedAutomationEnabled")!.GetValue(window)!),"gacha ready window remains idle");
                    Check(((ListBox)window.FindName("BList")).Background==Application.Current.Resources["Surface"],"gacha target list uses current product surface");
                    ((Expander)window.FindName("ResultExpander")).IsExpanded=true;
                    await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    Capture($"ready-{id}-{language}-{appearance}",(FrameworkElement)window.Content);
                }
                window.Close();await Until(()=>!window.IsVisible);
            }
            // The hosted app can finish showing before its deferred decorator runs.
            var shown=new Window{Content=new Grid(),Width=480,Height=180,ShowActivated=false,ShowInTaskbar=false,Left=-12000};
            DailyDialogs.Prepare(shown);shown.Show();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            int lateStarts=0,lateReady=0;
            var late=new HostedToolPreparation(shown,"equipment",root,(_,_)=>{lateStarts++;return Task.CompletedTask;},()=>lateReady++);
            await Until(()=>late.Ready);
            Check(lateStarts==1&&lateReady==1&&!DailyToolControl.IsOccupied(root),"already-rendered window starts preparation exactly once and releases ownership");shown.Close();
            // Failures do not trigger an automatic retry loop or leave controls unlocked.
            var failed=new Window{Content=new Grid(),Width=480,Height=180,ShowActivated=false,ShowInTaskbar=false,Left=-12000};
            int attempts=0;var broken=new HostedToolPreparation(failed,"equipment",root,(_,_)=>{attempts++;throw new IOException("fixture connection failure");},()=>throw new Exception("Unexpected ready"));
            DailyDialogs.Prepare(failed);failed.Show();await Until(()=>attempts==1&&!broken.Preparing);await Task.Delay(300);
            Check(attempts==1&&!broken.Body.IsEnabled&&broken.ActionButton.IsEnabled&&!DailyToolControl.IsOccupied(root),"failed setup stays recoverable without reconnecting indefinitely");failed.Close();
            // Closing during preparation waits for cancellation and releases ownership.
            foreach(string id in new[]{"mansion-runaway","equipment","infinite-gacha"}){
                Environment.SetEnvironmentVariable("BD2_DAILY_HOSTED_TOOL",id);
                var closing=Create(id);
                closing.ShowActivated=false;closing.ShowInTaskbar=false;closing.WindowStartupLocation=WindowStartupLocation.Manual;closing.Left=-12000;
                var closingStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var cancelled=new HostedToolPreparation(closing,id,root,async(_,token)=>{closingStarted.SetResult();await Task.Delay(Timeout.Infinite,token);},()=>throw new Exception("Closed setup became ready"));
                DailyDialogs.Prepare(closing);closing.Show();await closingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));closing.Close();await Until(()=>!closing.IsVisible);
                Check(!DailyToolControl.IsOccupied(root)&&!cancelled.Ready,id+": actual window closing cancels preparation and never enables actions");
            }
            DailyJson.Write(Path.Combine(smoke!,"tool-preparation-checks.json"),new{status="passed",checks,count=checks.Count,realGameTouched=false});
        }
        finally {Environment.SetEnvironmentVariable("BD2_DAILY_HOSTED_TOOL",prior);}
    }
}
