using BD2AccountSessionManager;
namespace BD2Daily;

// Both account registry writes and the game must run in Explorer's desktop context.
// A single relay prevents inherited development-host registry views from splitting them.
public static class DailyDesktopLaunch
{
    public const string ChildSwitch = "--desktop-session";
    public static bool NeedsRelay(string[] args) => args.Length == 0 ||
        args.Length == 1 && args[0] == "--launch-desktop" ||
        args.Length == 2 && args[0] is "--run-selected" or "--inspect-account";
    public static string[] ChildArguments(string[] args) =>
        args.Length == 1 && args[0] == "--launch-desktop" ? [ChildSwitch] : [ChildSwitch, ..args];
    public static string[] Normalize(string[] args) => args.Length > 0 && args[0] == ChildSwitch ? args[1..] : args;
    public static void Start(string executable, string[] args)
    {
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable) || !Path.GetFileName(executable).Equals("BD2DailyAssistant.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请使用正式日常助手的桌面启动入口。");
        DesktopGameLaunch.Launch(executable, string.Join(" ",ChildArguments(args).Select(Quote)), Directory.GetCurrentDirectory());
    }
    internal static string Quote(string value)
    {
        var output=new System.Text.StringBuilder("\"");int slashes=0;
        foreach(char c in value){if(c=='\\'){slashes++;continue;}output.Append('\\',c=='"'?slashes*2+1:slashes);output.Append(c);slashes=0;}
        output.Append('\\',slashes*2);return output.Append('"').ToString();
    }
}
