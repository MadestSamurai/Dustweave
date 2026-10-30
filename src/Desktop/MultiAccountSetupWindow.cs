using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Dustweave.Accounts;

namespace Dustweave.Desktop;

// Offered once after saving a second distinct account, independently of first-use login.
internal sealed class MultiAccountSetupWindow : Window
{
    private static DailyLanguage L => DailyLanguage.Current;
    private readonly string root;
    private readonly SandboxInstallation installation;
    private readonly RadioButton sequential = new() { Name = "MultiAccountSequential", GroupName = "ExecutionMode", Margin = new(0, 0, 0, 18) };
    private readonly RadioButton parallel = new() { Name = "MultiAccountParallel", GroupName = "ExecutionMode" };
    private readonly Button next = new() { IsDefault = true }, later = new() { Margin = new(0, 0, 10, 0) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 14, 0, 0) };
    private bool working, confirmed;
    internal bool CanContinue => next.IsEnabled;

    internal MultiAccountSetupWindow(Window owner, string root, SandboxInstallation installation)
    {
        Owner = owner; this.root = root; this.installation = installation;
        Width = 620; MinWidth = 440; SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 40); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        L.Bind(this, TitleProperty, "multiaccount.title");
        var body = new StackPanel { Margin = new(24, 8, 24, 24) };
        var introduction = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 20) };
        L.Text(introduction, "multiaccount.body"); body.Children.Add(introduction);
        void AddChoice(RadioButton choice, string title, string help)
        {
            var text = new StackPanel();
            var label = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            var detail = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 5, 0, 0) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Ink"); detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk");
            L.Text(label, title); L.Text(detail, help); text.Children.Add(label); text.Children.Add(detail);
            choice.Content = text; choice.HorizontalContentAlignment = HorizontalAlignment.Stretch; choice.SetResourceReference(Control.ForegroundProperty, "Ink");
            L.Bind(choice, System.Windows.Automation.AutomationProperties.NameProperty, title); body.Children.Add(choice);
        }
        AddChoice(sequential, "onboarding.execution_sequential", "multiaccount.sequential_help");
        AddChoice(parallel, "onboarding.execution_parallel", "onboarding.execution_parallel_help");
        bool selected = DailyMultiAccountSetup.Read(root)?.Parallel ?? (DailyJson.TryRead<DailyParallelOptions>(Path.Combine(root, "parallel-options.json")) ?? new()).Enabled;
        parallel.IsChecked = selected; sequential.IsChecked = !selected;
        foreach (var choice in new[] { sequential, parallel }) choice.Checked += (_, _) => {
            try { DailyMultiAccountSetup.Write(root, new(Parallel: parallel.IsChecked == true)); status.Text = ""; }
            catch (Exception error) { DailyUiText.Error(status, error, ""); }
        };
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 20, 0, 0) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); L.Text(hint, "multiaccount.later_help"); body.Children.Add(hint); body.Children.Add(status);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 22, 0, 0) };
        L.Bind(later, ContentControl.ContentProperty, "multiaccount.later"); later.Click += (_, _) => Close(); actions.Children.Add(later);
        L.Bind(next, ContentControl.ContentProperty, "onboarding.execution_continue"); next.Style = (Style)FindResource("PrimaryButton");
        next.Click += async (_, _) => await FinishAsync(); actions.Children.Add(next); body.Children.Add(actions);
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Closing += OnClosing; DailyDialogs.Prepare(this);
    }

    internal async Task FinishAsync()
    {
        if (working) return;
        working = true; next.IsEnabled = later.IsEnabled = sequential.IsEnabled = parallel.IsEnabled = false;
        try
        {
            bool useParallel = parallel.IsChecked == true;
            DailyMultiAccountSetup.Write(root, new(Parallel: useParallel));
            if (useParallel && (await Task.Run(installation.Read)).Error != null)
            {
                var setup = new SandboxSetupWindow(this, installation, SandboxSetupPurpose.Onboarding);
                DailyDialogs.ShowModal(setup);
                if (!setup.Ready || (await Task.Run(installation.Read)).Error != null)
                {
                    L.Text(status, "onboarding.execution_cancelled"); status.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); return;
                }
            }
            string path = Path.Combine(root, "parallel-options.json");
            var options = (DailyJson.TryRead<DailyParallelOptions>(path) ?? new()) with { Enabled = useParallel };
            options.Validate(); DailyJson.Write(path, options);
            DailyMultiAccountSetup.Write(root, new("completed", useParallel)); confirmed = true; Close();
        }
        catch (Exception error) { DailyUiText.Error(status, error, ""); status.SetResourceReference(TextBlock.ForegroundProperty, "Error"); }
        finally { working = false; next.IsEnabled = later.IsEnabled = sequential.IsEnabled = parallel.IsEnabled = true; }
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (confirmed) return;
        if (working) { e.Cancel = true; return; }
        try { DailyMultiAccountSetup.Write(root, new("dismissed", parallel.IsChecked == true)); }
        catch (Exception error) { e.Cancel = true; DailyUiText.Error(status, error, ""); }
    }
}
