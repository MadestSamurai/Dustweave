using System.IO;
using System.Text.Json;
using System.Windows;
namespace BD2Rhythm.Desktop;
public partial class App:Application {
 // Optional shared .NET launcher entry; standalone Main and normal startup remain unchanged.
 private string[]? hostedArguments;
 public static int RunHosted(string[] args, Action<Application>? configure = null)
 {
  var application = new App { hostedArguments = args };
  application.InitializeComponent(); configure?.Invoke(application);
  return application.Run();
 }
 Mutex? single;
 protected override async void OnStartup(StartupEventArgs e){base.OnStartup(e);
  if((hostedArguments ?? e.Args).Length==2&&(hostedArguments ?? e.Args)[0]=="--identity"){File.WriteAllText((hostedArguments ?? e.Args)[1],JsonSerializer.Serialize(new{version="0.2.1",runtime=RhythmIdentity.RuntimeName,defaultJitterMs=50,chartSource="live-game",compatibility="local-interface-adaptation",toolFingerprint=Compatibility.HookCompiler.ToolFingerprint,languages=new[]{"zh-CN","en-US"}}));Shutdown();return;}
  if((hostedArguments ?? e.Args).Length==3&&(hostedArguments ?? e.Args)[0]=="--check-client"){try{var p=await Task.Run(()=>Compatibility.HookCompiler.Prepare((hostedArguments ?? e.Args)[1]));File.WriteAllText((hostedArguments ?? e.Args)[2],JsonSerializer.Serialize(p.Report));Shutdown();}catch(Exception ex){File.WriteAllText((hostedArguments ?? e.Args)[2],ex.ToString());Shutdown(1);}return;}
  if((hostedArguments ?? e.Args).Length==2&&(hostedArguments ?? e.Args)[0]=="--smoke"){try{Directory.CreateDirectory((hostedArguments ?? e.Args)[1]);var w=new RhythmWindow(Path.Combine(Path.GetFullPath((hostedArguments ?? e.Args)[1]),"isolated"));MainWindow=w;await w.SmokeAsync((hostedArguments ?? e.Args)[1]);Shutdown();}catch(Exception ex){File.WriteAllText(Path.Combine((hostedArguments ?? e.Args)[1],"failure.txt"),ex.ToString());Shutdown(1);}return;}
  single=new Mutex(true,"Local\\BD2Rhythm.Private.Desktop",out bool first);if(!first){MessageBox.Show(new Localization.LanguageCatalog(Localization.LanguagePreference.Read(RhythmIdentity.DataRoot)).Text("音游工具已经打开，请使用现有窗口。"),"BD2 Rhythm");Shutdown();return;}MainWindow=new RhythmWindow();MainWindow.Show();
 }
 protected override void OnExit(ExitEventArgs e){single?.Dispose();base.OnExit(e);}
}
