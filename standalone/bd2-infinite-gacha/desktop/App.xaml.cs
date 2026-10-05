using System.IO;
using BD2InfiniteGacha.Localization;
using System.Windows;
using BD2InfiniteGacha.Compatibility;
namespace BD2InfiniteGacha.Desktop;
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
        base.OnStartup(e);var locale=new LanguageCatalog(LanguagePreference.Read(Identity.Root));
        if((hostedArguments ?? e.Args).Length==3&&(hostedArguments ?? e.Args)[0]=="--check-client")
        {try{var p=HookCompiler.Prepare((hostedArguments ?? e.Args)[1]);Directory.CreateDirectory((hostedArguments ?? e.Args)[2]);File.WriteAllBytes(Path.Combine((hostedArguments ?? e.Args)[2],Identity.Runtime+".dll"),p.Payload);JsonFiles.Write(Path.Combine((hostedArguments ?? e.Args)[2],"compatibility.json"),p.Report);Shutdown();}catch(Exception ex){JsonFiles.Write(Path.Combine((hostedArguments ?? e.Args)[2],"error.json"),new{error=ex.ToString()});Shutdown(1);}return;}
        if((hostedArguments ?? e.Args).Length==2&&(hostedArguments ?? e.Args)[0]=="--identity"){JsonFiles.Write((hostedArguments ?? e.Args)[1],new{version=ApplicationInfo.Version,runtime=Identity.Runtime,fingerprint=HookCompiler.Fingerprint});Shutdown();return;}
        try{
            if((hostedArguments ?? e.Args).Length==2&&(hostedArguments ?? e.Args)[0]=="--startup-smoke"){
                string root=Path.GetFullPath((hostedArguments ?? e.Args)[1]);Directory.CreateDirectory(root);
                // Exercise the real disconnected transport, not the demo port.
                var window=new MainWindow(new GamePort(root),root,false);
                window.Loaded+=(_,_)=>{JsonFiles.Write(Path.Combine(root,"startup.json"),new{status="passed",windowVisible=window.IsVisible,realTransport=true,lease=BD2.LocalIpc.DesktopFiles.HasLease(root)});window.Close();};
                window.Show();return;
            }
            if((hostedArguments ?? e.Args).Length==3&&(hostedArguments ?? e.Args)[0]=="--snapshot-smoke"){
                var snapshot=JsonFiles.Read<Snapshot>((hostedArguments ?? e.Args)[1])??throw new InvalidDataException("Snapshot missing");
                string root=Path.GetFullPath((hostedArguments ?? e.Args)[2]);var window=new MainWindow(new DemoPort(snapshot),root,false);
                window.Loaded+=async(_,_)=>{await window.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);window.SnapshotSmoke();};
                window.Show();return;
            }
            if((hostedArguments ?? e.Args).Length==2&&(hostedArguments ?? e.Args)[0]=="--notification-smoke"){
                var root=Path.GetFullPath((hostedArguments ?? e.Args)[1]);var window=new MainWindow(new DemoPort(),root,false);
                window.Loaded+=async(_,_)=>{await window.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);window.NativeNotificationSmoke();};
                window.Show();return;
            }
            if((hostedArguments ?? e.Args).Length==2&&(hostedArguments ?? e.Args)[0]=="--smoke"){string root=Path.GetFullPath((hostedArguments ?? e.Args)[1]);new MainWindow(new DemoPort(),root,true).Show();return;}
            if((hostedArguments ?? e.Args).Length!=0){Shutdown(2);return;}
            mutex=new Mutex(true,@"Local\BD2InfiniteGacha-v1",out bool first);if(!first){MessageBox.Show(locale.Text("工具已打开，请检查任务栏。"),locale.Text("无限抽抽乐助手"));Shutdown();return;}
            new MainWindow(new GamePort(),Identity.Root,false).Show();
        }catch(Exception ex){MessageBox.Show(locale.Text(ex.Message),locale.Text("启动失败"),MessageBoxButton.OK,MessageBoxImage.Error);Shutdown(1);}
    }
    protected override void OnExit(ExitEventArgs e){mutex?.Dispose();base.OnExit(e);}
}
