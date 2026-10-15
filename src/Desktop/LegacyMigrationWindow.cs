using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dustweave.Desktop;

internal sealed class LegacyMigrationWindow : Window
{
    private readonly TextBox acknowledgement = new() { MinHeight = 40, Margin = new Thickness(0, 8, 0, 12) };
    private readonly Button proceed = new() { IsEnabled = false, MinHeight = 40 };
    private readonly ComboBox languages = new() { Width = 148, MinHeight = 36, DisplayMemberPath = "Label", SelectedValuePath = "Code" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private DailyLanguage L => DailyLanguage.Current;

    internal LegacyMigrationWindow()
    {
        Width = 740; Height = Math.Min(760, SystemParameters.WorkArea.Height - 48);
        MaxWidth = Math.Max(320, SystemParameters.WorkArea.Width - 32);
        MinHeight = Math.Min(480, Height); MinWidth = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize; ShowInTaskbar = true;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Dustweave;component/Assets/Dustweave.ico"));
        L.Bind(this, TitleProperty, "migration.title");
        AutomationProperties.SetAutomationId(this, "LegacyMigrationNotice");
        var body = new StackPanel { Margin = new Thickness(28, 8, 28, 26) };
        var top = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 20) };
        var version = new TextBlock { Text = "Dustweave 1.2.12", VerticalAlignment = VerticalAlignment.Center };
        version.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); top.Children.Add(version);
        DockPanel.SetDock(languages, Dock.Right); top.Children.Add(languages);
        languages.ItemsSource = new[] { new { Code = "zh-CN", Label = "简体中文" }, new { Code = "zh-TW", Label = "繁體中文" }, new { Code = "en-US", Label = "English" } };
        languages.SelectedValue = L.Code;
        AutomationProperties.SetName(languages, "Language / 语言 / 語言");
        languages.SelectionChanged += (_, _) => { if (languages.SelectedValue is string code && code != L.Code) L.Select(code); };
        body.Children.Add(top);
        body.Children.Add(Text("migration.heading", 27, FontWeights.SemiBold, bottom: 12));
        body.Children.Add(Text("migration.intro", bottom: 20));
        var warning = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 22) };
        warning.SetResourceReference(BackgroundProperty, "WarningSoft");
        var warningText = Text("migration.warning"); warningText.SetResourceReference(TextBlock.ForegroundProperty, "Warning"); warning.Child = warningText; body.Children.Add(warning);
        body.Children.Add(Text("migration.download_heading", 17, FontWeights.SemiBold, bottom: 10));
        var downloads = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var github = Action("migration.github", () => Open(DailyLegacyMigration.Download)); github.Style = (Style)Application.Current.FindResource("PrimaryButton");
        downloads.Children.Add(github); downloads.Children.Add(Action("migration.quark", () => Open(DailyLegacyMigration.Mirror)));
        downloads.Children.Add(Action("migration.copy_code", () => { try { Clipboard.SetText(DailyLegacyMigration.MirrorCode); status.Text = L.Get("migration.copied"); } catch { status.Text = L.Get("migration.copy_failed"); } }));
        body.Children.Add(downloads);
        body.Children.Add(Text("migration.install", bottom: 8));
        status.SetResourceReference(TextBlock.ForegroundProperty, "MutedInk"); body.Children.Add(status);
        var divider = new Border { Height = 1, Margin = new Thickness(0, 18, 0, 18) }; divider.SetResourceReference(BackgroundProperty, "Line"); body.Children.Add(divider);
        body.Children.Add(Text("migration.continue_heading", 17, FontWeights.SemiBold, bottom: 8));
        body.Children.Add(Text("migration.instruction"));
        AutomationProperties.SetAutomationId(acknowledgement, "LegacyAcknowledgement");
        L.Bind(acknowledgement, AutomationProperties.NameProperty, "migration.instruction");
        acknowledgement.TextChanged += (_, _) => proceed.IsEnabled = DailyLegacyMigration.Accepts(acknowledgement.Text);
        body.Children.Add(acknowledgement);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(Action("migration.exit", Close));
        L.Bind(proceed, ContentControl.ContentProperty, "migration.continue");
        AutomationProperties.SetAutomationId(proceed, "LegacyContinue");
        proceed.Click += (_, _) => { if (DailyLegacyMigration.Accepts(acknowledgement.Text)) DialogResult = true; };
        actions.Children.Add(proceed); body.Children.Add(actions);
        body.Children.Add(Text("migration.session_only", 12, bottom: 0, top: 10));
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = body };
        DailyDialogs.Prepare(this); ShowInTaskbar = true;
    }

    private TextBlock Text(string key, double size = 14, FontWeight? weight = null, double bottom = 0, double top = 0)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = size, FontWeight = weight ?? FontWeights.Normal, Margin = new Thickness(0, top, 0, bottom), LineHeight = size * 1.5 };
        L.Text(text, key); return text;
    }
    private Button Action(string key, Action action)
    {
        var button = new Button { Margin = new Thickness(0, 0, 8, 0), MinHeight = 40 };
        L.Bind(button, ContentControl.ContentProperty, key); button.Click += (_, _) => action(); return button;
    }
    private void Open(string address)
    {
        try { Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }); status.Text = L.Get("migration.browser_opened"); }
        catch { status.Text = L.Get("migration.open_failed") + "\n" + address; }
    }

    internal static async Task ProbeAsync(string output)
    {
        Directory.CreateDirectory(output);
        var cases = new List<string>();
        using var theme = new DailyTheme(DailyIdentity.DataRoot);
        foreach (var appearance in new[] { DailyAppearance.Light, DailyAppearance.Dark })
        foreach (var code in DailyLanguage.Codes)
        {
            theme.Select(appearance); DailyLanguage.Current.Select(code);
            var window = new LegacyMigrationWindow { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = 0, Width = 740, Height = 760 };
            window.Show(); window.UpdateLayout();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (window.proceed.IsEnabled) throw new Exception("Blank acknowledgement enabled continuation.");
            window.acknowledgement.Text = "我不需要更";
            if (window.proceed.IsEnabled) throw new Exception("Partial acknowledgement enabled continuation.");
            window.acknowledgement.Text = code == "en-US" ? DailyLegacyMigration.EnglishAcknowledgement : DailyLegacyMigration.ChineseAcknowledgement;
            if (!window.proceed.IsEnabled) throw new Exception("Complete acknowledgement rejected.");
            window.languages.SelectedValue = code == "en-US" ? "zh-TW" : "en-US";
            if (!window.proceed.IsEnabled) throw new Exception("Switching language lost typed acknowledgement.");
            window.languages.SelectedValue = code; window.acknowledgement.Clear();
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap(740, 760, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(output, $"migration-{code}-{appearance}.png"))) encoder.Save(file);
            window.Close(); cases.Add($"{code}-{appearance}: empty/partial/valid acknowledgement, language switching, close");
        }
        var declined = new LegacyMigrationWindow();
        declined.Loaded += (_, _) => declined.Dispatcher.BeginInvoke(declined.Close);
        if (declined.ShowDialog() == true) throw new Exception("Closing granted consent.");
        var accepted = new LegacyMigrationWindow();
        accepted.Loaded += (_, _) => accepted.Dispatcher.BeginInvoke(new Action(() => {
            accepted.acknowledgement.Text = DailyLegacyMigration.EnglishAcknowledgement;
            accepted.proceed.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        if (accepted.ShowDialog() != true) throw new Exception("Explicit acknowledgement did not continue.");
        DailyJson.Write(Path.Combine(output, "result.json"), new { status = "passed", cases, closesWithoutConsent = true, explicitInputRequired = true, realGameTouched = false });
    }
}
