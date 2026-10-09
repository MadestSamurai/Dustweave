extern alias liveUtility;
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
        if(args.Length>0&&args[0]=="--mansion-window"){MansionTool.Start(this,args.Skip(1).ToArray());return;}
        if (args.Length == 3 && args[0] == "--restart-after")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                System.Diagnostics.Process? owner = null;
                try { owner = System.Diagnostics.Process.GetProcessById(int.Parse(args[1])); } catch (ArgumentException) { }
                using (var parent = owner)
                {
                    if (parent != null && !parent.HasExited)
                    {
                        if (parent.StartTime.ToUniversalTime().Ticks != long.Parse(args[2]) || !string.Equals(parent.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Invalid restart owner");
                        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                        await parent.WaitForExitAsync(deadline.Token);
                    }
                }
                // Let the previous app release its named single-instance mutex.
                using var gate = new Mutex(false, DailyApplication.InstanceMutex);
                bool acquired; try { acquired = gate.WaitOne(TimeSpan.FromSeconds(5)); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("Restart owner still active");
                gate.ReleaseMutex();
                _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WorkingDirectory = Directory.GetCurrentDirectory() });
                Shutdown();
            }
            catch { Shutdown(1); }
            return;
        }
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
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var info = DailyPlugin.Current;
                var extension = DailyExtensionLoader.Load(info);
                if (extension?.ApiVersion != DailyPlugin.ApiVersion)
                    throw new InvalidDataException("Plugin did not load");
                var proofs = DailyWorkflowRegistry.Proofs(extension);
                bool connectionPrepared = false;
                if (info.HookSources.Length != 0)
                {
                    string managed = DailyPluginProbe.InstalledGameManagedDirectory();
                    await liveUtility::Dustweave.Connection.LiveEntry.RunAsync(["prepare", managed, Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "connection")], false);
                    connectionPrepared = true;
                }
                DailyJson.Write(args[1], new
                {
                    status = "passed",
                    info = new { info.Available, info.State, info.Id, info.Version, info.Fingerprint },
                    api = extension.ApiVersion,
                    proofs = proofs.Length,
                    connectionPrepared,
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
        bool scheduled = args.Length == 1 && args[0] == "--scheduled";
        bool updated = args.Length == 2 && args[0] == "--updated";
        string? smoke = args.Length == 2 && args[0] == "--smoke" ? Path.GetFullPath(args[1]) : null;
        string? runSelected = args.Length == 2 && args[0] == "--run-selected" ? Path.GetFullPath(args[1]) : null;
        string? viewAccount = args.Length == 2 && args[0] == "--view-account" && DailyProfiles.ValidKey(args[1]) ? args[1] : null;
        string? inspectAccount = args.Length == 2 && args[0] == "--inspect-account" && DailyProfiles.ValidKey(args[1]) ? args[1] : null;
        string? resumeAccount = args.Length == 2 && args[0] == "--resume-account" && DailyProfiles.ValidKey(args[1]) ? args[1] : null;
        if (args.Length > 0 && smoke == null && runSelected == null && viewAccount == null && inspectAccount == null && resumeAccount == null && !scheduled && !updated)
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
                if (!scheduled) MessageBox.Show(DailyLanguage.Current.Get("startup.already_open"), "Dustweave");
                Shutdown();
                return;
            }
            StartupDiagnostics.Mark("main-window");
            sessions = new AccountSessions();
            var window = new MainWindow(sessions, new DailyGameHost(), DailyIdentity.DataRoot, null) { ScheduledStartup = scheduled, UpdatedStartup = updated, UpdateNonce = updated ? args[1] : "" };
            if (viewAccount != null) { DailySandbox.RequireBoundAccount(viewAccount); window.Loaded += (_, _) => { try { window.ViewAccountTasks(viewAccount); } catch (Exception error) { window.ShowTaskNavigationError(error); } }; }
            if (inspectAccount != null) window.Loaded += async (_, _) => await window.InspectAccountAsync(inspectAccount);
            if (resumeAccount != null) window.Loaded += async (_, _) => await window.ResumeQueueAsync(resumeAccount);
            if (runSelected != null)
            {
                var request = DailyJson.TryRead<QueuePlanRequest>(runSelected) ?? throw new InvalidDataException("无法读取本次勾选环节。");
                request.Validate(sessions.Read().CurrentKey, new DailyPreferenceStore(DailyIdentity.DataRoot).Read(sessions.Read().CurrentKey));
                window.Loaded += async (_, _) => await window.RunSelectedAsync(request);
            }
            window.Show();
            StartupDiagnostics.Mark("main-window-visible");
        }
        catch (Exception ex)
        {
            if (smoke != null)
            {
                DailyJson.Write(Path.Combine(smoke, "smoke.json"), new { status = "failed", error = ex.ToString(), realGameTouched = false });
                Shutdown(1);
                return;
            }
            ShowStartupFailure(ex, StartupDiagnostics.Fail(ex)); Shutdown(1);
        }
    }
    internal static void ShowStartupFailure(Exception error, string diagnostic)
    {
        string text, title;
        try
        {
            var language = DailyLanguage.Current;
            language.Initialize(DailyIdentity.DataRoot);
            text = DailyUserText.Error(error, language.Translate) + "\n\n" + language.Get(diagnostic.Length > 0 ? "startup.diagnostic_path" : "startup.diagnostic_unavailable", diagnostic);
            title = language.Get("startup.failed");
        }
        catch
        {
            // Resource loading itself can fail during startup; reporting must not recurse.
            text = "Dustweave 启动失败 / could not start.\n\n" + error.Message + (diagnostic.Length > 0 ? "\n\n" + diagnostic : "");
            title = "Dustweave";
        }
        try { MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Error); }
        catch { NativeStartupMessage(0, text, title, 0x10); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int NativeStartupMessage(nint owner, string text, string title, uint flags);
    protected override void OnExit(ExitEventArgs e)
    {
        sessions?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}
