using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Dustweave.Accounts;

namespace Dustweave.Desktop;

internal sealed class FirstRunWindow : Window
{
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly DailyFirstRun flow;
    private readonly GameInstallation installation;
    private readonly string root;
    private readonly GameInstallationPanel location;
    private readonly TextBlock progress = new(), heading = new() { FontSize = 24, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 16) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) };
    private readonly TextBox name = new() { MaxLength = 64, Margin = new(0, 6, 0, 0) };
    private readonly StackPanel identity = new();
    private readonly ComboBox language = new() { Width = 148, MinHeight = 36, SelectedValuePath = "Tag", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button next = new() { IsDefault = true }, skip = new() { Margin = new(0, 0, 10, 0) }, retry = new() { Margin = new(0, 0, 10, 0), Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? verification;
    private Task? pending;
    private bool allowClose, closing, polling;
    internal bool Saved { get; private set; }
    internal int Stage { get; private set; }
    internal bool CanContinue => next.IsEnabled;
    internal Func<bool>? ConfirmSkipForSmoke { get; set; }

    internal FirstRunWindow(Window owner, DailyFirstRun flow, GameInstallation installation, string root)
    {
        Owner = owner; this.flow = flow; this.installation = installation; this.root = root;
        Width = Math.Min(680, SystemParameters.WorkArea.Width - 64); Height = Math.Min(570, SystemParameters.WorkArea.Height - 80);
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        L.Bind(this, TitleProperty, "onboarding.title");
        var body = new DockPanel { Margin = new(24, 8, 24, 24) };
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 18, 0, 0) };
        L.Bind(skip, ContentControl.ContentProperty, "onboarding.skip"); L.Bind(retry, ContentControl.ContentProperty, "onboarding.retry");
        next.Style = (Style)FindResource("PrimaryButton"); actions.Children.Add(skip); actions.Children.Add(retry); actions.Children.Add(next);
        DockPanel.SetDock(actions, Dock.Bottom); body.Children.Add(actions);
        var top = new DockPanel { Margin = new(0, 0, 0, 18) };
        var languages = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var languageLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 10, 0) };
        languageLabel.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); L.Text(languageLabel, "onboarding.language");
        L.Bind(language, System.Windows.Automation.AutomationProperties.NameProperty, "onboarding.language");
        System.Windows.Automation.AutomationProperties.SetAutomationId(language, "OnboardingLanguage");
        language.Items.Add(new ComboBoxItem { Content = "简体中文", Tag = "zh-CN" });
        language.Items.Add(new ComboBoxItem { Content = "繁體中文", Tag = "zh-TW" });
        language.Items.Add(new ComboBoxItem { Content = "English", Tag = "en-US" });
        language.SelectedValue = L.Code; language.SelectionChanged += SelectLanguage;
        WeakEventManager<DailyLanguage, EventArgs>.AddHandler(L, nameof(DailyLanguage.Changed), LanguageChanged);
        languages.Children.Add(languageLabel); languages.Children.Add(language);
        DockPanel.SetDock(languages, Dock.Right); top.Children.Add(languages);
        progress.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); progress.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(progress); DockPanel.SetDock(top, Dock.Top); body.Children.Add(top);
        var content = new StackPanel(); content.Children.Add(heading); content.Children.Add(message);
        location = new(installation, false); content.Children.Add(location);
        var label = new TextBlock(); L.Text(label, "onboarding.account_name"); identity.Children.Add(label);
        L.Bind(name, System.Windows.Automation.AutomationProperties.NameProperty, "onboarding.account_name"); identity.Children.Add(name); content.Children.Add(identity); content.Children.Add(status);
        body.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = body;
        DailyDialogs.Prepare(this);
        next.Click += async (_, _) => { if (Stage == 0) await StartVerificationAsync(); else if (Stage == 2) SaveAccount(); else if (Stage == 3) { allowClose = true; Close(); } };
        skip.Click += (_, _) => Close(); retry.Click += (_, _) => Reset();
        Closing += CloseRequested;
        Closed += (_, _) => { poll.Stop(); verification?.Cancel(); WeakEventManager<DailyLanguage, EventArgs>.RemoveHandler(L, nameof(DailyLanguage.Changed), LanguageChanged); };
        flow.Progress += p => Dispatcher.Invoke(() => { L.Text(message, "onboarding." + p.Stage); if (p.Detail.Length > 0) DailyUiText.Set(status, p.Detail); });
        poll.Tick += async (_, _) => await CheckClosedAsync();
        Reset();
    }

    private void LanguageChanged(object? sender, EventArgs e) => language.SelectedValue = L.Code;
    private void SelectLanguage(object sender, SelectionChangedEventArgs e)
    {
        if (language.SelectedValue is not string code || code == L.Code) return;
        try { L.Select(code); }
        catch (Exception error)
        {
            language.SelectedValue = L.Code;
            L.Bind(status, TextBlock.TextProperty, () => L.Get("language.error", DailyUserText.Error(error, L.Translate)));
            status.SetResourceReference(TextBlock.ForegroundProperty, "Error");
        }
    }

    private void Reset()
    {
        poll.Stop(); Stage = 0; location.Visibility = Visibility.Visible; location.IsEnabled = true; identity.Visibility = Visibility.Collapsed;
        L.Text(progress, "onboarding.step", 1, 3); L.Text(heading, "onboarding.path_title"); L.Text(message, "onboarding.path_body");
        L.Bind(next, ContentControl.ContentProperty, "onboarding.start"); next.IsEnabled = true; retry.Visibility = Visibility.Collapsed; status.Text = "";
    }
    internal async Task StartVerificationAsync()
    {
        if (Stage != 0 || pending is { IsCompleted: false }) return;
        verification?.Dispose(); verification = new(); next.IsEnabled = false; location.IsEnabled = false; status.Text = "";
        try
        {
            await Task.Run(installation.Resolve);
            verification.Token.ThrowIfCancellationRequested();
            Stage = 1; location.Visibility = Visibility.Collapsed;
            L.Text(progress, "onboarding.step", 2, 3); L.Text(heading, "onboarding.login_title"); L.Text(message, "onboarding.launching");
            var task = flow.VerifyAsync(verification.Token); pending = task;
            var proof = await task;
            if (verification.IsCancellationRequested) return;
            Stage = 2; identity.Visibility = Visibility.Visible; name.Text = proof.Name;
            L.Text(heading, "onboarding.detected_title"); L.Text(message, "onboarding.detected", proof.Name);
            L.Bind(next, ContentControl.ContentProperty, "onboarding.save"); retry.Visibility = Visibility.Visible;
            L.Text(status, "onboarding.wait_close"); status.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
            poll.Start(); await CheckClosedAsync();
            if (ShowActivated) { if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
        }
        catch (OperationCanceledException) when (verification.IsCancellationRequested) { }
        catch (Exception error)
        {
            try { DailyJson.Write(Path.Combine(root, "onboarding-error.json"), new { atUtc = DateTimeOffset.UtcNow, stage = Stage, error = error.ToString() }); } catch { }
            Reset(); DailyUiText.Error(status, error, ""); status.SetResourceReference(TextBlock.ForegroundProperty, "Error");
        }
        finally { pending = null; }
    }
    internal async Task CheckClosedAsync()
    {
        if (Stage != 2 || polling) return;
        polling = true;
        try
        {
            bool ready = await Task.Run(flow.ReadyToSave);
            if (Stage != 2 || allowClose) return;
            next.IsEnabled = ready;
            L.Text(status, ready ? "onboarding.can_save" : "onboarding.wait_close");
        }
        catch (Exception error) { next.IsEnabled = false; DailyUiText.Error(status, error, ""); }
        finally { polling = false; }
    }
    internal void SaveAccount()
    {
        try
        {
            flow.Save(name.Text);
            DailyJson.Write(Path.Combine(root, "first-run.json"), new FirstRunRecord(State: "tour"));
            Saved = true; Stage = 3; poll.Stop(); identity.Visibility = Visibility.Collapsed; retry.Visibility = Visibility.Collapsed;
            L.Text(progress, "onboarding.step", 3, 3); L.Text(heading, "onboarding.saved_title"); L.Text(message, "onboarding.saved_body", name.Text.Trim());
            status.Text = ""; L.Bind(next, ContentControl.ContentProperty, "onboarding.tour"); next.IsEnabled = true;
        }
        catch (Exception error) { DailyUiText.Error(status, error, ""); }
    }
    private void CloseRequested(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true; if (closing) return; closing = true;
        // Leave WPF Closing before opening a confirmation or calling Close again.
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () => await ConfirmSkipAndCloseAsync()));
    }
    private async Task ConfirmSkipAndCloseAsync()
    {
        try
        {
            bool confirmed = ConfirmSkipForSmoke?.Invoke() ?? DailyDialogs.ShowModal(DailyDialogs.Message(this, L.Get("onboarding.skip_title"), L.Get(Saved ? "onboarding.skip_tour" : "onboarding.skip_warning"), true)) == true;
            if (!confirmed) return;
            verification?.Cancel(); if (pending != null) { try { await pending; } catch { } }
            DailyJson.Write(Path.Combine(root, "first-run.json"), new FirstRunRecord(State: "skipped"));
            allowClose = true; Close();
        }
        catch (Exception error)
        {
            allowClose = false; DailyUiText.Error(status, error, "");
            try { DailyJson.Write(Path.Combine(root, "onboarding-error.json"), new { atUtc = DateTimeOffset.UtcNow, stage = Stage, operation = "skip", error = error.ToString() }); } catch { }
        }
        finally { closing = false; }
    }
}
