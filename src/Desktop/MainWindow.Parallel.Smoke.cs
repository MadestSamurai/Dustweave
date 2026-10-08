using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dustweave.Desktop;

public partial class MainWindow
{
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
                Capture("parallel-"+L.Code+"-"+appearance);
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
