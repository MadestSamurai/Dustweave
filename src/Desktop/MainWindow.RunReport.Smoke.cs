using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async Task CheckRunReportForSmoke()
    {
        var draw = new DailyDrawSummary(6, [
            new("Costume",101,1,5,"",new("魔法少女 · 艾莉婕","魔法少女 · 艾莉婕","Magical Girl · Elize"),true,true),
            new("Costume",102,2,3,"",new("白猫 · 鲁","白貓 · 魯","White Cat · Rou"),false,true),
            new("Equipment",201,1,5,"UR",new("血之吞噬者","血之吞噬者","Blood Devourer"),false,true),
            new("Equipment",202,2,3,"SR",new("魔法杖","魔法杖","Magic Staff"),false,true)]);
        var view = new QueueView("partial","","fixture/run-report.json", [
            new("guild","completed",""), new QueueStage("free_draws","completed",""){Draws=draw},
            new QueueStage("event_battle","completed","event_auto_failed_swept"){HasNotice=true},
            new QueueStage("weekly_mainline","partial","卡带 6 · 地图 608") { Locations = new() {
                ["p6"] = new("异教之塔","異教之塔","Tower of Desire"), ["m608"] = new("第八层","第八層","Eighth Floor") } }], new string('a',64));
        L.Audit(); WorkspaceTabs.SelectedItem=RunTab;
        foreach (int language in new[]{0,1,2}) foreach (int appearance in new[]{1,2})
        {
            LanguageSelector.SelectedIndex=language; ThemeSelector.SelectedIndex=appearance;
            dailyPanel.Show(view); dailyPanel.ShowHistory();
            if (DailyResultText.Detail(view.Stages[3]).Contains("608") || !DailyResultText.Detail(view.Stages[1]).Contains("1"))
                throw new Exception("Map names or draw counts did not reach the timeline");
            await Dispatcher.InvokeAsync(()=>{}, DispatcherPriority.ApplicationIdle);
            Capture($"result-timeline-{language}-{appearance}");
            var report = DailyResultWindow.RunResults([new("Fixture A",view),new("Fixture B",view with {Stages=[new("mail","completed","")]})]); report.Owner=this;
            var details = DailyResultWindow.DrawDetails(draw); details.Owner=this;
            var tasks = dailyPanel.OpenTaskDetailsForSmoke();
            try
            {
                report.Show(); details.Show();
                if(language==2) {report.Width=500;report.Height=440;details.Width=500;details.Height=440;}
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                if (report.ResultsScroll.ActualHeight<100 || details.ResultsScroll.ActualHeight<100) throw new Exception("Report content is clipped");
                foreach(var window in new Window[]{report,details,tasks})
                    if (!Equals(((SolidColorBrush)window.Background).Color, ((SolidColorBrush)FindResource("Surface")).Color))
                        throw new Exception("Result dialog background differs from shared surface");
                Capture($"run-results-{language}-{appearance}",(FrameworkElement)report.Content);
                Capture($"draw-details-{language}-{appearance}",(FrameworkElement)details.Content);
                Capture($"task-details-{language}-{appearance}",(FrameworkElement)tasks.Content);
            }
            finally {report.Close();details.Close();tasks.Close();}
        }
    }
}

