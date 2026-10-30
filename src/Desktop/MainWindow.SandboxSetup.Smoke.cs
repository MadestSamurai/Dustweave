using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Dustweave.Accounts;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async Task CheckSandboxSetupForSmoke()
    {
        if (smoke == null || sessions is not DemoEnvironment) throw new InvalidOperationException("Isolated fixture required");
        L.Audit();
        var originalTab = WorkspaceTabs.SelectedItem; string locale = L.Code; int appearance = ThemeSelector.SelectedIndex;
        string installed = Path.Combine(root, "sandbox-setup-fixture", "沙盒 Sandboxie"); Directory.CreateDirectory(installed);
        string component = Path.Combine(installed, "SbieDll.dll");
        foreach (string name in new[] { "Start.exe", "SbieIni.exe", "SbieDll.dll" }) File.WriteAllText(Path.Combine(installed, name), "fixture; never execute");
        var store = new SandboxInstallation(Path.Combine(root, "sandbox-path-preview"));
        store.Select(installed);
        var panel = softwareSandboxPath;
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); checks++; }
        SandboxSetupWindow? Active() => Application.Current.Windows.OfType<SandboxSetupWindow>().SingleOrDefault();
        try
        {
            WorkspaceTabs.SelectedItem = SoftwareSettingsTab;
            await panel.RefreshAsync();
            Check(await panel.EnsureReadyAsync() && Active() == null && !panel.HelpVisible, "Valid installation showed a prompt");
            File.Delete(component);
            await panel.RefreshAsync();
            Check(panel.HelpVisible && panel.CanChoose && panel.DisplayedPath == installed && Active() == null, "Missing installation is not editable or passive refresh opened a dialog");
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Check(!await panel.EnsureReadyAsync(scheduled: true), "Unattended attempt authorized launch");
                Check(Application.Current.Windows.OfType<SandboxSetupWindow>().Count() == 1, "Scheduled attempts stacked prompts");
            }
            var notice = Active()!;
            await notice.RefreshAsync();
            Check(notice.DownloadVisible, "Missing installation lacks download help");
            await notice.VerifyAsync();
            Check(!notice.Completion.IsCompleted, "Recheck accepted an incomplete installation");
            notice.Close(); Check(!await notice.Completion, "Closing setup accepted a launch");
            await panel.RefreshAsync();

            foreach (string code in DailyLanguage.Codes) foreach (int mode in new[] { 1, 2 })
            {
                L.Select(code); ThemeSelector.SelectedIndex = mode;
                var window = new SandboxSetupWindow(this, store, SandboxSetupPurpose.Launch);
                window.Show(); await window.RefreshAsync();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Check(window.DownloadVisible && window.CanChoose && window.ActualHeight < window.MaxHeight, "Setup contents overflow or lack recovery actions");
                var buttons = SandboxButtons(window).Where(b => b.Name.StartsWith("Sandbox") && b.IsVisible).ToArray();
                Check(buttons.Length == 5 && buttons.All(b => b.ActualWidth > 30 && b.ActualHeight >= 30 && !b.Content.ToString()!.StartsWith("sandbox_")), "Missing or untranslated setup actions");
                Check(buttons.Single(b => b.Name == "SandboxDownload").IsDefault && buttons.Single(b => b.Name == "SandboxDownload").Style == FindResource("PrimaryButton")
                    && !buttons.Single(b => b.Name == "SandboxVerify").IsDefault, "Missing installation did not prioritize downloading");
                CaptureSandboxDialog(window, "sandbox-setup-" + code + "-" + mode);
                var details = SandboxElements<Expander>(window).Single();
                details.IsExpanded = false;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Check(SandboxButtons(window).Count(b => b.Name.StartsWith("Sandbox") && b.IsVisible) == 3, "Manual paths did not collapse behind the installed option");
                CaptureSandboxDialog(window, "sandbox-setup-simple-" + code + "-" + mode);
                window.Close(); Check(!await window.Completion, "Preview close authorized a launch");
            }

            // Drive the real modal gate. Reentrant starts must not inherit a
            // successful confirmation and create a second queue.
            var exercised = new TaskCompletionSource<bool>();
            var driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            driver.Tick += async (_, _) => {
                var window = Active(); if (window == null || !window.CanChoose) return;
                driver.Stop();
                try {
                    Check(!await panel.EnsureReadyAsync(), "Duplicate startup was accepted during preparation");
                    Check(!RunAccountsButton.IsEnabled && !ConnectButton.IsEnabled && !AccountsGrid.IsEnabled, "Preparation left conflicting account actions enabled");
                    Check(window.DisplayedPath == installed, "Broken configured path is hidden");
                    await window.SelectAsync(Path.Combine(installed, "not-installed"));
                    Check(store.Read().Directory == installed, "Invalid edit overwrote saved path");
                    File.WriteAllText(component, "fixture; never execute");
                    await window.SelectAsync(installed);
                    Check(!window.DownloadVisible && SandboxButtons(window).Single(b => b.Name == "SandboxVerify").IsDefault, "Detected installation did not restore Continue as primary");
                    Check(!window.Completion.IsCompleted, "Selecting a folder started the queue without confirmation");
                    await window.VerifyAsync();
                    exercised.TrySetResult(true);
                } catch (Exception error) { exercised.TrySetException(error); window.Close(); }
            };
            driver.Start();
            try { Check(await panel.EnsureReadyAsync(), "Confirmed valid installation did not unblock launch"); await exercised.Task; }
            finally { driver.Stop(); }
            Check(!panel.Preparing && Active() == null, "Setup left controls locked");
            Check(await panel.EnsureReadyAsync() && Active() == null, "Working installation kept prompting");
            File.Delete(component);
            var cancelDriver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            cancelDriver.Tick += (_, _) => { if (Active() is { } dialog) { cancelDriver.Stop(); dialog.Close(); } };
            cancelDriver.Start();
            try { Check(!await panel.EnsureReadyAsync(), "Cancelling the real startup gate allowed execution"); }
            finally { cancelDriver.Stop(); }
            Check(!panel.Preparing && ConnectButton.IsEnabled, "Cancel did not restore account controls");
            File.WriteAllText(component, "fixture; never execute"); await panel.RefreshAsync();
            var readOnly = new SandboxSetupWindow(this, store, SandboxSetupPurpose.Settings, true);
            readOnly.Show(); await readOnly.RefreshAsync();
            Check(!readOnly.CanChoose, "Sandbox child can edit host location"); readOnly.Close();
            DailyJson.Write(Path.Combine(smoke, "sandbox-setup.json"), new { status = "passed", checks, realGameTouched = false, locales = DailyLanguage.Codes });
        }
        finally
        {
            Active()?.Close();
            L.Select(locale); ThemeSelector.SelectedIndex = appearance; WorkspaceTabs.SelectedItem = originalTab;
        }
    }
    private void CaptureSandboxDialog(Window window, string name)
    {
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(smoke!, name + ".png")); encoder.Save(stream);
    }
    private static IEnumerable<Button> SandboxButtons(DependencyObject node) => SandboxElements<Button>(node);
    private static IEnumerable<T> SandboxElements<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T element) yield return element;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in SandboxElements<T>(System.Windows.Media.VisualTreeHelper.GetChild(node, i))) yield return child;
    }
}
