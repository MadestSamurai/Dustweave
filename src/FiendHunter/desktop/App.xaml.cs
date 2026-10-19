using System.IO;
using System.Text.Json;
using BD2FiendHunter.Compatibility;
using BD2FiendHunter.Localization;
using System.Windows;
namespace BD2FiendHunter.Desktop;
public partial class App:Application {
 // Optional shared .NET launcher entry; standalone Main and normal startup remain unchanged.
 private string[]? hostedArguments;
 public static int RunHosted(string[] args, Action<Application>? configure = null)
 {
  var application = new App { hostedArguments = args };
  application.InitializeComponent(); configure?.Invoke(application);
  return application.Run();
 }
 Mutex? singleton;
 protected override void OnStartup(StartupEventArgs e){base.OnStartup(e);
  if((hostedArguments ?? e.Args).Length==2&&(hostedArguments ?? e.Args)[0]=="--identity"){File.WriteAllText((hostedArguments ?? e.Args)[1],JsonSerializer.Serialize(new{version=typeof(App).Assembly.GetName().Version!.ToString(3),runtime="PublicRuntime1",fingerprint=HookCompiler.Fingerprint}));Shutdown();return;}
  if((hostedArguments ?? e.Args).Length==2&&(hostedArguments ?? e.Args)[0]=="--check-client"){try{var prepared=HookCompiler.Prepare((hostedArguments ?? e.Args)[1]);File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"compatibility-check.json"),JsonSerializer.Serialize(prepared.Report));Shutdown();}catch(Exception ex){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"compatibility-check.json"),JsonSerializer.Serialize(new{Error=ex.ToString()}));Shutdown(1);}return;}
  if((hostedArguments ?? e.Args).Length==2&&(hostedArguments ?? e.Args)[0]=="--smoke"){
   var w=new MainWindow(true){ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-12000,Top=0};MainWindow=w;
   w.Loaded+=(_,_)=>{try{w.Smoke(Path.GetFullPath((hostedArguments ?? e.Args)[1]));Shutdown();}catch(Exception ex){Directory.CreateDirectory((hostedArguments ?? e.Args)[1]);File.WriteAllText(Path.Combine((hostedArguments ?? e.Args)[1],"error.txt"),ex.ToString());Shutdown(1);}};w.Show();return;
  }
  singleton=new Mutex(true,@"Local\BD2FiendHunterDesktop",out bool first);if(!first){MessageBox.Show(new Catalog(Catalog.LoadLanguage())["duplicate"]);Shutdown();return;}
  MainWindow=new MainWindow();MainWindow.Show();
 }
 protected override void OnExit(ExitEventArgs e){singleton?.Dispose();base.OnExit(e);}
}
