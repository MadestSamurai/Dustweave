using System.IO;
using BD2ApostleDefense.Localization;
using System.Windows;
using BD2ApostleDefense.Compatibility;
namespace BD2ApostleDefense.Desktop;
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
 private Mutex? mutex;
 protected override void OnStartup(StartupEventArgs e)
 {
  DispatcherUnhandledException+=(_,args)=>ConnectionDiagnostics.Write(Identity.Root,"desktop.unhandled",error:args.Exception);
  AppDomain.CurrentDomain.UnhandledException+=(_,args)=>ConnectionDiagnostics.Write(Identity.Root,"process.unhandled",error:args.ExceptionObject as Exception);
  base.OnStartup(e);Ui.Catalog=new(LanguagePreference.Read(Identity.Root));
  if((hostedArguments ?? e.Args).Length==3&&(hostedArguments ?? e.Args)[0]=="--check-client")
  {try{var p=HookCompiler.Prepare((hostedArguments ?? e.Args)[1]);Directory.CreateDirectory((hostedArguments ?? e.Args)[2]);File.WriteAllBytes(Path.Combine((hostedArguments ?? e.Args)[2],Identity.Runtime+".dll"),p.Payload);JsonFiles.Write(Path.Combine((hostedArguments ?? e.Args)[2],"compatibility.json"),p.Report);Shutdown();}catch(Exception ex){JsonFiles.Write(Path.Combine((hostedArguments ?? e.Args)[2],"error.json"),new{error=ex.ToString()});Shutdown(1);}return;}
  if((hostedArguments ?? e.Args).Length==2&&(hostedArguments ?? e.Args)[0]=="--identity"){JsonFiles.Write((hostedArguments ?? e.Args)[1],new{version=AppVersion.Current,runtime=Identity.Runtime,fingerprint=HookCompiler.ToolFingerprint});Shutdown();return;}
  try
  {
   if((hostedArguments ?? e.Args).Length is 2 or 3&&(hostedArguments ?? e.Args)[0]=="--smoke"){string root=Path.GetFullPath((hostedArguments ?? e.Args)[1]);var fixture=(hostedArguments ?? e.Args).Length==3?(JsonFiles.Read<Snapshot>(Path.GetFullPath((hostedArguments ?? e.Args)[2]))??throw new InvalidOperationException("无法读取验收盘面")):null;new MainWindow(new DemoPort(fixture),root,true).Show();return;}
   if((hostedArguments ?? e.Args).Length==2&&(hostedArguments ?? e.Args)[0]=="--connection-smoke"){ShutdownMode=ShutdownMode.OnExplicitShutdown;_ = ConnectionSmoke(Path.GetFullPath((hostedArguments ?? e.Args)[1]));return;}
   if((hostedArguments ?? e.Args).Length!=0){Shutdown(2);return;}
   mutex=new Mutex(true,@"Local\BD2ApostleDefense-v1",out bool first);if(!first){MessageBox.Show(Ui.Text("工具已打开，请检查任务栏。"),Ui.Text("使徒防守助手"));Shutdown();return;}
   new MainWindow(new GamePort(),Identity.Root).Show();
  }catch(Exception ex){MessageBox.Show(Ui.Text(ex.Message),Ui.Text("启动失败"),MessageBoxButton.OK,MessageBoxImage.Error);Shutdown(1);}
 }
 private async Task ConnectionSmoke(string root)
 {
  Directory.CreateDirectory(root);
  try{var window=new MainWindow(new ConnectionSmokePort(root),root,connectionSmoke:true);await window.ConnectionSmoke(root);Shutdown();}
  catch(Exception e){JsonFiles.Write(Path.Combine(root,"connection-smoke.json"),new{status="failed",error=e.ToString()});Shutdown(1);}
 }
 protected override void OnExit(ExitEventArgs e){mutex?.Dispose();base.OnExit(e);}
}
