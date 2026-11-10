using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
namespace Dustweave.Desktop;

// Storage overview: one bar answers "what can I clean", then disk → location → content.
// Cleaning, moving storage and removing a sandbox stay three separate actions.
public partial class MainWindow
{
    private enum StorageTone { Clean, Regenerable, Temporary, Keep, Unknown }
    private static string StorageSize(long bytes)
    {
        const double MiB = 1048576d, GiB = 1073741824d;
        if (bytes >= GiB) return (bytes / GiB).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        double mb = bytes / MiB;
        return (mb >= 10 || mb == 0 ? Math.Round(mb).ToString("0", CultureInfo.InvariantCulture) : mb.ToString("0.0", CultureInfo.InvariantCulture)) + " MB";
    }
    private static StorageTone StorageToneOf(DailyStorageState state) => state switch
    {
        DailyStorageState.Regenerable => StorageTone.Regenerable,
        DailyStorageState.Account or DailyStorageState.Kept => StorageTone.Keep,
        _ => StorageTone.Temporary,
    };
    private static string StorageStateKey(DailyStorageState state) => state switch
    {
        DailyStorageState.Recent => "storage.state.recent",
        DailyStorageState.Regenerable => "storage.state.regenerable",
        DailyStorageState.InUse => "storage.state.in_use",
        DailyStorageState.Recovery => "storage.state.recovery",
        DailyStorageState.Account => "storage.state.account",
        _ => "storage.state.kept",
    };
    // Bytes per bar group: what a cleanup removes now, what regenerates, what is kept for now, what is always kept.
    private static long[] StorageGroups(IEnumerable<DailyStorageCategory> categories)
    {
        var groups = new long[4];
        foreach (var c in categories)
        {
            groups[0] += c.ReclaimableBytes; groups[2] += c.DeferredBytes;
            long rest = Math.Max(0, c.Bytes - c.ReclaimableBytes - c.DeferredBytes);
            groups[StorageToneOf(c.Retained) switch { StorageTone.Regenerable => 1, StorageTone.Keep => 3, _ => 2 }] += rest;
        }
        return groups;
    }
    private static readonly string[] StorageGroupBrushes = ["Success", "Focus", "Warning", "LineStrong"];
    private static readonly string[] StorageGroupKeys = ["storage.group.clean", "storage.group.regenerable", "storage.group.temporary", "storage.group.keep"];

