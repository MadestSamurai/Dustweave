using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async Task CheckSchedulingForSmoke()
    {
        static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
                if (child is T match) yield return match;
                foreach (var item in Descendants<T>(child)) yield return item;
            }
        }
        var fixture = (DemoEnvironment)host; int before = fixture.Calls.Count;
        string locale = L.Code; double width = Width, height = Height;
        int checks = 0;
        void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); checks++; }
        try
        {
            WorkspaceTabs.SelectedItem = AccountsTab;
            foreach (var row in rows) row.Selected = true;
            UpdateSelection();
            Check(RunAccountsButton.IsEnabled && RunAccountsButton.Style == FindResource("PrimaryButton"), "Account queue primary action missing");
            SetBusy(true); Check(!RunAccountsButton.IsEnabled, "Queue start enabled during another operation"); SetBusy(false);
            Check(!Descendants<Button>(dailyPanel).Any(b => b.Content?.ToString() == L.Get("run.multi", rows.Count)), "Multi-account action still in Today");
            var plan = new DailySchedulePlan { Enabled = true, Hour = 9, Minute = 15, Accounts = rows.Select(r => r.Account.AccountKey).ToArray(), Days = [1, 3, 5], SavedUtc = DateTimeOffset.UtcNow };
            schedulePanel.Show(plan, new("fixture", "completed", DateTimeOffset.UtcNow));
            schedulePanel.SetAccounts(rows.Select(r => r.Account));
            foreach (string language in DailyLanguage.Codes)
            {
                L.Select(language); Width = 1180; Height = 800; WorkspaceTabs.SelectedItem = ScheduleTab;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Capture("schedule-" + language);
                Check(Descendants<TextBlock>(schedulePanel).Any(t => t.Text == L.Get("schedule.accounts")), "Schedule locale stale");
                WorkspaceTabs.SelectedItem = UpdatesTab;
                updatePanel.Status("updates.ready", "0.9.1"); updatePanel.Busy(false, true, false);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("updates-" + language);
                Check(Descendants<Button>(updatePanel).Any(b => b.Content?.ToString() == L.Get("updates.install")), "Update action locale stale");
            }
            L.Select("en-US"); Width = 920; Height = 650;
            WorkspaceTabs.SelectedItem = AccountsTab; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("accounts-queue-compact");
            Check(RunAccountsButton.TransformToAncestor(this).TransformBounds(new Rect(RunAccountsButton.RenderSize)).Right <= ActualWidth, "Queue action clipped");
            WorkspaceTabs.SelectedItem = ScheduleTab; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("schedule-compact");
            Check(!File.Exists(scheduleStore.PlanPath), "Smoke persisted a real schedule");
            Check(fixture.Calls.Count == before, "Schedule/update UI called the game");
            Check(DailyReleaseHistory.ShouldShow(root, "0.9.0"), "New version notice suppressed");
            var notice = CreateReleaseNotesWindow();
            notice.ShowActivated = false; notice.ShowInTaskbar = false; notice.WindowStartupLocation = WindowStartupLocation.Manual; notice.Left = -12000; notice.Top = 0;
            notice.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            notice.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(notice.ActualWidth), (int)Math.Ceiling(notice.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(notice);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(smoke!, "release-notice.png"))) encoder.Save(file);
            notice.Close();
            Check(!DailyReleaseHistory.ShouldShow(root, "0.9.0") && DailyReleaseHistory.ShouldShow(root, "0.9.1"), "Version notice repeat logic failed");
            DailyReleaseHistory.MarkSeen(root, "0.9.1");
            Check(!DailyReleaseHistory.ShouldShow(root, "0.9.0"), "Rolling back repeated already read notes");
            DailyJson.Write(Path.Combine(smoke!, "scheduling-updates.json"), new { status = "passed", checks, gameCommands = 0, windowsTasksRegistered = 0, realGameTouched = false });
        }
        finally { L.Select(locale); Width = width; Height = height; WorkspaceTabs.SelectedItem = RunTab; updatePanel.Busy(false, false, false); }
    }
}
