using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dustweave.Compatibility;

namespace Dustweave.Desktop;
internal static class PandoraTool
{
    internal static int Run(string[] args, Action<Application>? configure)
    {
        if (args.Length == 2 && args[0] == "--identity") { File.WriteAllText(args[1], $"Dustweave {DailyProductVersion.Current} · PANDORA ESCAPE\n"); return 0; }
        if (args.Length == 3 && args[0] == "--check-client")
        {
            Directory.CreateDirectory(args[2]);
            File.WriteAllBytes(Path.Combine(args[2], "pandora.dll"), PandoraCompiler.Prepare(args[1]));
            DailyJson.Write(Path.Combine(args[2], "result.json"), new { status = "passed", realGameTouched = false });
            return 0;
        }
        var app = new App { StartupArguments = ["--pandora-window", ..args] };
        app.InitializeComponent(); configure?.Invoke(app); return app.Run();
    }
    internal static void Start(Application app, string[] args)
    {
        bool smoke = args.Length == 2 && args[0] == "--smoke";
        string root = smoke ? Path.Combine(Path.GetFullPath(args[1]), "isolated") : DailyIdentity.DataRoot;
        DailyLanguage.Current.Initialize(root);
        var theme = new DailyTheme(root); app.Exit += (_, _) => theme.Dispose();
        if (!smoke)
        {
            var single = new Mutex(true, DailyToolCatalog.Find("pandora").MutexName, out bool first);
            app.Exit += (_, _) => single.Dispose();
            if (!first) { MessageBox.Show(DailyLanguage.Current.Get("pandora.already_open"), "PANDORA ESCAPE"); app.Shutdown(); return; }
        }
        var window = new PandoraWindow(smoke); app.MainWindow = window; window.Show();
        if (!smoke) return;
        app.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                Directory.CreateDirectory(args[1]);
                foreach (var language in new[] { "zh-CN", "zh-TW", "en-US" })
                foreach (var appearance in new[] { DailyAppearance.Light, DailyAppearance.Dark })
                foreach (bool challenge in new[] { false, true })
                {
                    DailyLanguage.Current.Select(language); theme.Select(appearance);
                    window.SetPreview(JsonNode.Parse("""{"Reason":"playing","Completed":7,"Seconds":184,"Hp":850,"MaxHp":1000,"Stages":[{"Id":11,"Group":1,"Difficulty":1,"Index":0,"Challenge":false,"Unlocked":true,"Name":"STAGE 1"},{"Id":99,"Group":9,"Difficulty":1,"Index":1,"Challenge":true,"Unlocked":true,"Name":"Challenge"}]}""")!.AsObject(), challenge, autoEquipment: !challenge);
                    app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    window.UpdateLayout();
                    if (window.ChallengeNotice.Visibility != (challenge ? Visibility.Visible : Visibility.Collapsed)) throw new InvalidOperationException("Challenge warning missing");
                    if (window.EquipmentPanel.Visibility != (challenge ? Visibility.Visible : Visibility.Collapsed)) throw new InvalidOperationException("Equipment option must be challenge-only");
                    if (challenge && (window.AutoEquipment.IsChecked != false || window.StatusText.Text != DailyLanguage.Current.Get("pandora.state.manual-equipment"))) throw new InvalidOperationException("Manual equipment choice is not visible");
                    var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(args[1], $"{language}-{appearance}-{(challenge ? "challenge" : "currency")}.png")); png.Save(stream);
                }
                DailyJson.Write(Path.Combine(args[1], "result.json"), new { status = "passed", modes = 2, languages = 3, themes = 2, realGameTouched = false });
                window.Close();
            }
            catch (Exception e) { DailyJson.Write(Path.Combine(args[1], "error.json"), new { error = e.ToString() }); app.Shutdown(1); }
        }), DispatcherPriority.ApplicationIdle);
    }
}
