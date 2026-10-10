using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async Task CheckTaskNavigationForSmoke()
    {
        var fixture = (DemoEnvironment)sessions;
        int calls = fixture.Calls.Count, checks = 0;
        string original = fixture.CurrentKey;
        void Check(bool condition, string why) { if (!condition) throw new Exception(why); checks++; }
        static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is T match) yield return match;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }
        var a = fixture.Accounts[0]; var b = fixture.Accounts[1];
        string record = Path.Combine(root, "live", "queues", Guid.NewGuid().ToString("N"), "result.json");
        DailyJson.Write(record, new { state = "paused", context = new { actor = new object[] { 1, 2, "fixture", b.AccountKey, "player" } }, items = new[] { new { task = "mail", state = "pending" } } });
        DailyQueueHistory.Remember(root, b.AccountKey, record);
        var store = new DailyPreferenceStore(root);
        var bPrefs = new DailyPreferences(); bPrefs.Mirror.Enabled = false; store.Save(b.AccountKey, bPrefs);
        try
        {
            ViewAccountTasks(a.AccountKey); dailyPanel.ShowCurrentPlan(); dailyPanel.ClearSelection(); dailyPanel.SelectPlanTask("free_draws", true);
            var selector = Descendants<ComboBox>(dailyPanel).Single();
            selector.SelectedValue = b.AccountKey;
            Check(TaskAccountKey == b.AccountKey && fixture.CurrentKey == original && fixture.Calls.Count == calls, "Choosing tasks switched the game account");
            Check(!dailyPanel.ShowingPlan && dailyPanel.VisibleTasks.SequenceEqual(new[] { "mail" }), "Account history was not selected by identity");
            dailyPanel.ShowCurrentPlan();
            Check(!dailyPanel.VisibleTasks.Contains("mirror"), "Selected account used another account settings");
            dailyPanel.ClearSelection(); dailyPanel.SelectPlanTask("mail", true);
            fixture.CurrentKey = fixture.Accounts[2].AccountKey;
            RefreshAccounts(); RefreshDailyHistory(true);
            Check(TaskAccountKey == b.AccountKey && (string?)selector.SelectedValue == b.AccountKey && dailyPanel.SelectedPlanTasks.SequenceEqual(new[] { "mail" }), "Background account/history refresh stole the task selection");
            ViewAccountTasks(a.AccountKey); dailyPanel.ShowCurrentPlan();
            Check(dailyPanel.SelectedPlanTasks.SequenceEqual(new[] { "free_draws" }), "Returning to an account lost its draft selection");
            ViewAccountTasks(b.AccountKey); dailyPanel.ShowCurrentPlan();
            Check(dailyPanel.SelectedPlanTasks.SequenceEqual(new[] { "mail" }), "Draft selections leaked across accounts");
            QueuePlanRequest? request = null; void Observe(QueuePlanRequest value) => request = value;
            dailyPanel.PlanRequested += Observe; dailyPanel.StartCurrentSelection(); dailyPanel.PlanRequested -= Observe;
            Check(request?.Account == b.AccountKey && request.Tasks.SequenceEqual(new[] { "mail" }), "Selection sent the logged-in account rather than the viewed account");
            QueueRetryRequest? retryRequest = null; void Retry(QueueRetryRequest value) => retryRequest = value;
            dailyPanel.ShowHistory(); dailyPanel.SelectUnfinished(); dailyPanel.RetryRequested += Retry;
            Descendants<Button>(dailyPanel).Single(x => x.Content as string == L.Get("run.retry", 1)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dailyPanel.RetryRequested -= Retry;
            Check(retryRequest?.Account == b.AccountKey && retryRequest.Record == record, "Retry lost the selected account record");
            dailyPanel.ShowCurrentPlan();
            SetBusy(true); Check(!selector.IsEnabled, "Account selector remains enabled during execution"); SetBusy(false);
            foreach (var language in new[] { 0, 1, 2 }) foreach (var appearance in new[] { 1, 2 })
            {
                LanguageSelector.SelectedIndex = language; ThemeSelector.SelectedIndex = appearance; Width = 920; Height = 650;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout();
                dailyPanel.CheckViewportForSmoke();
                dailyPanel.CheckHeaderActionForSmoke();
                Check(!Descendants<TextBlock>(dailyPanel).Any(t => t.Text.StartsWith("run.")), "Untranslated task navigation text");
                Check(TaskAccountKey == b.AccountKey && dailyPanel.SelectedPlanTasks.SequenceEqual(new[] { "mail" }), "Presentation change erased task draft");
                Capture("task-account-" + L.Code + "-" + appearance);
            }
            Check(fixture.Calls.Count == calls, "Task navigation executed a game/session operation");
            DailyRunPanel.CheckAccountProgressForSmoke(root);
            var today = new QueuePeriod("fixture", "today", "player", DateTimeOffset.UtcNow.AddHours(1).UtcTicks);
            dailyPanel.LoadHistory(new("completed", "", "", [new("free_draws","completed","",FinishedAt:DateTimeOffset.UtcNow),new("mail","completed","",FinishedAt:DateTimeOffset.UtcNow)], b.AccountKey, today));
            dailyPanel.ShowCurrentPlan(); UpdateLayout(); Capture("task-inherited-progress");
            bool rejected = false;
            try { ViewAccountTasks(new string('d', 64)); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && TaskAccountKey == b.AccountKey, "Deleted or unknown account was accepted for tasks");
            await ConnectTaskAccountAsync(b.AccountKey);
            Check(fixture.CurrentKey == b.AccountKey && fixture.Calls.Skip(calls).Contains("launch:" + b.SlotNumber), "Explicit start did not connect the selected account");
            int afterSwitch = fixture.Calls.Count;
            await ConnectTaskAccountAsync(b.AccountKey);
            Check(!fixture.Calls.Skip(afterSwitch).Any(x => x == "close" || x.StartsWith("launch")), "Already selected game was needlessly restarted");
            afterSwitch = fixture.Calls.Count;
            rejected = false;
            try { await ConnectTaskAccountAsync(new string('d', 64)); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && fixture.Calls.Count == afterSwitch, "Missing target fell back to the logged-in account");
            DailyJson.Write(Path.Combine(smoke!, "task-navigation-ui.json"), new { status = "passed", checks, realGameTouched = false });
        }
        finally
        {
            fixture.CurrentKey = original; viewedAccount = ""; RefreshAccounts();
            LanguageSelector.SelectedIndex = 0; ThemeSelector.SelectedIndex = 1; Width = 1180; Height = 800;
            File.Delete(record); File.Delete(Path.Combine(root, "queue-history", b.AccountKey + ".json")); File.Delete(store.PathFor(b.AccountKey));
        }
    }
    private async Task CheckParallelForSmoke()
    {
        int calls=((DemoEnvironment)sessions).Calls.Count;
        var original=RunTab.Content; int checks=0;
        void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        static IEnumerable<T> Children<T>(DependencyObject node) where T:DependencyObject
        {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var child=VisualTreeHelper.GetChild(node,i);if(child is T result)yield return result;foreach(var nested in Children<T>(child))yield return nested;}
        }
        try
        {
            Width=920;Height=650;WorkspaceTabs.SelectedItem=RunTab;RunTab.Content=parallelPanel;
            var accounts=new[]{new DailyAccount(1,"主账号 / Main",new string('a',64),"",true,false,""),new DailyAccount(2,"副账号 / Second",new string('b',64),"",true,false,""),new DailyAccount(3,"等待账号 / Waiting",new string('c',64),"",true,false,"")};
            var items=accounts.Select((a,i)=>{var job=new DailyParallelJob(Guid.NewGuid().ToString("N"),a.AccountKey,a.Name,System.Text.Json.JsonSerializer.Serialize(new DailyPreferences()),true);string state=i==0?"running":i==1?"paused":"waiting";return new DailyParallelItem(a,job,state,i==1?"parallel.paused":"",new(job.Id,a.AccountKey,state,"",DateTimeOffset.UtcNow,1,1,GameId:i<2?1:0,Queue:i==2?null:new("running","","",[new("management","completed",""),new("mail","running","")],a.AccountKey)));}).ToArray();
            var run=new DailyParallelRun(Guid.NewGuid().ToString("N"),new(true,2),DateTimeOffset.UtcNow,items);
            foreach(int language in new[]{0,1,2})foreach(int appearance in new[]{1,2})
            {
                LanguageSelector.SelectedIndex=language;ThemeSelector.SelectedIndex=appearance;parallelPanel.Show(run);
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();
                Check(!Children<TextBlock>(parallelPanel).Any(t=>t.Text.StartsWith("parallel.")),"Parallel panel exposes an untranslated key");
                var buttons=Children<Button>(parallelPanel).ToArray();Check(buttons.Any(b=>b.Content as string==L.Get("parallel.pause_all")),"Global pause missing");
                Check(buttons.Any(b=>b.Content as string==L.Get("parallel.resume")&&b.IsEnabled),"Paused account cannot continue");
                Check(buttons.Any(b=>b.Content as string==L.Get("parallel.game")&&!b.IsEnabled),"Queued account exposes a game that has not started");
                string? controlled=null;void Control(string key,string action)=>controlled=key+":"+action;
                parallelPanel.ControlRequested+=Control;
                buttons.First(b=>b.Content as string==L.Get("parallel.resume")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                parallelPanel.ControlRequested-=Control;Check(controlled==accounts[1].AccountKey+":resume","Row action targeted another account");
                Check(!buttons.Single(b=>b.Content as string==L.Get("parallel.back")).IsEnabled,"Active batch can be discarded while workers run");
                var finishedRun=run with { Items=run.Items.Select(x=>x with {State="completed", Detail="", Status=x.Status! with { Queue=new("completed","","",[new("management","completed",""),new("mail","completed","")],x.Account.AccountKey) }}).ToArray() };
                parallelPanel.Show(finishedRun);UpdateLayout();
                var back=Children<Button>(parallelPanel).Single(b=>b.Content as string==L.Get("parallel.back"));
                Check(back.IsEnabled,"Finished batch cannot return to account tasks");
                int windows=Application.Current.Windows.Count;
                Capture("parallel-"+L.Code+"-"+appearance);
                back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(ReferenceEquals(RunTab.Content,dailyPanel)&&Application.Current.Windows.Count==windows,"Batch return opened a secondary window instead of the account list");
                Check(!Children<Button>(dailyPanel).Any(b=>b.Content as string=="批量总览"),"Dismissed batch can be reopened from daily tasks");
                RunTab.Content=parallelPanel;
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            }
            foreach(int appearance in new[]{1,2})
            {
                ThemeSelector.SelectedIndex=appearance;LanguageSelector.SelectedIndex=appearance==1?0:2;
                Exception? dialogError=null;
                var closeDialog=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(220) };
                closeDialog.Tick+=(_,_)=>
                {
                    closeDialog.Stop();
                    var dialog=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w.Owner==this);
                    try
                    {
                        Check(dialog!=null,"Parallel settings dialog did not open");dialog!.UpdateLayout();
                        Check(Children<ComboBox>(dialog).Any(c=>(int?)c.SelectedItem==2),"Parallel settings must default to two accounts");
                        Check(Children<TextBox>(dialog).Any(t=>t.IsReadOnly&&t.Text.Contains("sandboxie-plus.com")),"Installation guidance is not available");
                        Capture("parallel-settings-"+appearance,dialog);
                    }
                    catch(Exception error){dialogError=error;}
                    finally{dialog?.Close();}
                };
                closeDialog.Start();ExecutionMode_Click(this,new RoutedEventArgs());closeDialog.Stop();
                if(dialogError!=null)throw dialogError;
            }
            Check(((DemoEnvironment)sessions).Calls.Count==calls,"Parallel presentation touched a game or session");
            DailyJson.Write(Path.Combine(smoke!,"parallel-ui.json"),new{status="passed",checks,realGameTouched=false});
        }
        finally{RunTab.Content=original;LanguageSelector.SelectedIndex=0;ThemeSelector.SelectedIndex=1;Width=1180;Height=800;}
    }
}
