using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;

namespace Dustweave.Desktop;

/// <summary>Runs each original WPF application in a separate process, sharing the bundled runtime.</summary>
internal static class ToolApplicationHost
{
    private static void ConfigureWindow(Application app,string id) {
        app.Startup+=(_,_)=>app.Dispatcher.BeginInvoke(new Action(()=>{
            var window=app.MainWindow;
            if(window==null)return;
            _ = new HostedToolLocaleBridge(app);
            _ = new HostedToolActivity(window,id);
            if(window.FindName("ConnectButton") is System.Windows.Controls.Button button){
                if(id!="equipment"){button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));button.Visibility=Visibility.Collapsed;}
                else {button.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty,"SuiteRefresh");app.Resources["SuiteRefresh"]=DailyLanguage.Current.Get("tools.refresh_inventory");}
            }
        }),System.Windows.Threading.DispatcherPriority.Loaded);
    }
    internal static string Version(DailyToolDefinition tool) => Assembly.Load(new AssemblyName(tool.AssemblyName)).GetName().Version!.ToString(3);
    public static int CheckClient(string id, string managed, string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            if (id != "fiend-hunter") throw new ArgumentException("Unsupported client check adapter");
            var compiler = Assembly.Load("BD2FiendHunter.Compatibility").GetType("BD2FiendHunter.Compatibility.HookCompiler")!;
            var prepared = compiler.GetMethod("Prepare")!.Invoke(null, new object[] { managed })!;
            var report = prepared.GetType().GetProperty("Report")!.GetValue(prepared);
            DailyJson.Write(Path.Combine(output, "compatibility.json"), report);
            return 0;
        }
        catch (Exception e) { DailyJson.Write(Path.Combine(output, "error.json"), new { error = e.ToString(), realGameTouched = false }); return 1; }
    }
    public static int Run(string id, string[] arguments)
    {
        if (!DailyToolArguments.TryClassify(id, arguments, out bool helper)) return 2;
        var tool = DailyToolCatalog.Find(id);
        IDisposable? languageScope = null;
        try
        {
            if (!helper) DailyLanguage.Current.Initialize(DailyIdentity.DataRoot);
            if (!helper && Environment.GetEnvironmentVariable("BD2_DAILY_HOSTED_TOOL")!=id)throw new InvalidOperationException("请从日常助手的工具菜单打开，以共用游戏连接。");
            if (!helper) languageScope = HostedToolLocale.Begin(DailyLanguage.Current.Code);
            var assembly = Assembly.Load(new AssemblyName(tool.AssemblyName));
            var typeName = tool.Id == "equipment" ? "BD2Equipment.App" : tool.AssemblyName + ".Desktop.App";
            var entry = assembly.GetType(typeName)!.GetMethod("RunHosted", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException("工具缺少统一启动入口，请使用完整版本。");
            Action<Application>? setup=!helper&&!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BD2_DAILY_HOSTED_TOOL")) ? app=>ConfigureWindow(app,id) : null;
            return (int)entry.Invoke(null, new object?[] { arguments,setup })!;
        }
        catch (Exception error)
        {
            var cause = error is TargetInvocationException { InnerException: { } inner } ? inner : error;
            DailyJson.Write(Path.Combine(DailyIdentity.DataRoot, "tools", id + "-startup-error.json"),
                new { atUtc = DateTimeOffset.UtcNow, tool = id, error = cause.ToString() });
            if (!helper) MessageBox.Show(DailyLanguage.Current.Diagnostic(cause.Message, DailyUserText.Error(cause, DailyLanguage.Current.Translate)), DailyLanguage.Current.Get("tools.start_error_title"), MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
        finally { languageScope?.Dispose(); }
    }
}



