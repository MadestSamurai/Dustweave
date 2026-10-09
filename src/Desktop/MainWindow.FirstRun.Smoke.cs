using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Dustweave.Accounts;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async Task CheckFirstRunForSmoke()
    {
        if (sessions is not DemoEnvironment) throw new Exception("First-run UI requires isolated sessions.");
        string locale = L.Code; int appearance = ThemeSelector.SelectedIndex;
        double width = Width, height = Height; int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T item) yield return item;
                foreach (var nested in Descendants<T>(child)) yield return nested;
            }
        }
        var store = new GameInstallation(Path.Combine(root, "game-path-preview"));
        try
        {
            Width = 980; Height = 720;
            foreach (var code in DailyLanguage.Codes)
            foreach (int mode in new[] { 1, 2 })
            {
                L.Select(code); ThemeSelector.SelectedIndex = mode;
                string local = Path.Combine(root, "first-run-" + code + "-" + mode);
                var fixture = new DemoEnvironment(local);
                var flow = new DailyFirstRun(fixture, fixture, local);
                var window = new FirstRunWindow(this, flow, store, local) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = 0, ConfirmSkipForSmoke = () => true };
                using (DailyDialogs.Dim(this))
                {
                    window.Show();
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Check(window.Stage == 0 && window.CanContinue && window.Title == L.Get("onboarding.title"), "First step is not ready or localized");
                    var language = Descendants<ComboBox>(window).Single(c => System.Windows.Automation.AutomationProperties.GetAutomationId(c) == "OnboardingLanguage");
                    Check(language.IsVisible && language.IsEnabled && (string)language.SelectedValue == code, "Setup language selector is unavailable before starting");
                    Check(language.Items.Cast<ComboBoxItem>().Select(i => (string)i.Content).SequenceEqual(new[] { "简体中文", "繁體中文", "English" }), "Language choices must use their native names");
                    string alternate = code == "en-US" ? "zh-TW" : "en-US";
                    async Task SwitchLanguage(string choice)
                    {
                        language.SelectedValue = choice;
                        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                        Check(L.Code == choice && LanguageSelector.SelectedIndex == Array.IndexOf(DailyLanguage.Codes, choice) && window.Title == L.Get("onboarding.title") && (string)RunTab.Header == L.Get("nav.daily"), "Setup and main window did not change language together");
                        Check(DailyJson.TryRead<DailyLanguage.LanguageSetting>(Path.Combine(root, "language.json"))?.Language == choice, "Setup language was not persisted for the next launch");
                    }
                    await SwitchLanguage(alternate); await SwitchLanguage(code);
                    Check(window.Stage == 0 && fixture.Calls.Count == 0, "Language choice started game verification");
                    if (code == "en-US")
                    {
                        double originalWidth = window.Width; window.Width = 480;
                        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                        var position = language.TranslatePoint(new Point(), window);
                        Check(position.X > 0 && position.X + language.ActualWidth < window.ActualWidth - 16 && language.ActualWidth >= 140, "Language selector is clipped in compact setup");
                        Capture("onboarding-language-compact-" + mode, (FrameworkElement)window.Content);
                        window.Width = originalWidth;
                        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    }
                    Capture("onboarding-path-" + code + "-" + mode, (FrameworkElement)window.Content);
                    await window.StartVerificationAsync();
                    Check(window.Stage == 2 && !window.CanContinue, "Detected account must wait for normal game exit");
                    var accountName = Descendants<TextBox>(window).Single(t => t.IsVisible && !t.IsReadOnly);
                    accountName.Text = "My account";
                    int calls = fixture.Calls.Count; var game = fixture.Game;
                    await SwitchLanguage(alternate); await SwitchLanguage(code);
                    Check(window.Stage == 2 && !window.CanContinue && accountName.Text == "My account" && fixture.Game == game && fixture.Calls.Count == calls, "Language change reset the guide, account name or verification state");
                    Capture("onboarding-detected-" + code + "-" + mode, (FrameworkElement)window.Content);
                    fixture.Game = null; await window.CheckClosedAsync();
                    Check(window.CanContinue, "Closing the game did not unlock saving");
                    window.SaveAccount();
                    Check(window.Saved && window.Stage == 3 && DailyJson.TryRead<FirstRunRecord>(Path.Combine(local, "first-run.json"))?.State == "tour", "Saved account did not lead to the tour");
                    Capture("onboarding-saved-" + code + "-" + mode, (FrameworkElement)window.Content);
                    Descendants<Button>(window).Single(x => x.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(!window.IsVisible, "Continue did not return to the main app");
                }
                Check(DailyDialogs.ModalDepth == 0, "Setup left a modal shade behind");
                BeginPageTour(0);
                for (int page = 0; page < TourPages.Length; page++)
                {
                    ShowTourPage(page); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Check(firstRunTour!.IsVisible && Unavailable && (TourPages[page].Tab == null || WorkspaceTabs.SelectedItem == TourPages[page].Tab), "Tour did not highlight the actual page or block automation");
                    if (code == "zh-CN" || page is 0 or 3) Capture("onboarding-tour-" + code + "-" + mode + "-" + page, this);
                }
                EndPageTour(true);
                Check(!onboardingOpen && DailyJson.TryRead<FirstRunRecord>(FirstRunPath)?.State == "completed", "Tour did not release the interface or remember completion");
            }
            // Exercise the actual nested confirmation, before any async verification has
            // started. Closing synchronously inside Closing used to leave setup open.
            foreach (int mode in new[] { 1, 2 })
            {
                ThemeSelector.SelectedIndex = mode;
                string local = Path.Combine(root, "first-run-skip-" + mode);
                var fixture = new DemoEnvironment(local);
                var initial = new FirstRunWindow(this, new(fixture, fixture, local), store, local) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = 0 };
                bool confirmed = false, nestedShade = false;
                var answer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                answer.Tick += (_, _) =>
                {
                    var dialog = initial.OwnedWindows.Cast<Window>().FirstOrDefault(w => w.IsVisible && w.Title == L.Get("onboarding.skip_title"));
                    if (dialog == null) return;
                    nestedShade = DailyDialogs.ModalDepth == 2;
                    confirmed = true; answer.Stop();
                    Descendants<Button>(dialog).Single(b => b.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                initial.Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(() => { initial.Close(); initial.Close(); }));
                answer.Start();
                try { DailyDialogs.ShowModal(initial); } finally { answer.Stop(); }
                Check(confirmed && nestedShade && !initial.IsVisible, "Initial skip did not confirm and close the actual modal window");
                Check(DailyDialogs.ModalDepth == 0 && IsEnabled, "Initial skip left the app dimmed or disabled");
                Check(!File.Exists(Path.Combine(local, "onboarding-error.json")), "Initial skip logged a closing error");
                Check(DailyJson.TryRead<FirstRunRecord>(Path.Combine(local, "first-run.json"))?.State == "skipped" && fixture.Calls.Count == 0, "Initial skip changed the game or did not persist");
            }
            var waiting = new DemoEnvironment(root) { Ready = false };
            var skipWindow = new FirstRunWindow(this, new(waiting, waiting, root), store, root) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = 0 };
            bool prompted = false;
            skipWindow.ConfirmSkipForSmoke = () => { prompted = true; return false; };
            skipWindow.Show(); skipWindow.Close();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(prompted && skipWindow.IsVisible, "Closing setup bypassed the skip warning");
            var verifying = skipWindow.StartVerificationAsync(); await Task.Delay(40);
            skipWindow.ConfirmSkipForSmoke = () => true; skipWindow.Close();
            await verifying.WaitAsync(TimeSpan.FromSeconds(3));
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(!skipWindow.IsVisible && waiting.Game != null && !waiting.Calls.Any(x => x.StartsWith("save:") || x == "close"), "Skip closed the game, saved an unverified account, or left a window behind");
            Check(DailyJson.TryRead<FirstRunRecord>(FirstRunPath)?.State == "skipped", "Skip was not remembered");
            DailyJson.Write(Path.Combine(smoke!, "first-run-ui.json"), new { status = "passed", checks, locales = DailyLanguage.Codes, realGameTouched = false });
        }
        finally { EndPageTour(false); L.Select(locale); ThemeSelector.SelectedIndex = appearance; Width = width; Height = height; }
    }
}
