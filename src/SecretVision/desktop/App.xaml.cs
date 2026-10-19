using System.IO;
using System.Windows;
using BD2SecretVision.Compatibility;
using BD2SecretVision.Localization;
namespace BD2SecretVision.Desktop;
public partial class App : Application
{
 // Optional shared .NET launcher entry; standalone Main and normal startup remain unchanged.
 private string[]? hostedArguments;
 public static int RunHosted(string[] args, Action<Application>? configure = null)
 {
  var application = new App { hostedArguments = args };
  application.InitializeComponent(); configure?.Invoke(application);
  return application.Run();
 }
    private Mutex? mutex;
    public static string Version=>typeof(App).Assembly.GetName().Version!.ToString(3);
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try{
            if((hostedArguments ?? e.Args).Length==2&&(hostedArguments ?? e.Args)[0]=="--identity"){
                Files.Write((hostedArguments ?? e.Args)[1],new{version=Version,runtime="PublicRuntime4",fingerprint=HookCompiler.Fingerprint});Shutdown();return;
            }
            if((hostedArguments ?? e.Args).Length==3&&(hostedArguments ?? e.Args)[0]=="--check-client"){
                var hook=HookCompiler.Prepare((hostedArguments ?? e.Args)[1]);Files.Write(Path.Combine((hostedArguments ?? e.Args)[2],"compatibility.json"),hook.Report);
                Files.Write(Path.Combine((hostedArguments ?? e.Args)[2],"compile.json"),new{bytes=hook.Payload.Length,fingerprint=HookCompiler.Fingerprint,gameConnected=false});Shutdown();return;
            }
            if((hostedArguments ?? e.Args).Length==2&&(hostedArguments ?? e.Args)[0]=="--smoke") {new MainWindow(Path.GetFullPath((hostedArguments ?? e.Args)[1]),true).Show();return;}
            if((hostedArguments ?? e.Args).Length>0){Shutdown(2);return;}
            mutex=new Mutex(true,@"Local\BD2SecretVisionAssistant",out var first);
            if(!first){var language=new Catalog(new UserPreferences().DisplayLanguage);MessageBox.Show(language["single-instance"],language["title"]);Shutdown();return;}
            new MainWindow(Files.Root,false).Show();
        }catch(Exception error){
            if((hostedArguments ?? e.Args).Length>1){try{Files.Write(Path.Combine((hostedArguments ?? e.Args)[^1],"error.json"),new{error=error.ToString()});}catch{}Shutdown(1);return;}
            var language=new Catalog(new UserPreferences().DisplayLanguage);MessageBox.Show(language.Error(error),language["startup-error"]);Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e){mutex?.Dispose();base.OnExit(e);}
}
