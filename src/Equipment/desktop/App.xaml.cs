using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
namespace BD2Equipment;
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
 Mutex? instance;
 protected override void OnStartup(StartupEventArgs e){base.OnStartup(e);ShutdownMode=ShutdownMode.OnExplicitShutdown;
  if((hostedArguments ?? e.Args).Length>0&&(hostedArguments ?? e.Args)[0]=="--connection"){RunConnection((hostedArguments ?? e.Args).Skip(1).ToArray());return;}
  if((hostedArguments ?? e.Args).Length==2&&((hostedArguments ?? e.Args)[0]=="--smoke"||(hostedArguments ?? e.Args)[0]=="--smoke-en")){
   L.Select((hostedArguments ?? e.Args)[0]=="--smoke-en"?"en-US":"zh-CN");var window=new MainWindow{ShowActivated=false,ShowInTaskbar=false,Left=-12000,Top=0,WindowStartupLocation=WindowStartupLocation.Manual};
   window.Loaded+=async(_,_)=>{try{await window.Smoke(Path.GetFullPath((hostedArguments ?? e.Args)[1]));Shutdown();}catch(Exception ex){Directory.CreateDirectory((hostedArguments ?? e.Args)[1]);File.WriteAllText(Path.Combine((hostedArguments ?? e.Args)[1],"smoke.json"),new JsonObject{["status"]="failed",["error"]=ex.ToString(),["realGameTouched"]=false}.ToJsonString());Shutdown(1);}};window.Show();return;
  }
  if((hostedArguments ?? e.Args).Length==2 && (hostedArguments ?? e.Args)[0] is "--capture-check" or "--runtime-check") { RunDiagnostic((hostedArguments ?? e.Args)[0],Path.GetFullPath((hostedArguments ?? e.Args)[1]));return; }
  L.Initialize();instance=new Mutex(true,@"Local\BD2EquipmentAssistant-v1",out bool first);if(!first){MessageBox.Show(L.T("装备助手已经打开，请查看任务栏。"),L.T("BD2 装备助手"));Shutdown();return;}
  var main=new MainWindow((hostedArguments ?? e.Args).Contains("--refine"));MainWindow=main;ShutdownMode=ShutdownMode.OnMainWindowClose;main.Show();
 }
 async void RunConnection(string[] args){try{Console.SetOut(new StreamWriter(Console.OpenStandardOutput(),new System.Text.UTF8Encoding(false)){AutoFlush=true});Console.SetError(new StreamWriter(Console.OpenStandardError(),new System.Text.UTF8Encoding(false)){AutoFlush=true});await BD2Equipment.Live.LiveEntry.RunAsync(args,configureConsole:false);Shutdown(Environment.ExitCode);}catch(Exception ex){Console.Error.WriteLine(ex);Shutdown(1);}}
 async void RunDiagnostic(string mode,string folder){
  Directory.CreateDirectory(folder);
  try{
   JsonObject result;
   if(mode=="--capture-check"){
    var captured=await new WorkerClient().Run(new(){["operation"]="capture"});
    result=new(){["status"]="passed",["equipmentCount"]=captured["gear"]!.AsArray().Count,["resourcesSpent"]=false,["realGameTouched"]=true};
   }else result=new(){["status"]="passed",["bridgeChecks"]=await Task.Run(()=>WorkerClient.Invoke(["self-test"])),["realGameTouched"]=false};
   result["engine"]="C# in-process";await File.WriteAllTextAsync(Path.Combine(folder,"diagnostic.json"),result.ToJsonString());Shutdown();
  }catch(Exception ex){await File.WriteAllTextAsync(Path.Combine(folder,"diagnostic.json"),new JsonObject{["status"]="failed",["error"]=ex.ToString()}.ToJsonString());Shutdown(1);}
 }
 protected override void OnExit(ExitEventArgs e){instance?.Dispose();base.OnExit(e);}
}
