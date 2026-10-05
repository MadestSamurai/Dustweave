extern alias liveUtility;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
namespace BD2Daily.Desktop;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (DailyDesktopLaunch.NeedsRelay(args)) { DailyDesktopLaunch.Start(Environment.ProcessPath ?? throw new InvalidOperationException("无法定位日常助手"), args); return 0; }
        args = DailyDesktopLaunch.Normalize(args);
        if (args.Length==2 && args[0] is "--check-tool-languages" or "--check-tool-language-ui")
        {
            try { if(args[0]=="--check-tool-language-ui")HostedToolLocaleUiProbe.Run(Path.GetFullPath(args[1]));else HostedToolLocaleProbe.Run(Path.GetFullPath(args[1])); return 0; }
            catch(Exception error) { DailyJson.Write(Path.Combine(args[1],"result.json"),new {status="failed",error=error.ToString(),realGameTouched=false}); return 1; }
        }
        DailySuiteComposition.Configure();
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
        var app = new App { StartupArguments = args };
        app.InitializeComponent();
        return app.Run();
    }
    private static async Task<int> RunUtility(string[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException("Expected utility name");
        bool squareCheck = args.Length == 3 && args[0] == "diagnostics" && args[1] is "square-crossing" or "square-inspect";
        using var squareControl = squareCheck ? DailyToolControl.Acquire(DailyIdentity.DataRoot) : null;
        if (squareCheck)
        {
            var squareHost = new DailyGameHost();
            var squareGame = squareHost.Find() ?? throw new InvalidOperationException("游戏未运行，未启动或切换账号。");
            await squareHost.ConnectAsync(squareGame,Console.WriteLine,CancellationToken.None);
            await DailySuite.ActivateAsync(squareHost,DailyIdentity.DataRoot,"daily",Console.WriteLine,CancellationToken.None);
            await liveUtility::BD2Daily.Live.LiveEntry.RunAsync(["ready"],false,av=>liveUtility::BD2Daily.Live.LiveEntry.RunAsync(av,false));
        }
        string assemblyName = args[0] switch
        {
            "live" => "BD2Daily.Live",
            "minigame" => "BD2Daily.Fishing",
            "exporter" => "BD2TableExporter",
            "diagnostics" => "BD2Daily.RuntimeCheck",
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
            ? assembly.GetType("BD2Daily.Live.LiveEntry")!.GetMethod("RunAsync")!
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
            result = args[0] == "live" ? liveUtility::BD2Daily.Live.LiveEntry.RunAsync(args.Skip(1).ToArray(), false) : entry.Invoke(null, [args.Skip(1).ToArray()]);
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