    private static FrameworkElement StorageBar(long[] groups, double height)
    {
        var bar = new Grid { Height = height };
        long total = groups.Sum();
        for (int i = 0; i < groups.Length; i++)
        {
            if (groups[i] <= 0) continue;
            // Keep a small share visible; the legend carries the exact number.
            bar.ColumnDefinitions.Add(new() { Width = new GridLength(Math.Max(groups[i], total * 0.008), GridUnitType.Star) });
            var part = new Border { Margin = new(0, 0, 2, 0) };
            part.SetResourceReference(Border.BackgroundProperty, StorageGroupBrushes[i]);
            Grid.SetColumn(part, bar.ColumnDefinitions.Count - 1); bar.Children.Add(part);
        }
        if (bar.Children.Count == 0) { var empty = new Border(); empty.SetResourceReference(Border.BackgroundProperty, "Line"); bar.Children.Add(empty); }
        else ((Border)bar.Children[bar.Children.Count - 1]).Margin = new(0);
        bar.SizeChanged += (_, e) => bar.Clip = new RectangleGeometry(new Rect(e.NewSize), height / 2, height / 2);
        return bar;
    }
    private static Border StorageChip(StorageTone tone, string key, params object?[] args)
    {
        var text = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        L.Text(text, key, args);
        var chip = new Border { CornerRadius = new(10), Padding = new(8, 2, 8, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Child = text };
        var (background, foreground) = tone switch
        {
            StorageTone.Clean => ("PrimarySoft", "Success"),
            StorageTone.Regenerable => ("SurfaceMuted", "Focus"),
            StorageTone.Temporary => ("WarningSoft", "Warning"),
            StorageTone.Unknown => ("ErrorSoft", "Error"),
            _ => ("SurfaceMuted", "MutedInk"),
        };
        chip.SetResourceReference(Border.BackgroundProperty, background);
        text.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        return chip;
    }
    // Name | size | available now | state. Fixed right columns line up across nesting levels.
    private static Grid StorageColumns(UIElement name, string size, UIElement available, UIElement chip)
    {
        var grid = new Grid();
        foreach (double width in new[] { -1d, 92, 128, 176 })
            grid.ColumnDefinitions.Add(new() { Width = width < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
        grid.Children.Add(name);
        var bytes = new TextBlock { Text = size, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0) };
        Grid.SetColumn(bytes, 1); grid.Children.Add(bytes);
        Grid.SetColumn(available, 2); grid.Children.Add(available);
        Grid.SetColumn(chip, 3); grid.Children.Add(chip);
        if (chip is FrameworkElement element) element.Margin = new(16, 0, 0, 0);
        return grid;
    }
    private static TextBlock StorageAvailable(long reclaimable, long deferred)
    {
        var text = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right };
        if (reclaimable > 0) { L.Text(text, "storage.available", StorageSize(reclaimable)); text.SetResourceReference(TextBlock.ForegroundProperty, "Success"); }
        else if (deferred > 0) { L.Text(text, "storage.available_later", StorageSize(deferred)); text.SetResourceReference(TextBlock.ForegroundProperty, "Warning"); }
        else { text.Text = "—"; text.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); }
        return text;
    }
    private StackPanel StorageRow(Panel parent, UIElement header, string nameKey, params object?[] args)
    {
        var toggle = new ToggleButton { Style = (Style)FindResource("DisclosureRow"), Content = header, Tag = nameKey };
        L.Bind(toggle, AutomationProperties.NameProperty, nameKey, args);
        var details = new StackPanel { Visibility = Visibility.Collapsed, Margin = new(30, 0, 0, 8) };
        toggle.Checked += (_, _) => details.Visibility = Visibility.Visible;
        toggle.Unchecked += (_, _) => details.Visibility = Visibility.Collapsed;
        parent.Children.Add(toggle); parent.Children.Add(details);
        return details;
    }
    private static TextBlock StorageNote(Panel parent, string key, params object?[] args) => StorageNote(parent, () => L.Get(key, args));
    private static TextBlock StorageNote(Panel parent, Func<string> value)
    {
        var text = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 2, 0, 4) };
        L.Bind(text, TextBlock.TextProperty, value); text.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        parent.Children.Add(text); return text;
    }
    private static TextBlock StorageName(string key, params object?[] args)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        L.Text(text, key, args); return text;
    }
    private static Border StorageCategoryChip(DailyStorageCategory c) =>
        c.ReclaimableBytes > 0 ? StorageChip(StorageTone.Clean, c.ReclaimableBytes >= c.Bytes ? "storage.state.clean" : "storage.state.partial")
        : c.DeferredBytes > 0 ? StorageChip(StorageTone.Temporary, "storage.state.locked")
        : StorageChip(StorageToneOf(c.Retained), StorageStateKey(c.Retained));
    private void StorageCategory(Panel parent, DailyStorageCategory c)
    {
        string key = "storage.cat." + c.Name;
        var details = StorageRow(parent, StorageColumns(StorageName(key), StorageSize(c.Bytes), StorageAvailable(c.ReclaimableBytes, c.DeferredBytes), StorageCategoryChip(c)), key);
        StorageNote(details, () => L.Get("storage.detail.effect", L.Get(key + ".effect")));
        if (c.Bytes > c.ReclaimableBytes) StorageNote(details, () => L.Get("storage.detail.reason", L.Get(key + ".reason")));
        if (c.DeferredBytes > 0) StorageNote(details, "storage.detail.deferred", StorageSize(c.DeferredBytes));
    }
    private void StorageArea(Panel parent, DailyStorageArea area, Window owner, List<Button> manage, Func<bool> ready, Func<Task> rescan)
    {
        var account = area.Account is { } key ? rows.FirstOrDefault(r => r.Account.AccountKey == key)?.Account : null;
        string folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(area.Path));
        (string Key, object?[] Args) title = area.Kind switch
        {
            "main" => ("storage.area.main", Array.Empty<object?>()),
            "cache" => (area.Previous ? "storage.area.previous" : "storage.area.cache", Array.Empty<object?>()),
            "sandbox" when area.Unknown => ("storage.area.sandbox_store", Array.Empty<object?>()),
            "sandbox" when account != null => (area.Previous ? "storage.area.sandbox_previous" : "storage.area.sandbox", new object?[] { account.Name }),
            "sandbox" => ("storage.area.sandbox_unregistered", Array.Empty<object?>()),
            "extraction" => ("storage.area.extraction", new object?[] { folder }),
            _ => ("storage.area.crashes", Array.Empty<object?>()),
        };
        var name = StorageName(title.Key, title.Args); name.FontWeight = FontWeights.SemiBold;
        Border chip = area.Unknown ? StorageChip(StorageTone.Unknown, "storage.state.unknown")
            : area.Locked ? StorageChip(StorageTone.Temporary, "storage.state.locked")
            : area.ReclaimableBytes > 0 ? StorageChip(StorageTone.Clean, area.ReclaimableBytes >= area.Bytes ? "storage.state.clean" : "storage.state.partial")
            : StorageChip(StorageTone.Keep, "storage.state.none");
        // Unknown is never shown as zero.
        string size = area.Unknown ? L.Get("storage.size_unknown") : StorageSize(area.Bytes);
        var details = StorageRow(parent, StorageColumns(name, size, StorageAvailable(area.ReclaimableBytes, area.DeferredBytes), chip), title.Key, title.Args);
        StorageNote(details, "storage.area.path", area.Path);
        if (area.Kind == "sandbox" && account != null) StorageNote(details, "storage.area.folder", folder);
        if (area.Previous) StorageNote(details, area.Kind == "sandbox" ? "storage.area.sandbox_previous_hint" : "storage.area.previous_hint");
        if (area.Unknown) StorageNote(details, "storage.area.unknown", area.Skipped).SetResourceReference(TextBlock.ForegroundProperty, "Error");
        else if (area.Skipped > 0) StorageNote(details, "storage.area.skipped", area.Skipped).SetResourceReference(TextBlock.ForegroundProperty, "Warning");
        if (area.Locked) StorageNote(details, area.Kind == "extraction" ? "storage.area.locked_extraction" : "storage.area.locked_sandbox").SetResourceReference(TextBlock.ForegroundProperty, "Warning");
        var list = new StackPanel { Margin = new(0, 4, 0, 0) };
        details.Children.Add(list);
        foreach (var c in area.Categories.Where(c => c.Group.Length == 0)) StorageCategory(list, c);
        var own = area.Categories.Where(c => c.Group == "dustweave").ToArray();
        if (own.Length > 0)
        {
            // Sandbox-private Dustweave data is one line until expanded; no file is counted twice.
            long bytes = own.Sum(c => c.Bytes), now = own.Sum(c => c.ReclaimableBytes), later = own.Sum(c => c.DeferredBytes);
            var largest = own.OrderByDescending(c => c.Bytes).First();
            Border groupChip = now > 0 ? StorageChip(StorageTone.Clean, "storage.state.partial")
                : later > 0 ? StorageChip(StorageTone.Temporary, "storage.state.locked")
                : StorageChip(StorageToneOf(largest.Retained), StorageStateKey(largest.Retained));
            // Place the group by its total, among the other categories (each row adds a header and its details).
            var holder = new StackPanel();
            list.Children.Insert(Math.Min(list.Children.Count, area.Categories.Count(c => c.Group.Length == 0 && c.Bytes >= bytes) * 2), holder);
            var inner = StorageRow(holder, StorageColumns(StorageName("storage.cat.dustweave"), StorageSize(bytes), StorageAvailable(now, later), groupChip), "storage.cat.dustweave");
            StorageNote(inner, "storage.cat.dustweave.hint");
            foreach (var c in own) StorageCategory(inner, c);
        }
        if (area.Kind == "sandbox" && account != null)
        {
            // Removing a sandbox is a separate, confirmed operation in sandbox management.
            var action = new Button { Style = (Style)FindResource("QuietButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 8, 0, 4) };
            L.Bind(action, ContentControl.ContentProperty, "storage.area.manage");
            details.Children.Add(action);
            StorageNote(details, "storage.area.manage_hint");
            action.IsEnabled = ready() && account.Valid && smoke == null && !IsSandboxWindow;
            if (smoke == null && !IsSandboxWindow && account.Valid) manage.Add(action);
            action.Click += async (_, _) =>
            {
                if (!ready() || Unavailable) return;
                var window = new SandboxManagementWindow(owner, account);
                DailyDialogs.ShowModal(window);
                if (window.OpenSoftwareSettings) { owner.Close(); WorkspaceTabs.SelectedItem = SoftwareSettingsTab; return; }
                await rescan();
            };
        }
    }

    private Window CreateLogCleanupWindow(bool moveWhenReady = false, DailyStorageOverview? preview = null)
    {
        if (preview != null && smoke == null) throw new InvalidOperationException("Storage fixtures require an isolated smoke window.");
        var dialog = new Window { Owner = this, Width = 800, MinWidth = 680, MaxHeight = Math.Max(420, SystemParameters.WorkArea.Height - 60), SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        L.Bind(dialog, Window.TitleProperty, "logs.title");
        var layout = new Grid(); layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var panel = new StackPanel { Margin = new(24, 8, 24, 16) };
        TextBlock Text(Panel host, string key, double top = 12, params object?[] args) { var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, top, 0, 0) }; L.Text(text, key, args); host.Children.Add(text); return text; }
        Border Card(Panel host, double top = 16) { var card = new Border { CornerRadius = new(10), BorderThickness = new(1), Padding = new(18, 16, 18, 14), Margin = new(0, top, 0, 0) }; card.SetResourceReference(Border.BackgroundProperty, "Surface"); card.SetResourceReference(Border.BorderBrushProperty, "Line"); host.Children.Add(card); return card; }
        TextBlock Section(Panel host, string key) { var title = Text(host, key, 24); title.FontWeight = FontWeights.SemiBold; title.FontSize = 15; return title; }

        // 1. One bar: everything Dustweave occupies, split by what can be cleaned now.
        var summary = new StackPanel(); Card(panel, 4).Child = summary;
        var status = Text(summary, "logs.scanning", 0); status.Name = "LogCleanupStatus"; status.FontSize = 22; status.FontWeight = FontWeights.SemiBold;
        var headline = Text(summary, "storage.summary_wait", 4); headline.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        var progress = new ProgressBar { Name = "StorageOperationProgress", IsIndeterminate = SystemParameters.ClientAreaAnimation, Value = 50, Height = 4, Margin = new(0, 14, 0, 0) }; summary.Children.Add(progress);
        var activity = new ProgressBar { IsIndeterminate = true, Height = 2, Opacity = .5, Margin = new(0, 3, 0, 0), Visibility = Visibility.Collapsed }; summary.Children.Add(activity);
        var moveDetail = Text(summary, "storage.move.preparing", 6); moveDetail.Name = "StorageMoveDetail"; moveDetail.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); moveDetail.Visibility = Visibility.Collapsed;
        var barHost = new ContentControl { Margin = new(0, 14, 0, 0), Focusable = false, Visibility = Visibility.Collapsed }; summary.Children.Add(barHost);
        var legend = new WrapPanel { Margin = new(0, 10, 0, 0) }; summary.Children.Add(legend);
        var incomplete = Text(summary, "storage.incomplete", 8, 0); incomplete.FontSize = 12; incomplete.SetResourceReference(TextBlock.ForegroundProperty, "Warning"); incomplete.Visibility = Visibility.Collapsed;
        var optimization = Text(summary, "storage.compression", 6, 0); optimization.FontSize = 12; optimization.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); optimization.Visibility = Visibility.Collapsed;
        var result = Text(summary, "storage.rescan", 10); result.SetResourceReference(TextBlock.ForegroundProperty, "Success"); result.Visibility = Visibility.Collapsed;

        // 2. Disk → location → content.
        Section(panel, "storage.by_disk");
        var columns = StorageColumns(StorageName("storage.column.name"), L.Get("storage.column.size"), StorageName("storage.column.available"), StorageName("storage.column.state"));
        columns.Margin = new(43, 8, 19, 2);
        foreach (var label in columns.Children.OfType<TextBlock>()) { label.FontSize = 12; label.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); }
        L.Bind((TextBlock)columns.Children[1], TextBlock.TextProperty, "storage.column.size");
        ((TextBlock)columns.Children[2]).HorizontalAlignment = HorizontalAlignment.Right;
        panel.Children.Add(columns);
        var disks = new StackPanel { Name = "StorageDiskUsage" }; panel.Children.Add(disks);

        Section(panel, "storage.resources.title");
        Text(panel, "storage.resources.help", 8).SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        var resourceState = Text(panel, "storage.resources.unchecked", 8); resourceState.Name = "ResourceCopyStatus";
        var resourceActions = new WrapPanel { Margin = new(0, 10, 0, 0) }; panel.Children.Add(resourceActions);
        var resourceCheck = new Button { Name = "ResourceCopyCheck", Margin = new(0, 0, 10, 4) }; L.Bind(resourceCheck, ContentControl.ContentProperty, "storage.resources.check"); resourceActions.Children.Add(resourceCheck);
        var resourceOptimize = new Button { Name = "ResourceCopyOptimize", IsEnabled = false, Margin = new(0, 0, 0, 4) }; L.Bind(resourceOptimize, ContentControl.ContentProperty, "storage.resources.optimize"); resourceActions.Children.Add(resourceOptimize);
        DailyResourcePlan? resourcePlan = null;

        // 3. Cleanup rules, collapsed: the defaults are safe and rarely changed.
        var rules = new Expander { Style = (Style)FindResource("DisclosureExpander"), Margin = new(0, 18, 0, 0) };
        L.Bind(rules, HeaderedContentControl.HeaderProperty, "storage.rules"); panel.Children.Add(rules);
        var rulesPanel = new StackPanel { Margin = new(24, 4, 0, 0) }; rules.Content = rulesPanel;
        Text(rulesPanel, "logs.body", 0).SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        var automatic = new CheckBox { Name = "AutomaticLogCleanup", IsChecked = logPolicy.Automatic, Margin = new(0, 14, 0, 14) }; L.Bind(automatic, ContentControl.ContentProperty, "logs.automatic"); rulesPanel.Children.Add(automatic);
        var settings = new Grid(); settings.ColumnDefinitions.Add(new()); settings.ColumnDefinitions.Add(new());
        ComboBox Choice(string label, string name, int[] options, int value, int column) { var row = new StackPanel { Margin = new(0, 0, column == 0 ? 16 : 0, 0) }; var caption = new TextBlock { Margin = new(0, 0, 0, 8) }; L.Text(caption, label); row.Children.Add(caption); var choice = new ComboBox { Name = name, MinWidth = 150 }; foreach (int n in options) { var item = new ComboBoxItem { Tag = n }; L.Bind(item, ContentControl.ContentProperty, label + ".value", n); choice.Items.Add(item); if (n == value) choice.SelectedItem = item; } if (choice.SelectedIndex < 0) choice.SelectedIndex = 1; row.Children.Add(choice); Grid.SetColumn(row, column); settings.Children.Add(row); return choice; }
        var days = Choice("logs.days", "LogRetentionDays", [2, 3, 7], logPolicy.Days, 0);
        var size = Choice("logs.size", "LogRetentionSize", [128, 512, 1024, 4096], logPolicy.LimitMiB, 1); rulesPanel.Children.Add(settings);
        Text(rulesPanel, "logs.protection", 14).SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");

        // 4. Moving storage is not cleaning: it changes where data lives and deletes nothing.
        Section(panel, "storage.location");
        var locationPanel = new StackPanel(); Card(panel, 8).Child = locationPanel;
        var locationRow = new DockPanel();
        var move = new Button { Name = "CacheStorageMove", Margin = new(10, 0, 0, 0) }; L.Bind(move, ContentControl.ContentProperty, "storage.move"); DockPanel.SetDock(move, Dock.Right); locationRow.Children.Add(move);
        var location = new TextBox { IsReadOnly = true, Text = DailyCacheStorage.Location(root), VerticalContentAlignment = VerticalAlignment.Center }; locationRow.Children.Add(location); locationPanel.Children.Add(locationRow);
        var sandboxLocation = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0), FontSize = 12 }; sandboxLocation.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
        void ShowSandboxLocation() => L.Text(sandboxLocation, "storage.sandbox_location", smoke == null ? DailySandbox.Store : Path.Combine(root, "instances"));
        ShowSandboxLocation(); locationPanel.Children.Add(sandboxLocation);
        Text(locationPanel, "storage.move_scope", 8).SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");

        // Footer stays visible: the scope of cleaning is stated next to the button.
        var footer = new Border { BorderThickness = new(0, 1, 0, 0), Padding = new(24, 14, 24, 18) }; footer.SetResourceReference(Border.BorderBrushProperty, "Line");
        Grid.SetRow(footer, 1);
        var footerGrid = new Grid(); footerGrid.ColumnDefinitions.Add(new()); footerGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.Child = footerGrid;
        var scope = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 16, 0) }; L.Text(scope, "storage.clean_scope"); scope.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); footerGrid.Children.Add(scope);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(actions, 1); footerGrid.Children.Add(actions);
        var scan = new Button { Name = "LogCleanupScan", Margin = new(0, 0, 10, 0) }; L.Bind(scan, ContentControl.ContentProperty, "logs.scan"); actions.Children.Add(scan);
        var clean = new Button { Name = "LogCleanupStart", Style = (Style)FindResource("PrimaryButton"), IsEnabled = false }; L.Bind(clean, ContentControl.ContentProperty, "logs.clean"); actions.Children.Add(clean);

        DailyStorageOverview? storage = null; bool working = false, closed = false; var lifetime = new CancellationTokenSource();
        var manage = new List<Button>();
        static double MiB(long bytes) => Math.Round(bytes / 1048576d, 1);
        DailyStorageMigration.MoveStage? moveStage = null;
        void ResetMoveProgress()
        {
            progress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
            progress.IsIndeterminate = SystemParameters.ClientAreaAnimation; progress.Value = 50;
            activity.Visibility = moveDetail.Visibility = Visibility.Collapsed; moveStage = null;
        }
        void ShowMoveProgress(DailyStorageMigration.MoveProgress value)
        {
            if (closed) return;
            string key = value.Stage switch {
                DailyStorageMigration.MoveStage.Copying => "storage.move.copying",
                DailyStorageMigration.MoveStage.Verifying => "storage.move.verifying",
                DailyStorageMigration.MoveStage.Switching => "storage.move.switching",
                DailyStorageMigration.MoveStage.Finishing => "storage.move.finishing",
                _ => "storage.move.preparing"
            };
            L.Text(status, key);
            bool measured = value.Total > 0 || value.TotalFiles > 0;
            double fraction = value.Total > 0 ? (double)value.Completed / value.Total : value.TotalFiles > 0 ? (double)value.Files / value.TotalFiles : 0;
            double next = Math.Clamp(fraction * 100, 0, 100), previous = progress.Value;
            progress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
            progress.IsIndeterminate = !measured && SystemParameters.ClientAreaAnimation;
            progress.Value = measured ? next : 50;
            if (measured && moveStage == value.Stage && SystemParameters.ClientAreaAnimation)
                progress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(previous, next, TimeSpan.FromMilliseconds(160)) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
            activity.Visibility = measured && SystemParameters.ClientAreaAnimation ? Visibility.Visible : Visibility.Collapsed;
            moveDetail.Visibility = measured ? Visibility.Visible : Visibility.Collapsed;
            if (measured) L.Text(moveDetail, "storage.move.progress", fraction, value.Files, value.TotalFiles);
            moveStage = value.Stage;
        }
        if (smoke != null) progress.Tag = (Action<DailyStorageMigration.MoveProgress>)ShowMoveProgress;
        long Reclaimable() => storage == null ? 0 : storage.Areas.Length > 0 ? storage.Areas.Sum(a => a.ReclaimableBytes) : storage.ReclaimableBytes;
        void Controls(bool enabled)
        {
            scan.IsEnabled = enabled; days.IsEnabled = enabled; size.IsEnabled = enabled; automatic.IsEnabled = enabled; move.IsEnabled = enabled && !IsSandboxWindow;
            clean.IsEnabled = enabled && storage != null && (Reclaimable() > 0 || storage.ReclaimableBytes > 0 || storage.OptimizableFiles > 0);
            resourceCheck.IsEnabled = enabled && !IsSandboxWindow;
            resourceOptimize.IsEnabled = enabled && resourcePlan?.Copies.Length > 0 && !IsSandboxWindow;
            foreach (var button in manage) button.IsEnabled = enabled && !Unavailable;
            progress.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            ResetMoveProgress();
        }
        void Error(Exception error)
        {
            try { DailyJson.Write(Path.Combine(root,"storage-operation-error.json"),new {atUtc=DateTimeOffset.UtcNow,version=DailyProductVersion.Current,error=error.ToString()}); }
            catch(Exception logError)when(logError is IOException or UnauthorizedAccessException){ }
            if (error is DailyStorageBindingException) DailyUiText.Error(status, error);
            else if (error.Message.StartsWith("storage.", StringComparison.Ordinal)) L.Text(status, error.Message);
            else DailyUiText.Error(status, error);
        }
        void Show(DailyStorageOverview report)
        {
            var areas = report.Areas.Length > 0 ? report.Areas
                : report.Scopes.Select(s => new DailyStorageArea(s.Kind, s.Path, s.Skipped, [new DailyStorageCategory("account", DailyStorageState.Kept, s.Bytes, s.ReclaimableBytes, 0, 0)])).ToArray();
            var all = areas.SelectMany(a => a.Categories).ToArray();
            var groups = StorageGroups(all);
            long total = all.Sum(c => c.Bytes), now = all.Sum(c => c.ReclaimableBytes), later = all.Sum(c => c.DeferredBytes);
            if (total == 0 && areas.Any(a => a.Unknown)) L.Text(status, "storage.size_unknown");
            else L.Text(status, "storage.summary_total", StorageSize(total));
            if (now > 0 && later > 0) L.Text(headline, "storage.summary_both", StorageSize(now), StorageSize(later));
            else if (now > 0) L.Text(headline, "storage.summary_clean", StorageSize(now));
            else if (later > 0) L.Text(headline, "storage.summary_later", StorageSize(later));
            else L.Text(headline, "storage.summary_none");
            barHost.Content = StorageBar(groups, 14); barHost.Visibility = Visibility.Visible;
            legend.Children.Clear();
            for (int i = 0; i < groups.Length; i++)
            {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 22, 4) };
                var dot = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Margin = new(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
                dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, StorageGroupBrushes[i]);
                var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center }; L.Text(label, StorageGroupKeys[i], StorageSize(groups[i]));
                item.Children.Add(dot); item.Children.Add(label); L.Bind(item, FrameworkElement.ToolTipProperty, StorageGroupKeys[i] + "_help");
                legend.Children.Add(item);
            }
            int unread = areas.Where(a => a.Unknown).Sum(a => Math.Max(1, a.Skipped)) + areas.Where(a => !a.Unknown).Sum(a => a.Skipped);
            if (unread > 0) { L.Text(incomplete, "storage.incomplete", unread); incomplete.Visibility = Visibility.Visible; } else incomplete.Visibility = Visibility.Collapsed;
            if (report.OptimizableFiles > 0) { L.Text(optimization, "storage.compression", report.OptimizableFiles); optimization.Visibility = Visibility.Visible; } else optimization.Visibility = Visibility.Collapsed;
            disks.Children.Clear(); manage.Clear();
            foreach (var disk in areas.GroupBy(a => a.Disk, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var card = new StackPanel(); Card(disks, 8).Child = card;
                var top = new Grid(); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); top.ColumnDefinitions.Add(new());
                top.Children.Add(new TextBlock { Text = disk.Key, FontWeight = FontWeights.SemiBold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
                var categories = disk.SelectMany(a => a.Categories).ToArray();
                var usage = new TextBlock { TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new(16, 0, 0, 0) };
                long diskBytes = categories.Sum(c => c.Bytes);
                string diskSize = diskBytes == 0 && disk.Any(a => a.Unknown) ? L.Get("storage.size_unknown") : StorageSize(diskBytes);
                L.Text(usage, "storage.disk_usage", diskSize, StorageSize(categories.Sum(c => c.ReclaimableBytes)));
                usage.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); Grid.SetColumn(usage, 1); top.Children.Add(usage); card.Children.Add(top);
                var diskBar = StorageBar(StorageGroups(categories), 8); ((FrameworkElement)diskBar).Margin = new(0, 10, 0, 8); card.Children.Add(diskBar);
                var places = new StackPanel { Margin = new(-6, 0, -6, 0) }; card.Children.Add(places);
                foreach (var area in disk.OrderByDescending(a => a.Kind == "main").ThenByDescending(a => a.Bytes))
                    StorageArea(places, area, dialog, manage, () => !working, Scan);
            }
        }
        async Task Measure() { storage = preview ?? await new DailyStorageMaintenance(root).ScanAsync(logPolicy, lifetime.Token); if (!closed) Show(storage); }
        async Task Scan()
        {
            if (working) return;
            working = true; storage = null; Controls(false); L.Text(status, "logs.scanning");
            try { await Measure(); }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!closed) Error(e); }
            finally { working = false; if (!closed) Controls(true); else lifetime.Dispose(); }
        }
        void Save()
        {
            try
            {
                logPolicy = new(automatic.IsChecked == true, (int)((ComboBoxItem)days.SelectedItem).Tag, (int)((ComboBoxItem)size.SelectedItem).Tag); DailyLogCleanup.Save(root, logPolicy);
                compressionPending = false; nextLogCleanup = DateTime.UtcNow.AddMinutes(1);
                // The cleanable amount depends on the rules; measure again rather than showing a stale number.
                if (!working) _ = Scan(); else { storage = null; clean.IsEnabled = false; L.Text(status, "logs.changed"); }
            }
            catch (Exception e) { DailyUiText.Error(status, e); }
        }
        automatic.Click += (_, _) => Save(); days.SelectionChanged += (_, _) => Save(); size.SelectionChanged += (_, _) => Save();
        bool BusyStorage(bool moving = false) => Unavailable || dailyQueue.IsRunning || updateChecking || updateInstalling || moving && (ToolOpen || (!IsSandboxWindow && smoke == null && DailyUpdateHost.HasIsolatedWindows()));
        scan.Click += async (_, _) => { result.Visibility = Visibility.Collapsed; await Scan(); };
        resourceCheck.Click += async (_, _) =>
        {
            if (working) return;
            if (BusyStorage()) { L.Text(resourceState, "storage.busy"); return; }
            working = true; resourcePlan = null; Controls(false); L.Text(resourceState, "storage.resources.checking", 0, 0);
            try
            {
                resourcePlan = await new DailyStorageMaintenance(root).CheckResourceCopiesAsync(lifetime.Token,
                    new Progress<DailyResourceProgress>(p => { if (!closed && working) L.Text(resourceState, "storage.resources.checking", p.Examined, p.Matched); }));
                if (!closed) L.Text(resourceState, "storage.resources.checked", resourcePlan.Copies.Length, StorageSize(resourcePlan.Bytes), resourcePlan.Deferred);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!closed) DailyUiText.Error(resourceState, e); }
            finally { working = false; if (!closed) Controls(true); else lifetime.Dispose(); }
        };
        resourceOptimize.Click += async (_, _) =>
        {
            if (working || resourcePlan == null) return;
            if (BusyStorage()) { L.Text(resourceState, "storage.busy"); return; }
            working = true; Controls(false); L.Text(resourceState, "storage.resources.optimizing");
            try
            {
                DailyResourceResult done;
                SetCacheChanging(true);
                try { done = await new DailyStorageMaintenance(root).OptimizeResourceCopiesAsync(resourcePlan, lifetime.Token); }
                finally { SetCacheChanging(false); }
                resourcePlan = null;
                if (!closed) { L.Text(resourceState, "storage.resources.done", done.Files, StorageSize(done.Bytes), done.Deferred); await Measure(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!closed) DailyUiText.Error(resourceState, e); }
            finally { working = false; if (!closed) Controls(true); else lifetime.Dispose(); }
        };
        clean.Click += async (_, _) =>
        {
            if (storage == null || working) return;
            if (BusyStorage()) { L.Text(status, "storage.busy"); return; }
            working = true; Controls(false); L.Text(status, "logs.cleaning"); result.Visibility = Visibility.Collapsed;
            try
            {
                DailyLogCleanupResult done;
                SetCacheChanging(true);
                try
                {
                    if (LegacyEquipmentLogRoot is { } legacy) await DailyLogCleanup.ImportLegacyEquipmentLogsAsync(root, legacy, lifetime.Token);
                    done = await new DailyStorageMaintenance(root).CleanAsync(logPolicy, lifetime.Token); storage = null; SaveLogCleanupResult(done, true);
                }
                finally { SetCacheChanging(false); }
                if (!closed)
                {
                    L.Text(result, "storage.optimized", done.Files, done.CompressedFiles, MiB(done.Bytes), done.Skipped); result.Visibility = Visibility.Visible;
                    L.Text(status, "logs.scanning"); await Measure();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!closed) { if (e.Message == "logs.rescan") L.Text(status, "logs.rescan"); else DailyUiText.Error(status, e); } }
            finally { working = false; if (!closed) Controls(true); else lifetime.Dispose(); }
        };
        move.Click += async (_, _) =>
        {
            if (working) return;
            if (BusyStorage(true)) { L.Text(status, "storage.busy"); return; }
            var picker = new Microsoft.Win32.OpenFolderDialog { Title = L.Get("storage.pick") };
            if (picker.ShowDialog(dialog) != true) return;
            string destination = Path.Combine(picker.FolderName, "Dustweave-storage");
            if (DailyDialogs.ShowModal(DailyDialogs.Message(dialog, L.Get("storage.move"), L.Get("storage.confirm", destination), true)) != true) return;
            working = true; Controls(false); result.Visibility = Visibility.Collapsed; L.Text(status, "storage.move.preparing");
            try
            {
                DailyCacheMove moved;
                SetCacheChanging(true);
                try { moved = await new DailyStorageMigration(root).MoveAsync(destination, lifetime.Token, phases: new Progress<DailyStorageMigration.MoveProgress>(n => { if (!closed && working) ShowMoveProgress(n); })); }
                finally { SetCacheChanging(false); }
                if (!closed)
                {
                    location.Text = moved.Path; ShowSandboxLocation();
                    L.Text(result, "storage.moved", MiB(moved.Bytes), moved.RetainedFiles); result.Visibility = Visibility.Visible;
                    ResetMoveProgress(); L.Text(status, "logs.scanning"); await Measure();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!closed) Error(error); }
            finally { working = false; if (!closed) Controls(true); else lifetime.Dispose(); }
        };
        dialog.Loaded += async (_, _) => { await Scan(); if (moveWhenReady && !closed && move.IsEnabled) move.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
        dialog.Closed += (_, _) => { closed = true; ResetMoveProgress(); lifetime.Cancel(); if (!working) lifetime.Dispose(); };
        var body = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        layout.Children.Add(body); layout.Children.Add(footer);
        dialog.Content = layout; DailyDialogs.Prepare(dialog); return dialog;
    }
}
