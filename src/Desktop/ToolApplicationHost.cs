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
            if(DailySuite.PreparesInWindow(id)){
                // Owned tools install their shared chrome after this decorator, too.
                // Keep the first frame independent of connection and compilation.
                var root=DailyIdentity.DataRoot;
                _ = new HostedToolPreparation(window,id,root,(progress,cancel)=>HostedToolPreparation.PrepareAsync(id,root,progress,cancel),()=>ConnectReadyWindow(window,id));
                DailyDialogs.Prepare(window,toolWindow:true);
                return;
            }
            ConnectReadyWindow(window,id);
        }),System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private static void ConnectReadyWindow(Window window,string id){
            _ = new HostedToolActivity(window,id);
            if(window.FindName("ConnectButton") is System.Windows.Controls.Button button){
                if(id=="equipment"){button.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty,"SuiteRefresh");Application.Current.Resources["SuiteRefresh"]=DailyLanguage.Current.Get("tools.refresh_inventory");}
                _ = ConnectWhenIdle(window,button,id is not ("equipment" or "infinite-gacha" or "fiend-hunter"));
            }
    }
    internal static async Task ConnectWhenIdle(Window window,System.Windows.Controls.Button button,bool hideAfterConnect, string? dataRoot=null)
    {
        // The parent owns the control lease briefly while recording the new child process.
        for(int i=0;i<60&&window.IsLoaded;i++) {
            if(window.IsEnabled&&!DailyToolControl.IsOccupied(dataRoot??DailyIdentity.DataRoot)) {
                var request=new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent);
                button.RaiseEvent(request);
                if(hideAfterConnect&&!request.Handled)button.Visibility=Visibility.Collapsed;
                return;
            }
            await Task.Delay(50);
        }
    }
    internal static string Version(DailyToolDefinition tool) => tool.Id is "equipment" or "apostle-defense" or "infinite-gacha" or "mansion-runaway" or "pandora" or "fiend-hunter" ? DailyProductVersion.Current : Assembly.Load(new AssemblyName(tool.AssemblyName)).GetName().Version!.ToString(3);
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
            if(id=="mansion-runaway")return MansionTool.Run(arguments,helper?null:app=>ConfigureWindow(app,id));
            if(id=="pandora")return PandoraTool.Run(arguments,helper?null:app=>ConfigureWindow(app,id));
            var assembly = Assembly.Load(new AssemblyName(tool.AssemblyName));
            var typeName = tool.Id == "equipment" ? "BD2Equipment.App" : tool.AssemblyName + ".Desktop.App";
            var entry = assembly.GetType(typeName)!.GetMethod("RunHosted", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException("工具缺少统一启动入口，请使用完整版本。");
            Action<Application>? setup=!helper&&!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BD2_DAILY_HOSTED_TOOL")) ? app=>ConfigureWindow(app,id) : null;
            if(id is "equipment" or "infinite-gacha" or "fiend-hunter") { var activitySetup=setup; setup=app=>{HostedToolPresentation.Install(app,deferChrome:activitySetup!=null);activitySetup?.Invoke(app);}; }
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
