using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dustweave.Compatibility;

namespace Dustweave.Desktop;
internal static class MansionTool
{
    internal static int Run(string[] args, Action<Application>? configure)
    {
        if (args.Length == 2 && args[0] == "--identity")
        {
            File.WriteAllText(args[1], $"Dustweave {DailyProductVersion.Current} · MANSION RUNAWAY\n");
            return 0;
        }

        if (args.Length == 3 && args[0] == "--check-client")
        {
            Directory.CreateDirectory(args[2]);
            File.WriteAllBytes(Path.Combine(args[2], "mansion.dll"), MansionCompiler.Prepare(args[1]));
            return 0;
        }

        var app = new App
        {
            StartupArguments = ["--mansion-window", ..args]
        };
        app.InitializeComponent();
        configure?.Invoke(app);
        return app.Run();
    }

    internal static void Start(Application app, string[] args)
    {
        bool smoke = args.Length == 2 && args[0] == "--smoke";
        string root = smoke ? Path.Combine(Path.GetFullPath(args[1]), "isolated") : DailyIdentity.DataRoot;
        DailyLanguage.Current.Initialize(root);
        var theme = new DailyTheme(root);
        app.Exit += (_, _) => theme.Dispose();
        if (!smoke)
        {
            var single = new Mutex(true, DailyToolCatalog.Find("mansion-runaway").MutexName, out bool first);
            app.Exit += (_, _) => single.Dispose();
            if (!first)
            {
                MessageBox.Show(DailyLanguage.Current.Get("mansion.already_open"), "MANSION RUNAWAY");
                app.Shutdown();
                return;
            }
        }

        var window = new MansionWindow(smoke);
        app.MainWindow = window;
        window.Show();
        if (smoke)
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var directory = Path.GetFullPath(args[1]);
                    Directory.CreateDirectory(directory);
                    foreach (var language in new[]
                    {
                        "zh-CN",
                        "zh-TW",
                        "en-US"
                    }

                    )
                        foreach (var appearance in new[]
                        {
                            DailyAppearance.Light,
                            DailyAppearance.Dark
                        }

                        )
                        foreach (bool goalMode in new[] { false, true })
                        {
                            DailyLanguage.Current.Select(language);
                            theme.Select(appearance);
                            window.SetPreview(JsonNode.Parse(Preview().ToJsonString())!.AsObject(), goalMode);
                            if (window.Goal80Box.IsChecked != goalMode || (goalMode && (window.ChallengeMode.IsChecked != true || window.RetryBox.Visibility != Visibility.Collapsed)))
                                throw new InvalidOperationException("Goal-mode controls differ from selected behavior");
                            app.Dispatcher.Invoke(() =>
                            {
                            }, DispatcherPriority.ApplicationIdle);
                            window.UpdateLayout();
                            var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                            image.Render(window);
                            var png = new PngBitmapEncoder();
                            png.Frames.Add(BitmapFrame.Create(image));
                            using var stream = File.Create(Path.Combine(directory, language + "-" + appearance + (goalMode ? "-goal80" : "") + ".png"));
                            png.Save(stream);
                        }

                    DailyJson.Write(Path.Combine(directory, "result.json"), new { status = "passed", languages = 3, themes = 2, goalModes = 2, realGameTouched = false });
                    window.Close();
                }
                catch (Exception error)
                {
                    DailyJson.Write(Path.Combine(args[1], "error.json"), new { error = error.ToString() });
                    app.Shutdown(1);
                }
            }), DispatcherPriority.ApplicationIdle);
    }

    static JsonObject Preview()
    {
        var tiles = new JsonArray();
        var coins = new JsonArray();
        for (int y = 0; y < 11; y++)
            for (int x = 0; x < 11; x++)
                if (x % 2 == 1 || y % 2 == 1)
                {
                    tiles.Add(new JsonObject { ["X"] = x, ["Y"] = y, ["World"] = new JsonObject { ["X"] = x * 2 - 10, ["Z"] = y * 2 - 10 } });
                    if ((x + y) % 3 == 0)
                        coins.Add(new JsonObject { ["Position"] = new JsonObject { ["X"] = x * 2 - 10, ["Z"] = y * 2 - 10 } });
                }

        return new JsonObject
        {
            ["Objective"] = new JsonObject
            {
                ["Stage"] = "4",
                ["OrdinaryRemaining"] = 27,
                ["Seconds"] = 83.4,
                ["Score"] = "731",
                ["Chain"] = 36,
                ["MaxChain"] = 52
            },
            ["Camera"] = new JsonObject
            {
                ["Yaw"] = 90
            },
            ["Board"] = new JsonObject
            {
                ["Width"] = 11,
                ["Height"] = 11,
                ["Tiles"] = tiles,
                ["Coins"] = coins,
                ["Player"] = new JsonObject
                {
                    ["X"] = 0,
                    ["Z"] = 0
                },
                ["Health"] = 2,
                ["Enemies"] = new JsonArray(new JsonObject { ["Position"] = new JsonObject { ["X"] = 6, ["Z"] = 6 } }),
                ["Exits"] = new JsonArray()
            }
        };
    }
}

