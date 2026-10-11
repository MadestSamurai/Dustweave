using System.Windows;
using System.Windows.Controls;

namespace Dustweave.Desktop;

public sealed class TaskPresetChoice : System.ComponentModel.INotifyPropertyChanged
{
    public string Id { get; }
    public string Name { get; }
    public TaskPresetChoice(string id, string name)
    {
        Id = id; Name = name;
        WeakEventManager<DailyLanguage, EventArgs>.AddHandler(DailyLanguage.Current, nameof(DailyLanguage.Changed), LanguageChanged);
    }
    public string Label => Id.Length == 0 ? DailyLanguage.Current.Get("presets.default") : Name;
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void LanguageChanged(object? sender, EventArgs e) => PropertyChanged?.Invoke(this, new(nameof(Label)));
}

public sealed partial class DailyRunPanel
{
    private DailyTaskPresetStore presetStore = null!;
    private readonly ComboBox presetSelector = new() { Width = 180, DisplayMemberPath = nameof(TaskPresetChoice.Label), SelectedValuePath = nameof(TaskPresetChoice.Id), Margin = new(0, 0, 8, 0) };
    private readonly Button savePreset = new(), managePreset = new() { Content = "···", MinWidth = 34, Padding = new(8, 4, 8, 4) };
    private bool refreshingPresets;
    private string presetAccount = "", appliedPreset = "";
    public event Action? PresetsChanged;
    private FrameworkElement CreatePresets(string root)
    {
        presetStore = new(root);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new(8, 10, 0, 0) };
        L.Bind(presetSelector, FrameworkElement.ToolTipProperty, "presets.help");
        L.Bind(presetSelector, System.Windows.Automation.AutomationProperties.NameProperty, "presets.label");
        L.Bind(savePreset, ContentControl.ContentProperty, "presets.save");
        L.Bind(managePreset, FrameworkElement.ToolTipProperty, "presets.manage");
        L.Bind(managePreset, System.Windows.Automation.AutomationProperties.NameProperty, "presets.manage");
        bar.Children.Add(presetSelector); bar.Children.Add(savePreset); bar.Children.Add(managePreset); savePreset.Margin = new(0, 0, 8, 0);
        presetSelector.SelectionChanged += (_, _) => {
            if (refreshingPresets || isBusy || !DailyProfiles.ValidKey(activeAccount) || presetSelector.SelectedValue is not string id) return;
            try { presetStore.Assign(activeAccount, id); RefreshPresets(true); PresetsChanged?.Invoke(); }
            catch (Exception error) { ShowOperationError(DailyUserText.Error(error, L.Translate)); }
        };
        savePreset.Click += (_, _) => SavePreset(false);
        managePreset.Click += (_, _) => {
            var menu = new ContextMenu();
            void Item(string key, Action action) { var item = new MenuItem { Header = L.Get(key) }; item.Click += (_, _) => action(); menu.Items.Add(item); }
            if (presetSelector.SelectedValue is string { Length: > 0 } id)
            {
                Item("presets.replace", () => SavePreset(true));
                Item("presets.rename", () => SavePreset(true, renameOnly: true));
                Item("presets.delete", () => {
                    try {
                        var owner = Window.GetWindow(this);
                        if (DailyDialogs.ShowModal(DailyDialogs.Message(owner, L.Get("presets.delete"), L.Get("presets.delete_confirm"), true)) != true) return;
                        presetStore.Delete(id); RefreshPresets(false); PresetsChanged?.Invoke();
                    } catch (Exception error) { ShowOperationError(DailyUserText.Error(error, L.Translate)); }
                });
            }
            else Item("presets.save", () => SavePreset(false));
            var unused = presetStore.Read();
            foreach (var preset in unused.Presets.Where(p => !unused.Accounts.ContainsValue(p.Id)))
            {
                var remove = new MenuItem { Header = L.Get("presets.delete") + " · " + preset.Name };
                remove.Click += (_, _) => {
                    try {
                        if (DailyDialogs.ShowModal(DailyDialogs.Message(Window.GetWindow(this), L.Get("presets.delete"), L.Get("presets.delete_confirm"), true)) != true) return;
                        presetStore.Delete(preset.Id); RefreshPresets(); PresetsChanged?.Invoke();
                    } catch (Exception error) { ShowOperationError(DailyUserText.Error(error, L.Translate)); }
                };
                menu.Items.Add(remove);
            }
            managePreset.ContextMenu = menu; menu.PlacementTarget = managePreset; menu.IsOpen = true;
        };
        return bar;
    }
    public void RefreshPresets(bool apply = false)
    {
        if (presetStore == null) return;
        var data = presetStore.Read();
        string id = data.Accounts.GetValueOrDefault(activeAccount, "");
        var options = new[] { new TaskPresetChoice("", "") }.Concat(data.Presets.Select(p => new TaskPresetChoice(p.Id, p.Name))).ToArray();
        refreshingPresets = true;
        try { presetSelector.ItemsSource = options; presetSelector.SelectedValue = id; }
        finally { refreshingPresets = false; }
        bool changed = presetAccount != activeAccount || appliedPreset != id;
        presetAccount = activeAccount; appliedPreset = id;
        if (apply || changed && id.Length > 0)
        {
            var wanted = id.Length > 0 ? data.Presets.Single(p => p.Id == id).Tasks.ToHashSet() : DailyStageCatalog.Selectable.Select(s => s.Id).ToHashSet();
            foreach (var stage in DailyStageCatalog.All) planChoices[stage.Id] = wanted.Contains(stage.Id);
            if (!isBusy) ShowCurrentPlan();
        }
        presetSelector.IsEnabled = savePreset.IsEnabled = managePreset.IsEnabled = !isBusy && DailyProfiles.ValidKey(activeAccount);
    }
    private void SavePreset(bool replace, bool renameOnly = false)
    {
        if (isBusy || showingReport || !DailyProfiles.ValidKey(activeAccount)) return;
        try
        {
            string? id = replace ? presetSelector.SelectedValue as string : null;
            var existing = presetStore.Read().Presets.FirstOrDefault(p => p.Id == id);
            var owner = Window.GetWindow(this);
            var body = new StackPanel { Margin = new(24, 8, 24, 24) };
            var input = new TextBox { Text = existing?.Name ?? "", MaxLength = 60, Margin = new(0, 0, 0, 14) };
            L.Bind(input, System.Windows.Automation.AutomationProperties.NameProperty, "presets.name");
            var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; L.Text(hint, "presets.selection_only");
            var errorText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; errorText.SetResourceReference(TextBlock.ForegroundProperty, "Error");
            var save = new Button { HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true, Style = (Style)FindResource("PrimaryButton") }; L.Bind(save, ContentControl.ContentProperty, "presets.save");
            body.Children.Add(hint); body.Children.Add(input); body.Children.Add(errorText); body.Children.Add(save);
            var window = new Window { Owner = owner, Width = 480, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = body, Title = L.Get(renameOnly ? "presets.rename" : "presets.save") };
            save.Click += (_, _) => {
                try {
                    var preset = presetStore.Save(input.Text, renameOnly ? existing!.Tasks : SelectedPlanTasks, replace ? id : null);
                    presetStore.Assign(activeAccount, preset.Id); RefreshPresets(true); PresetsChanged?.Invoke(); window.DialogResult = true;
                } catch (Exception error) { DailyUiText.Error(errorText, error); }
            };
            window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); }; DailyDialogs.ShowModal(window);
        }
        catch (Exception error) { ShowOperationError(DailyUserText.Error(error, L.Translate)); }
    }
}
