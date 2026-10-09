using Dustweave.Accounts;
namespace Dustweave;

// Opening the UI must not depend on Explorer COM. Game launches still use the
// validated desktop route; the explicit relay remains available for developer hosts.
public static class DailyDesktopLaunch
{
    public const string ChildSwitch = "--desktop-session";
    public static bool NeedsRelay(string[] args) => args.Length == 1 && args[0] == "--launch-desktop";
    public static string[] ChildArguments(string[] args) =>
        args.Length == 1 && args[0] == "--launch-desktop" ? [ChildSwitch] : [ChildSwitch, ..args];
    public static string[] Normalize(string[] args) => args.Length > 0 && args[0] == ChildSwitch ? args[1..] : args;
    public static void Start(string executable, string[] args)
    {
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable) || !DailyApplication.IsExecutable(executable))
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
