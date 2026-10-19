using System.IO;
using BD2Sichuan.Localization;
using System.Reflection;
using System.Text.Json;
using System.Windows;
namespace BD2Sichuan.Desktop;
public partial class App:Application
{
 // Optional shared .NET launcher entry; standalone Main and normal startup remain unchanged.
 private string[]? hostedArguments;
 public static int RunHosted(string[] args, Action<Application>? configure = null)
 {
  var application = new App { hostedArguments = args };
  application.InitializeComponent(); configure?.Invoke(application);
  return application.Run();
 }
    private Mutex? single;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if((hostedArguments ?? e.Args).Length==2&&(hostedArguments ?? e.Args)[0]=="--identity")
        {File.WriteAllText((hostedArguments ?? e.Args)[1],JsonSerializer.Serialize(new{runtime=SichuanIdentity.RuntimeName,toolFingerprint=BD2Sichuan.Compatibility.HookCompiler.ToolFingerprint,compatibility="local-interface-adaptation",defaultIntervalMs=1000}));Shutdown();return;}
        if((hostedArguments ?? e.Args).Length==3&&(hostedArguments ?? e.Args)[0]=="--check-client")
        {
            try { var result=await Task.Run(()=>BD2Sichuan.Compatibility.HookCompiler.Prepare((hostedArguments ?? e.Args)[1]));File.WriteAllText((hostedArguments ?? e.Args)[2],JsonSerializer.Serialize(result.Report));Shutdown(); }
            catch(Exception ex){File.WriteAllText((hostedArguments ?? e.Args)[2],JsonSerializer.Serialize(new{Status="unsupported",Error=ex.ToString(),Injection=false}));Shutdown(1);}return;
        }
        if((hostedArguments ?? e.Args).Length==2&&(hostedArguments ?? e.Args)[0]=="--smoke")
        {
            try{Directory.CreateDirectory((hostedArguments ?? e.Args)[1]);var window=new SichuanWindow(Path.Combine(Path.GetFullPath((hostedArguments ?? e.Args)[1]),"isolated",Guid.NewGuid().ToString("N")));MainWindow=window;await window.SmokeAsync((hostedArguments ?? e.Args)[1]);Shutdown(0);}
            catch(Exception ex){File.WriteAllText(Path.Combine((hostedArguments ?? e.Args)[1],"failure.txt"),ex.ToString());Shutdown(1);}return;
        }
        single=new Mutex(true,"Local\\BD2Sichuan.Desktop",out bool first);
        if(!first){var catalog=new LanguageCatalog(LanguagePreference.Read(SichuanIdentity.BoardRoot));MessageBox.Show(catalog.Text("连连看工具已经打开，请使用现有窗口。"),catalog.Text("BD2 连连看"));Shutdown();return;}
        var main=new SichuanWindow();MainWindow=main;main.Show();
    }
    protected override void OnExit(ExitEventArgs e){single?.Dispose();base.OnExit(e);}
}
