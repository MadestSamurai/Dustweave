using System.IO;
using System.Windows;
namespace Dustweave.Desktop;

public partial class App : Application
{
    internal string[]? StartupArguments { get; init; }
    private Mutex? instance; private AccountSessions? sessions;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = StartupArguments ?? e.Args;
        if (args.Length == 2 && args[0] == "--check-tool-session")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { await ToolMenuProbe.CheckAsync(Path.GetFullPath(args[1])); Shutdown(); }
            catch (Exception error) { DailyJson.Write(Path.Combine(args[1], "validation.json"), new { status = "failed", error = error.ToString(), realGameTouched = false }); Shutdown(1); }
            return;
        }
        if (args.Length == 2 && args[0] == "--tool-catalog")
        {
            DailyJson.Write(args[1], DailyToolCatalog.All.Select(t => new { t.Id, t.Name, t.Category, t.Repository, t.AssemblyName, version = ToolApplicationHost.Version(t) }));
            Shutdown(); return;
        }
        if (args.Length == 2 && args[0] == "--identity")
        {
            File.WriteAllText(args[1], $"Dustweave {DailyProductVersion.Current}\nProtocol baseline {DailyIdentity.Version}\n{DailyIdentity.RuntimeName}\n");
            Shutdown();
            return;
        }
        if (args.Length == 2 && args[0] == "--check-plugin")
        {
            try
            {
                var info = DailyPlugin.Current;
                var extension = DailyExtensionLoader.Load(info);
                if (extension?.ApiVersion != DailyPlugin.ApiVersion)
                    throw new InvalidDataException("Plugin did not load");
                var proofs = DailyWorkflowRegistry.Proofs(extension);
                DailyJson.Write(args[1], new
                {
                    status = "passed",
                    info,
                    api = extension.ApiVersion,
                    proofs = proofs.Length,
                    realGameTouched = false
                });
                Shutdown();
            }
            catch (Exception error) { DailyJson.Write(args[1], new { status = "failed", error = error.ToString(), realGameTouched = false }); Shutdown(1); }
            return;
        }
        if (args.Length == 2 && args[0] == "--check-stage-host")
        {
            string destination = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(destination);
            try
            {
                const string label = "日常 애莉 繁體 😀";
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    label,
                    large = new string('界', 80000)
                });
                using var parsed = System.Text.Json.JsonDocument.Parse(json);
                if (parsed.RootElement.GetProperty("label").GetString() != label || parsed.RootElement.GetProperty("large").GetString()!.Length != 80000)
                    throw new InvalidDataException("Managed result round-trip failed");
                var proofs = DailyWorkflowRegistry.Proofs();
                if (proofs.Select(p => p.Role).Distinct().Count() != proofs.Length)
                    throw new InvalidDataException("Duplicate transaction proof");
                DailyJson.Write(Path.Combine(destination, "stage-protocol.json"), new
                {
                    status = "passed",
                    engine = "dotnet-workflow-v1",
                    unicodeRoundtrip = true,
                    registeredProofs = proofs.Length,
                    externalWorker = false,
                    realGameTouched = false
                });
                Shutdown();
            }
            catch (Exception ex) { DailyJson.Write(Path.Combine(destination, "stage-protocol.json"), new { status = "failed", error = ex.ToString(), realGameTouched = false }); Shutdown(1); }
            return;
        }
        if (args.Length == 3 && args[0] == "--check-client")
        {
            try
            {
                var prepared = Dustweave.Compatibility.DailyHookCompiler.Prepare(args[1]);
                Directory.CreateDirectory(args[2]);
                File.WriteAllBytes(Path.Combine(args[2], DailyIdentity.RuntimeName + ".dll"), prepared.Payload);
                DailyJson.Write(Path.Combine(args[2], "compatibility.json"), prepared.Report);
                DailyJson.Write(Path.Combine(args[2], "guild-compatibility.json"), prepared.GuildReport);
                DailyJson.Write(Path.Combine(args[2], "startup-compatibility.json"), prepared.StartupReport);
                Shutdown();
            }
            catch (Exception ex) { Directory.CreateDirectory(args[2]); DailyJson.Write(Path.Combine(args[2], "error.json"), new { error = ex.ToString() }); Shutdown(1); }
            return;
        }
        if (args.Length == 3 && args[0] == "--replay")
        {
            try
            {
                var rows = GuildReplay.Run(args[1]);
                DailyJson.Write(args[2], new
                {
                    status = rows.Count == 0 ? "no_decisions" : rows.All(r => r.Match) ? "passed" : "mismatch",
                    rows,
                    realGameTouched = false
                });
                Shutdown(rows.Any(r => !r.Match) ? 1 : 0);
            }
            catch (Exception ex) { DailyJson.Write(args[2], new { status = "error", error = ex.Message }); Shutdown(1); }
            return;
        }
        if (args.Length == 1 && args[0] == "--trade-offline")
        {
            DailyLanguage.Current.Initialize(DailyIdentity.DataRoot);
            var offline = new Window { Width = 1060, Height = 780, MinWidth = 760, MinHeight = 560, Content = new TradePlanPanel(DailyIdentity.DataRoot, allowGame:false), WindowStartupLocation = WindowStartupLocation.CenterScreen };
            DailyLanguage.Current.Bind(offline, Window.TitleProperty, "trade.offline_title");
            offline.Show();
            return;
        }
        if (args.Length == 3 && args[0] == "--trade-smoke")
        {
            string folder = Path.GetFullPath(args[2]);
            Directory.CreateDirectory(folder);
            var panel = new TradePlanPanel(Path.Combine(folder, "fixture-data"));
            var window = new Window { Title = "跑商离线检查", Width = 1060, Height = 780, Content = panel, ShowActivated = false, ShowInTaskbar = false, Left = -12000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual };
            window.Loaded += async (_, _) =>
            {
                try
                {
                    panel.LoadSnapshot(Path.GetFullPath(args[1]));
                    await panel.Compute();
                    window.UpdateLayout();
                    await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1060, 780, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using (var file = File.Create(Path.Combine(folder, "trade-preview.png")))
                        encoder.Save(file);
                    bool generated = panel.PlanReady;
                    DailyJson.Write(Path.Combine(folder, "trade-smoke.json"), new
                    {
                        passed = generated,
                        realGameTouched = false
                    });
                    Shutdown(generated ? 0 : 1);
                }
                catch (Exception ex) { DailyJson.Write(Path.Combine(folder, "trade-smoke.json"), new { passed = false, error = ex.ToString(), realGameTouched = false }); Shutdown(1); }
            };
            window.Show();
            return;
        }
        if (args.Length == 2 && args[0] == "--design-preview")
        {
            var previewRoot = Path.GetFullPath(args[1]);
            var fixture = new DemoEnvironment(previewRoot);
            var preview = new MainWindow(fixture, fixture, previewRoot, previewRoot, automatedSmoke: false);
            DailyLanguage.Current.Bind(preview, Window.TitleProperty, "app.preview_title"); preview.Show();
            return;
        }
        string? smoke = args.Length == 2 && args[0] == "--smoke" ? Path.GetFullPath(args[1]) : null;
        string? runSelected = args.Length == 2 && args[0] == "--run-selected" ? Path.GetFullPath(args[1]) : null;
        string? inspectAccount = args.Length == 2 && args[0] == "--inspect-account" && DailyProfiles.ValidKey(args[1]) ? args[1] : null;
        if (args.Length > 0 && smoke == null && runSelected == null && inspectAccount == null)
        {
            Shutdown(2);
            return;
        }
        try
        {
            if (smoke != null)
            {
                var fixture = new DemoEnvironment(smoke);
                new MainWindow(fixture, fixture, smoke, smoke) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = 0 }.Show();
                return;
            }
            DailyLanguage.Current.Initialize(DailyIdentity.DataRoot); instance = new Mutex(true, DailyApplication.InstanceMutex, out bool first);
            if (!first)
            {
                MessageBox.Show(DailyLanguage.Current.Get("startup.already_open"), "Dustweave");
                Shutdown();
                return;
            }
            sessions = new AccountSessions();
            var window = new MainWindow(sessions, new DailyGameHost(), DailyIdentity.DataRoot, null);
            if (inspectAccount != null) window.Loaded += async (_, _) => await window.InspectAccountAsync(inspectAccount);
            if (runSelected != null)
            {
                var request = DailyJson.TryRead<QueuePlanRequest>(runSelected) ?? throw new InvalidDataException("无法读取本次勾选环节。");
                request.Validate(sessions.Read().CurrentKey, new DailyPreferenceStore(DailyIdentity.DataRoot).Read(sessions.Read().CurrentKey));
                window.Loaded += async (_, _) => await window.RunSelectedAsync(request);
            }
            window.Show();
        }
        catch (Exception ex)
        {
            if (smoke != null)
            {
                DailyJson.Write(Path.Combine(smoke, "smoke.json"), new { status = "failed", error = ex.ToString(), realGameTouched = false });
                Shutdown(1);
                return;
            }
            try { DailyJson.Write(Path.Combine(DailyIdentity.DataRoot, "startup-error.json"), new { version = DailyIdentity.Version, utc = DateTimeOffset.UtcNow, error = ex.ToString() }); } catch { }
            ShowStartupFailure(ex); Shutdown(1);
        }
    }
    private static void ShowStartupFailure(Exception error)
    {
        string text, title;
        try
        {
            var language = DailyLanguage.Current;
            text = DailyUserText.Error(error, language.Translate) + "\n\n" + language.Get("startup.diagnostic");
            title = language.Get("startup.failed");
        }
        catch
        {
            // Resource loading itself can fail during startup; reporting must not recurse.
            text = "Dustweave could not start. See startup-error.json for details.";
            title = "Dustweave";
        }
        MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        sessions?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}

