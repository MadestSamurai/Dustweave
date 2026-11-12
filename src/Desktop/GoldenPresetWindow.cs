using System.Windows;
using System.Windows.Controls;
using Dustweave.Golden;
namespace Dustweave.Desktop;

public sealed class GoldenPresetWindow : Window
{
    static DailyLanguage L => DailyLanguage.Current;
    readonly GoldenPresetSession session;
    readonly Func<bool> canOperate;
    readonly bool allowGame;
    readonly TextBox code = new() { AcceptsReturn = false, TextWrapping = TextWrapping.NoWrap, Height = 40, MaxLength = FormationCode.MaxLength, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden };
    readonly ComboBox slots = new() { MinHeight = 36, MinWidth = 190, DisplayMemberPath = nameof(GoldenSlot.Label) };
    readonly Button read = Button("goldencode.read"), export = Button("goldencode.generate"), copy = Button("goldencode.copy"), import = Button("goldencode.import");
    readonly TextBlock summary = Text(), status = Text(), lineup = Text(), savedLineup = Text();
    readonly ProgressBar progress = new() { Height = 5, IsIndeterminate = true, Visibility = Visibility.Collapsed, Margin = new(0, 12, 0, 0) };
    readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    GoldenSnapshot? snapshot;
    bool busy, closing, initialReadPending = true;
    public event Action? ActivityChanged;
    internal bool IsWorking => busy;
    static TextBlock Text() { var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) }; t.SetResourceReference(TextBlock.ForegroundProperty, "Ink"); return t; }
    static Button Button(string key) { var b = new Button { MinHeight = 36, Padding = new(14, 5, 14, 5) }; L.Bind(b, ContentControl.ContentProperty, key); return b; }
    public GoldenPresetWindow(string root, string account, bool allowGame = true, Func<bool>? canOperate = null)
    {
        this.allowGame = allowGame; this.canOperate = canOperate ?? (() => true); session = new(root, account);
        Width = 720; Height = 700; MinWidth = 560; MinHeight = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        L.Bind(this, TitleProperty, "tool.golden-presets");
        var layout = new DockPanel { Margin = new(24, 0, 24, 24) };
        var bottom = new DockPanel { LastChildFill = false, Margin = new(0, 12, 0, 0) };
        bottom.Children.Add(read); DockPanel.SetDock(import, Dock.Right); bottom.Children.Add(import); import.SetResourceReference(StyleProperty, "PrimaryButton");
        DockPanel.SetDock(bottom, Dock.Bottom); layout.Children.Add(bottom);
        DockPanel.SetDock(progress, Dock.Bottom); layout.Children.Add(progress); DockPanel.SetDock(status, Dock.Bottom); layout.Children.Add(status);
        var content = new StackPanel();
        var intro = Text(); L.Text(intro, "goldencode.intro"); intro.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); content.Children.Add(intro);
        var codeLabel = Text(); L.Text(codeLabel, "goldencode.code"); content.Children.Add(codeLabel);
        var codeLine = new DockPanel(); copy.Margin = new(12, 0, 0, 0); DockPanel.SetDock(copy, Dock.Right); codeLine.Children.Add(copy); codeLine.Children.Add(code); content.Children.Add(codeLine);
        L.Bind(code, System.Windows.Automation.AutomationProperties.NameProperty, "goldencode.code");
        content.Children.Add(summary); content.Children.Add(lineup);
        var divider = new Border { Height = 1, Margin = new(0, 14, 0, 14) }; divider.SetResourceReference(Border.BackgroundProperty, "Line"); content.Children.Add(divider);
        var slotLabel = Text(); L.Text(slotLabel, "goldencode.slot"); content.Children.Add(slotLabel);
        var slotLine = new DockPanel(); export.Margin = new(12, 0, 0, 0); DockPanel.SetDock(export, Dock.Right); slotLine.Children.Add(export); slotLine.Children.Add(slots); content.Children.Add(slotLine);
        savedLineup.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); content.Children.Add(savedLineup);
        L.Bind(slots, System.Windows.Automation.AutomationProperties.NameProperty, "goldencode.slot");
        layout.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = layout;
        session.Report = key => Dispatcher.Invoke(() => L.Text(status, key));
        code.TextChanged += (_, _) => Preview(); slots.SelectionChanged += (_, _) => Preview();
        read.Click += async (_, _) => await ReadPresets();
        ContentRendered += (_, _) => TryReadOnOpen();
        export.Click += (_, _) => GenerateCode();
        copy.Click += (_, _) => { try { Clipboard.SetText(FormationCode.Encode(FormationCode.Decode(code.Text))); L.Text(status, "goldencode.copied"); } catch { L.Text(status, "goldencode.copy_manually"); } };
        import.Click += async (_, _) => await Work(async () => {
            string input = code.Text.Trim(); var formation = FormationCode.Decode(input); int selected = (slots.SelectedItem as GoldenSlot)?.Slot ?? -1;
            SetSnapshot(await Task.Run(() => session.Read(input)), selected);
            FormationImport.Validate(formation, snapshot!.Rules, snapshot.Owned, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var target = slots.SelectedItem as GoldenSlot ?? throw new InvalidOperationException("goldencode.choose_slot");
            if (DailyDialogs.ShowModal(DailyDialogs.Message(this, L.Get("goldencode.import"), L.Get("goldencode.confirm", target.Label), true, safeDefault:true)) != true) { L.Text(status, "goldencode.cancelled"); return; }
            var result = await Task.Run(() => session.Save(input, target.Slot, snapshot)); SetSnapshot(result, target.Slot); L.Text(status, result.ViewSynchronized ? "goldencode.saved" : "goldencode.saved_refresh");
        });
        Closing += (_, e) => { if (busy) { closing = true; session.Stop(); e.Cancel = true; } };
        Closed += (_, _) => { closed.TrySetResult(); L.Changed -= LanguageChanged; };
        L.Changed += LanguageChanged; Preview(); L.Text(status, "goldencode.open_lobby");
    }
    Task ReadPresets() => Work(async () => { initialReadPending = false; int selected = (slots.SelectedItem as GoldenSlot)?.Slot ?? -1; SetSnapshot(await Task.Run(() => session.Read()), selected); L.Text(status, "goldencode.ready"); });
    void TryReadOnOpen() { if (initialReadPending && IsLoaded && allowGame && !closing && !closed.Task.IsCompleted && canOperate()) _ = ReadPresets(); }
    void SetSnapshot(GoldenSnapshot value, int slot = -1) { snapshot = value; slots.ItemsSource = value.Slots; slots.SelectedItem = value.Slots.FirstOrDefault(s => s.Slot == slot) ?? value.Slots.FirstOrDefault(); Preview(); }
    void LanguageChanged(object? sender, EventArgs e) { slots.Items.Refresh(); Preview(); }
    void GenerateCode() { if (slots.SelectedItem is GoldenSlot slot) { if (slot.Error.Length > 0) { L.Text(status, slot.Error); return; } code.Text = slot.Code; L.Text(status, "goldencode.generated"); } }
    string Describe(Formation formation) => string.Join("\n", formation.Units.Select((u, i) => L.Get("goldencode.unit", i + 1,
        snapshot?.Names.GetValueOrDefault(u.Costume)?.In(L.Code) is { Length: > 0 } title ? title : "#" + u.Costume, u.Position + 1)))
        + "\n" + L.Get("goldencode.blessings", string.Join(", ", formation.Attack), string.Join(", ", formation.Defense));
    void Preview()
    {
        lineup.Text = ""; summary.Text = ""; savedLineup.Text = "";
        try { if (code.Text.Trim().Length > 0) { var f = FormationCode.Decode(code.Text); L.Text(summary, "goldencode.summary", f.Season, f.Units.Length, f.Width, f.Height); lineup.Text = Describe(f); } }
        catch (Exception e) { summary.Text = L.Get(e.Message); }
        if (slots.SelectedItem is GoldenSlot slot)
        {
            try { savedLineup.Text = slot.Error.Length > 0 ? L.Get(slot.Error) : slot.Code.Length == 0 ? L.Get("goldencode.empty_slot") : Describe(FormationCode.Decode(slot.Code)); }
            catch { L.Text(savedLineup, "goldencode.export_unavailable"); }
        }
        Availability();
    }
    internal void RefreshAvailability() { Availability(); TryReadOnOpen(); }
    void Availability() { read.IsEnabled = !busy && allowGame && canOperate(); import.IsEnabled = read.IsEnabled && code.Text.Trim().Length > 0; export.IsEnabled = !busy && slots.SelectedItem is GoldenSlot { Code.Length: > 0 }; copy.IsEnabled = !busy && lineup.Text.Length > 0; slots.IsEnabled = !busy; code.IsReadOnly = busy; progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed; }
    async Task Work(Func<Task> action)
    {
        if (busy || !allowGame || !canOperate()) return; busy = true; Availability(); ActivityChanged?.Invoke();
        try { await action(); }
        catch (OperationCanceledException) { L.Text(status, "goldencode.cancelled"); }
        catch (Exception e) { status.Text = e.Message.StartsWith("goldencode.", StringComparison.Ordinal) ? L.Get(e.Message) : DailyUserText.Error(e, L.Translate); }
        finally { busy = false; Availability(); ActivityChanged?.Invoke(); if (closing) Close(); }
    }
    internal void BringForward() { if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
    internal async Task<bool> CloseSafelyAsync() { if (closed.Task.IsCompleted) return true; Close(); try { await closed.Task.WaitAsync(TimeSpan.FromSeconds(15)); return true; } catch (TimeoutException) { return false; } }
    internal void PreviewForSmoke(string value) { if (allowGame) throw new InvalidOperationException(); code.Text = value; if (read.IsEnabled || import.IsEnabled) throw new InvalidOperationException("Synthetic golden preview can touch the game"); }
    internal int VerifyPreviewForSmoke(string value)
    {
        if (allowGame) throw new InvalidOperationException();
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        var f = FormationCode.Decode(value);
        var names = f.Units.Select((u, i) => (u.Costume, Name: new DailyDisplayName("角色" + i + " · 服装", "角色" + i + " · 服裝", "Character " + i + " · Costume"))).ToDictionary(x => x.Costume, x => x.Name);
        names[999999] = new("新服装", "新服裝", "Z");
        var first = new GoldenSnapshot(1, new string('a', 64), "ready", new(), [], [new(0, "", "", "", ""), new(1, "Saved", value, "", "")], names) { ViewSynchronized = true };
        code.Text = ""; SetSnapshot(first);
        code.Text = value;
        Check(lineup.Text.Contains(names[f.Units[0].Costume].In(L.Code)) && !lineup.Text.Contains("#" + f.Units[0].Costume), "Pasted costumes outside saved slots must resolve from the full catalog");
        slots.SelectedIndex = 1;
        Check(code.Text == value && savedLineup.Text == lineup.Text, "Selecting a slot shows its content without replacing the pasted code");
        var other = FormationCode.Encode(new Formation { Season = 50, Width = 5, Height = 5, Units = [new() { Costume = 999999, Position = 0 }], Attack = [], Defense = [] });
        code.Text = other;
        Check(lineup.Text.Contains(names[999999].In(L.Code)) && !lineup.Text.Contains("#999999"), "Unowned and one-character names stay visible");
        SetSnapshot(first with { Revision = 2, Status = "saved", Slots = [first.Slots[0], new(1, "Saved", other, "", "")] }, 1);
        Check(savedLineup.Text == lineup.Text && code.Text == other && slots.SelectedIndex == 1, "Readback immediately refreshes the selected slot and keeps the input");
        code.Text = "invalid";
        Check(lineup.Text.Length == 0 && !copy.IsEnabled && savedLineup.Text.Length > 0, "Invalid code cannot leave a misleading old preview or hide the saved slot");
        GenerateCode();
        Check(code.Text == other && copy.IsEnabled && status.Text == L.Get("goldencode.generated"), "Generating a code is separate from copying it");
        SetSnapshot(first, 1); code.Text = value;
        return checks;
    }
}
