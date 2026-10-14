using System.Windows;
using System.Windows.Controls;
using Dustweave.Accounts;

namespace Dustweave.Desktop;

internal sealed class SandboxInstallationPanel : Border
{
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly SandboxInstallation store;
    private readonly bool readOnly;
    private readonly Button edit = new();
    private readonly Button help = new() { Margin = new(0, 0, 8, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock summary = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 12, 0) };
    private SandboxSetupWindow? editor;
    private bool working;
    internal bool Preparing { get; private set; }
    internal event Action? PreparingChanged;
    internal string DisplayedPath { get; private set; } = "";
    internal bool CanChoose => IsEnabled && !working && !readOnly;
    internal bool HelpVisible => help.Visibility == Visibility.Visible;

    internal SandboxInstallationPanel(SandboxInstallation store, bool readOnly)
    {
        this.store = store; this.readOnly = readOnly;
        Padding = new(0, 0, 0, 12); Margin = new(0, 0, 0, 12);
        BorderThickness = new(0, 0, 0, 1); SetResourceReference(BorderBrushProperty, "Line");
        var header = new DockPanel();
        L.Bind(edit, ContentControl.ContentProperty, () => L.Translate("更改位置"));
        edit.Click += (_, _) => OpenEditor(); DockPanel.SetDock(edit, Dock.Right); header.Children.Add(edit);
        L.Bind(help, ContentControl.ContentProperty, "sandbox_setup.help");
        help.Click += (_, _) => OpenEditor(); DockPanel.SetDock(help, Dock.Right); header.Children.Add(help);
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        L.Text(title, "sandbox_path.title"); DockPanel.SetDock(title, Dock.Left); header.Children.Add(title);
        summary.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); header.Children.Add(summary); Child = header;
        Loaded += async (_, _) => await RefreshAsync();
        IsEnabledChanged += (_, _) => editor?.SetCanEdit(IsEnabled || Preparing);
    }

    internal SandboxSetupWindow CreateEditor() => new(Window.GetWindow(this), store, SandboxSetupPurpose.Settings, readOnly);
    internal void OpenEditor(Window? owner = null)
    {
        if (working || !IsEnabled) return;
        if (editor != null) { editor.Activate(); return; }
        editor = new(owner ?? Window.GetWindow(this), store, SandboxSetupPurpose.Settings, readOnly);
        WatchEditor(editor); DailyDialogs.ShowModal(editor);
    }
    private void WatchEditor(SandboxSetupWindow window)
    {
        window.Closed += async (_, _) => {
            if (ReferenceEquals(editor, window)) editor = null;
            await RefreshAsync();
        };
    }

    // Run once before creating a parallel queue, never in per-account workers or
    // passive discovery. An unattended attempt shows help without awaiting input;
    // the scheduler records that occurrence as failed before it can run again.
    internal async Task<bool> EnsureReadyAsync(bool scheduled = false)
    {
        if (Preparing) { editor?.Activate(); return false; }
        Preparing = true; PreparingChanged?.Invoke();
        try
        {
            var value = await Task.Run(store.Read); Present(value);
            if (value.Error == null) return true;
            if (editor == null)
            {
                editor = new(Window.GetWindow(this), store, scheduled ? SandboxSetupPurpose.Schedule : SandboxSetupPurpose.Launch, readOnly);
                WatchEditor(editor);
                if (scheduled) { editor.Show(); return false; }
                var window = editor;
                DailyDialogs.ShowModal(window);
                return window.Ready && (await Task.Run(store.Read)).Error == null;
            }
            var existing = editor; existing.Activate();
            if (scheduled) return false;
            return await existing.Completion && (await Task.Run(store.Read)).Error == null;
        }
        finally { Preparing = false; PreparingChanged?.Invoke(); editor?.SetCanEdit(IsEnabled); }
    }
    internal async Task RefreshAsync() => Present(await Task.Run(store.Read));
    internal async Task SelectAsync(string value)
    {
        if (!CanChoose) return;
        working = true;
        Exception? failure = null;
        try { await Task.Run(() => store.Select(value)); }
        catch (Exception error) { failure = error; }
        finally { working = false; await RefreshAsync(); }
        if (failure != null) { L.Bind(summary, TextBlock.TextProperty, () => DailyUserText.Error(failure, L.Translate)); summary.SetResourceReference(TextBlock.ForegroundProperty, "Error"); }
    }
    private void Present(SandboxInstallationState value)
    {
        DisplayedPath = value.Directory;
        L.Bind(summary, TextBlock.TextProperty, () => value.Error != null ? L.Describe(value.Error) : value.Directory);
        L.Bind(summary, ToolTipProperty, () => value.Error == null ? value.Directory : L.Describe(value.Error));
        summary.SetResourceReference(TextBlock.ForegroundProperty, value.Error != null ? "Error" : "MutedInk");
        help.Visibility = value.Error != null && !readOnly ? Visibility.Visible : Visibility.Collapsed;
    }
}
