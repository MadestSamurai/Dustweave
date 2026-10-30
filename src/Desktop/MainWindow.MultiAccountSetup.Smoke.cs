using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Dustweave.Accounts;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async Task CheckMultiAccountSetupForSmoke()
    {
        if (smoke == null || sessions is not DemoEnvironment demo) throw new InvalidOperationException("Isolated sessions required");
        L.Audit(); string locale = L.Code; int appearance = ThemeSelector.SelectedIndex, checks = 0, initialCalls = demo.Calls.Count;
        string setupPath = Path.Combine(root, "multi-account-setup.json");
        byte[]? initialSetup = File.Exists(setupPath) ? File.ReadAllBytes(setupPath) : null;
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); checks++; }
        string installed = Path.Combine(root, "multi-account-fixture"); Directory.CreateDirectory(installed);
        foreach (string name in new[] { "Start.exe", "SbieIni.exe", "SbieDll.dll" }) File.WriteAllText(Path.Combine(installed, name), "fixture; never execute");
        var store = new SandboxInstallation(Path.Combine(root, "sandbox-path-preview")); store.Select(installed);
        string component = Path.Combine(installed, "SbieDll.dll"); File.Delete(component);
        try
        {
            foreach (string code in DailyLanguage.Codes) foreach (int theme in new[] { 1, 2 })
            {
                L.Select(code); ThemeSelector.SelectedIndex = theme;
                string local = Path.Combine(root, "multi-account-" + code + "-" + theme); Directory.CreateDirectory(local);
                var firstRun = new FirstRunRecord(State: "completed"); DailyJson.Write(Path.Combine(local, "first-run.json"), firstRun);
                DailyMultiAccountSetup.AfterSave(local, 1, 2, true);
                var window = new MultiAccountSetupWindow(this, local, store) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000 };
                window.Show(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var choices = SandboxElements<RadioButton>(window).ToArray();
                Check(choices.Length == 2 && choices.All(x => x.IsVisible) && choices.Single(x => x.Name == "MultiAccountSequential").IsChecked == true, "Mode choices are missing or do not default to sequential");
                choices.Single(x => x.Name == "MultiAccountParallel").IsChecked = true;
                Check(DailyMultiAccountSetup.Read(local)?.Parallel == true, "Pending parallel choice was not persisted");
                if (code == "en-US") window.Width = 440;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Check(window.ActualHeight < window.MaxHeight && SandboxElements<Button>(window).Where(b => b.IsVisible).All(b => b.ActualWidth >= 30), "Compact setup clipped actions");
                CaptureSandboxDialog(window, "multi-account-" + code + "-" + theme);
                choices.Single(x => x.Name == "MultiAccountSequential").IsChecked = true;
                await window.FinishAsync();
                Check(!window.IsVisible && DailyMultiAccountSetup.Read(local)?.State == "completed" && DailyJson.TryRead<DailyParallelOptions>(Path.Combine(local, "parallel-options.json")) is { Enabled: false }, "Sequential confirmation required installation or did not persist");
                Check(DailyJson.TryRead<FirstRunRecord>(Path.Combine(local, "first-run.json")) == firstRun, "Multi-account setup modified first-use login state");
                var observed = demo.Read(); var first = observed.Accounts[0];
                RefreshAccounts(observed with { Accounts = [first], CurrentKey = first.AccountKey, SessionComplete = true, GameRunning = false, StarterRunning = false });
                Check(LoginHintText.Text == L.Get("multiaccount.add_second") && LoginNewButton.IsEnabled, "Saved first account does not explain how to add another account");
                RefreshAccounts(observed with { Accounts = [first], CurrentKey = first.AccountKey, GameRunning = true });
                Check(LoginHintText.Text == L.Get("account.login_running") && !LoginNewButton.IsEnabled, "Second-account hint replaced the active-game instruction");
                RefreshAccounts(observed with { GameRunning = false, StarterRunning = false });
                Check(LoginHintText.Text == L.Get("account.login_saved"), "Second-account hint repeated after more accounts were saved");
                RefreshAccounts(observed);
            }
            string retryRoot = Path.Combine(root, "multi-account-retry"); Directory.CreateDirectory(retryRoot);
            string optionsPath = Path.Combine(retryRoot, "parallel-options.json");
            DailyJson.Write(optionsPath, new DailyParallelOptions(false, 3, false)); DailyMultiAccountSetup.Write(retryRoot, new(Parallel: true));
            var retry = new MultiAccountSetupWindow(this, retryRoot, store) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000 }; retry.Show();
            var cancellation = new TaskCompletionSource();
            var cancelDriver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            cancelDriver.Tick += async (_, _) => {
                var dialog = retry.OwnedWindows.OfType<SandboxSetupWindow>().FirstOrDefault(); if (dialog == null || !dialog.CanChoose) return;
                cancelDriver.Stop();
                try { await dialog.VerifyAsync(); Check(!dialog.Ready, "Incomplete installation passed"); dialog.Close(); cancellation.TrySetResult(); }
                catch (Exception error) { cancellation.TrySetException(error); dialog.Close(); }
            };
            cancelDriver.Start();
            try { await retry.FinishAsync(); await cancellation.Task; } finally { cancelDriver.Stop(); }
            Check(retry.IsVisible && retry.CanContinue && DailyMultiAccountSetup.Read(retryRoot) is { State: "pending", Parallel: true }, "Cancelled installation lost the pending choice");
            Check(DailyJson.TryRead<DailyParallelOptions>(optionsPath) is { Enabled: false, Maximum: 3, CloseGame: false }, "Cancelling installation changed saved execution options");
            var pending = DailyMultiAccountSetup.Read(retryRoot)!; retry.Close();
            Check(DailyMultiAccountSetup.Read(retryRoot)?.State == "dismissed" && !DailyMultiAccountSetup.Pending(retryRoot, 2), "Closing setup did not suppress future prompts");
            DailyMultiAccountSetup.Write(retryRoot, pending); // Restart with the pending record left by an interrupted installation.
            var resumed = new MultiAccountSetupWindow(this, retryRoot, store) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000 }; resumed.Show();
            Check(SandboxElements<RadioButton>(resumed).Single(x => x.Name == "MultiAccountParallel").IsChecked == true, "Restart lost the pending mode");
            var completion = new TaskCompletionSource();
            var installDriver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            installDriver.Tick += async (_, _) => {
                var dialog = resumed.OwnedWindows.OfType<SandboxSetupWindow>().FirstOrDefault(); if (dialog == null || !dialog.CanChoose) return;
                installDriver.Stop();
                try { File.WriteAllText(component, "fixture; never execute"); await dialog.VerifyAsync(); completion.TrySetResult(); }
                catch (Exception error) { completion.TrySetException(error); dialog.Close(); }
            };
            installDriver.Start();
            try { await resumed.FinishAsync(); await completion.Task; } finally { installDriver.Stop(); }
            Check(!resumed.IsVisible && DailyJson.TryRead<DailyParallelOptions>(optionsPath) is { Enabled: true, Maximum: 3, CloseGame: false }, "Installed parallel setup did not preserve unrelated settings");
            Check(demo.Calls.Count == initialCalls, "Setup started game activity");

            // Exercise the actual Accounts Save handler, including repeat updates and a third account.
            var accounts = demo.Accounts.ToArray(); string current = demo.CurrentKey; var game = demo.Game;
            try
            {
                demo.Accounts.Clear(); demo.Accounts.Add(accounts[0]); demo.CurrentKey = accounts[1].AccountKey; demo.Game = null; RefreshAccounts();
                WorkspaceTabs.SelectedItem = AccountsTab; int offers = 0;
                async Task SaveThroughUi(bool expectOffer)
                {
                    bool confirmedName = false; var saved = new TaskCompletionSource();
                    var driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                    driver.Tick += (_, _) => {
                        try
                        {
                            if (OwnedWindows.OfType<MultiAccountSetupWindow>().FirstOrDefault() is { } choice)
                            { offers++; Check(SavedAccountCount(sessions.Read()) == 2, "Setup appeared before the second account was saved"); choice.Close(); }
                            else if (OwnedWindows.OfType<Window>().FirstOrDefault(w => w.IsVisible && SandboxElements<TextBox>(w).Any()) is { } prompt)
                            { confirmedName = true; SandboxElements<Button>(prompt).Single(b => b.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                            else if (confirmedName && !onboardingOpen) { driver.Stop(); saved.TrySetResult(); }
                        }
                        catch (Exception error) { driver.Stop(); saved.TrySetException(error); }
                    };
                    int beforeOffers = offers; driver.Start();
                    try { Save_Click(this, new()); await saved.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
                    finally { driver.Stop(); }
                    Check(offers - beforeOffers == (expectOffer ? 1 : 0), "Save offered setup at the wrong account count");
                }
                await SaveThroughUi(true); Check(DailyMultiAccountSetup.Read(root)?.State == "dismissed", "Not now was not remembered");
                await SaveThroughUi(false); demo.CurrentKey = accounts[2].AccountKey; RefreshAccounts(); await SaveThroughUi(false);
                Check(SavedAccountCount(sessions.Read()) == 3 && demo.Calls.Skip(initialCalls).All(c => c.StartsWith("save:")), "Account-save guide altered game behavior");
            }
            finally { demo.Accounts.Clear(); demo.Accounts.AddRange(accounts); demo.CurrentKey = current; demo.Game = game; RefreshAccounts(); }
            DailyJson.Write(Path.Combine(smoke, "multi-account-setup.json"), new { status = "passed", checks, realGameTouched = false, locales = DailyLanguage.Codes });
        }
        finally
        {
            demo.Calls.RemoveRange(initialCalls, demo.Calls.Count - initialCalls);
            if (initialSetup != null) File.WriteAllBytes(setupPath, initialSetup); else File.Delete(setupPath);
            L.Select(locale); ThemeSelector.SelectedIndex = appearance;
        }
    }
}
