extern alias liveUtility;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
namespace Dustweave.Desktop;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        bool window = args.Length == 0 || args[0] is "--desktop-session" or "--launch-desktop"
            or "--view-account" or "--inspect-account" or "--resume-account" or "--run-selected" or "--scheduled" or "--updated";
        try { if (window) StartupDiagnostics.Begin(); return MainCore(args); }
        catch (Exception error)
        {
            string diagnostic = StartupDiagnostics.Fail(error);
            if (window) App.ShowStartupFailure(error, diagnostic);
            else { try { Console.Error.WriteLine(error); } catch { } }
            return 1;
        }
    }

    // Keep early initialization/JIT failures inside the outer reporting boundary.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static int MainCore(string[] args)
    {
        if (args.Length == 3 && args[0] == DailySandboxSessions.ExportSwitch)
        {
            try { DailySandboxSessions.Export(args[1],args[2]); return 0; }
            catch { return 1; } // Never include credential payloads in a launch error.
        }
        if (args.Length == 4 && args[0] == "--create-update-delta")
        {
            try {
                var delta = DailyUpdateDeltaPackage.Create(args[1], args[2], args[3]);
                DailyJson.Write(Path.Combine(args[3], delta.FileName + ".json"), delta);
                return 0;
            }
            catch (Exception error) { Directory.CreateDirectory(args[3]); DailyJson.Write(Path.Combine(args[3], "delta-error.json"), new { error = error.ToString() }); return 1; }
        }
        if (args.Length == 2 && args[0] is "--apply-update" or "--recover-update") return DailyUpdateInstaller.Run(args[1], args[0] == "--recover-update");
        if (args.Length == 2 && args[0] == DailySandbox.LaunchSwitch)
        {
            try
            {
                var binding = DailySandbox.Bootstrap(args[1]);
                var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WorkingDirectory = Directory.GetCurrentDirectory() };
                var request = DailyJson.TryRead<DailySandboxRequest>(args[1])!;
                if (request.Worker is {} job)
                {
                    start.CreateNoWindow = true; start.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                    start.ArgumentList.Add("--parallel-worker"); start.ArgumentList.Add(Path.Combine(DailyParallelRuntime.WorkerDirectory(DailyIdentity.DataRoot, job.Id), "job.json"));
                }
                else { start.ArgumentList.Add(DailyDesktopLaunch.ChildSwitch); start.ArgumentList.Add("--inspect-account"); start.ArgumentList.Add(binding.Account); }
                using var child = System.Diagnostics.Process.Start(start) ?? throw new IOException("隔离窗口未启动。");
                return 0;
            }
            catch (Exception error) { DailyJson.Write(Path.Combine(DailyIdentity.DataRoot, "sandbox-error.json"), new DailySandboxError(DateTimeOffset.UtcNow, Path.GetFileName(args[1]), error.Message, error.ToString())); return 1; }
        }
        StartupDiagnostics.Mark("update-recovery");
        if (Dustweave.Accounts.SandboxProcessScope.CurrentBox.Length == 0 && DailyUpdateInstaller.RecoverBeforeStartup(DailyDesktopLaunch.Normalize(args))) return 0;
        if (DailyDesktopLaunch.NeedsRelay(args)) { StartupDiagnostics.Mark("explicit-desktop-relay"); DailyDesktopLaunch.Start(Environment.ProcessPath ?? throw new InvalidOperationException("无法定位日常助手"), args); return 0; }
        args = DailyDesktopLaunch.Normalize(args);
        if (args.Length==2 && args[0] is "--check-tool-languages" or "--check-tool-language-ui")
        {
            try { if(args[0]=="--check-tool-language-ui")HostedToolLocaleUiProbe.Run(Path.GetFullPath(args[1]));else HostedToolLocaleProbe.Run(Path.GetFullPath(args[1])); return 0; }
            catch(Exception error) { DailyJson.Write(Path.Combine(args[1],"result.json"),new {status="failed",error=error.ToString(),realGameTouched=false}); return 1; }
        }
        StartupDiagnostics.Mark("host-configuration");
        DailySuiteComposition.Configure(DailySuiteOwner.Inherits(args));
        if (args.Length == 2 && args[0] == "--parallel-worker")
        {
            try { return DailyParallelWorker.RunAsync(args[1]).GetAwaiter().GetResult(); }
            catch (Exception error) { DailyJson.Write(Path.Combine(DailyIdentity.DataRoot, "parallel-worker-error.json"), new { atUtc=DateTimeOffset.UtcNow, error=error.ToString() }); return 1; }
        }
        if (args.Length > 0 && args[0] == DailyConnectionAccess.HelperSwitch) return DailyConnectionAccess.RunHelperAsync(args).GetAwaiter().GetResult();
        if(args.Length==2&&args[0]=="--check-suite-host"){try{SuiteHostProbe.Run(args[1]);return 0;}catch(Exception e){DailyJson.Write(Path.Combine(args[1],"error.json"),new{error=e.ToString()});return 1;}}if(args.Length==3&&args[0]=="--check-suite"){try{DailySuiteComposition.Check(args[1],args[2]).GetAwaiter().GetResult();return 0;}catch(Exception e){DailyJson.Write(Path.Combine(args[2],"error.json"),new{error=e.ToString()});return 1;}} if (args.Length == 3 && args[0] == "--tool-menu-probe")
            return ToolMenuProbe.Run(args[1], args[2]);
        if (args.Length == 4 && args[0] == "--check-tool-client")
            return ToolApplicationHost.CheckClient(args[1], args[2], args[3]);
        if (args.Length >= 1 && args[0] == "--tool")
            return args.Length < 2 ? 2 : ToolApplicationHost.Run(args[1], args.Skip(2).ToArray());
        // The equipment tool re-enters its own executable for an isolated connection helper.
        if (args.Length >= 1 && args[0] == "--connection")
            return ToolApplicationHost.Run("equipment", args);
        if (args.Length >= 1 && args[0] == "--utility")
        {
            try
            {
                // WinExe has no console to reconfigure. Pipes still carry UTF-8 logs.
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError(), new System.Text.UTF8Encoding(false)) { AutoFlush = true });
                return RunUtility(args.Skip(1).ToArray()).GetAwaiter().GetResult();
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        StartupDiagnostics.Mark("application-resources");
        var app = new App { StartupArguments = args };
        app.InitializeComponent();
        return app.Run();
    }
    private static async Task<int> RunUtility(string[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException("Expected utility name");
        bool squareCheck = args.Length == 3 && args[0] == "diagnostics" && args[1] is "square-crossing" or "square-inspect" or "square-merchant";
        using var squareControl = squareCheck ? DailyToolControl.Acquire(DailyIdentity.DataRoot) : null;
        if (squareCheck)
        {
            var squareHost = new DailyGameHost();
            var squareGame = squareHost.Find() ?? throw new InvalidOperationException("游戏未运行，未启动或切换账号。");
            await squareHost.ConnectAsync(squareGame,Console.WriteLine,CancellationToken.None);
            await DailySuite.ActivateAsync(squareHost,DailyIdentity.DataRoot,"daily",Console.WriteLine,CancellationToken.None);
            await liveUtility::Dustweave.Connection.LiveEntry.RunAsync(["ready"],false,av=>liveUtility::Dustweave.Connection.LiveEntry.RunAsync(av,false));
        }
        string assemblyName = args[0] switch
        {
            "live" => "Dustweave.Connection",
            "minigame" => "Dustweave.ToolHost",
            "exporter" => "Dustweave.TableExporter",
            "diagnostics" => "Dustweave.RuntimeCheck",
            _ => throw new ArgumentException("Unknown daily utility: " + args[0])
        };
        if(args.Length>=2&&(args[0]=="live"&&args[1]=="ready"||args[0]=="minigame"&&(args[1]=="connect"||args[1]=="connect-sichuan"))){
            string id=args[0]=="live"?"daily":args[1]=="connect"?"fishing-daily":"sichuan-daily";
            var host=new DailyGameHost();
            await DailySuite.ActivateAsync(host,DailyIdentity.DataRoot,id,Console.WriteLine,CancellationToken.None);
            if(id!="daily"){
                var game=host.Find()!;var state=DailySuite.Read(DailyIdentity.DataRoot,game)!;
                var descriptor=new System.Diagnostics.ProcessStartInfo();DailySuite.SetToolEnvironment(descriptor,state,game,id);
                foreach(var variable in descriptor.Environment.Where(x=>x.Key.StartsWith("BD2_DAILY_",StringComparison.Ordinal)))Environment.SetEnvironmentVariable(variable.Key,variable.Value);
            }
        }
        var assembly = Assembly.Load(new AssemblyName(assemblyName));
        var entry = args[0] == "live"
            ? assembly.GetType("Dustweave.Connection.LiveEntry")!.GetMethod("RunAsync")!
            : assembly.EntryPoint ?? throw new MissingMethodException("Missing utility entry point: " + assemblyName);
        if (args.Length == 3 && args[1] == "--describe")
        {
            DailyJson.Write(args[2], new
            {
                tool = args[0],
                assembly = assembly.GetName().Name,
                protocol = DailyTools.Protocol,
                version = DailyIdentity.Version,
                realGameTouched = false
            });
            return 0;
        }
        object? result;
        try
        {
            result = args[0] == "live" ? liveUtility::Dustweave.Connection.LiveEntry.RunAsync(args.Skip(1).ToArray(), false) : entry.Invoke(null, [args.Skip(1).ToArray()]);
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
        if (result is Task<int> code)
            return await code;
        if (result is Task task)
            await task;
        return result is int exit ? exit : Environment.ExitCode;
    }
}
