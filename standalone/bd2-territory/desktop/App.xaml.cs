using System.IO;
using System.Text.Json;
using System.Windows;
using BD2Territory.Compatibility;
namespace BD2Territory.Desktop;
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
 private Mutex? single;
 protected override async void OnStartup(StartupEventArgs e)
 {
  DispatcherUnhandledException+=(_,args)=>TerritoryDiagnostics.Write(TerritoryIdentity.DataRoot,"desktop.unhandled",error:args.Exception);
  AppDomain.CurrentDomain.UnhandledException+=(_,args)=>TerritoryDiagnostics.Write(TerritoryIdentity.DataRoot,"process.unhandled",error:args.ExceptionObject as Exception);
  base.OnStartup(e);ShutdownMode=ShutdownMode.OnExplicitShutdown;
  try
  {
   if((hostedArguments ?? e.Args).Length==2&&(hostedArguments ?? e.Args)[0]=="--identity")
   {
    File.WriteAllText((hostedArguments ?? e.Args)[1],JsonSerializer.Serialize(new{version=typeof(App).Assembly.GetName().Version!.ToString(3),languages=new[]{"zh-CN","en-US"},automaticCooking=true,automaticSurplusSales=true,defaultAutoSell=true,defaultSellThreshold=9900,minSellThreshold=100,maxSellThreshold=9900,defaultCooking=false,defaultUseNavMesh=false,defaultNavigation="astar",defaultCookingBatch=100,runtime=TerritoryIdentity.RuntimeName,toolFingerprint=HookCompiler.ToolFingerprint,compatibility="local-interface-adaptation",recipe="活力面疙瘩",dynamicRecipes=true,fixedCropPlanting=true,adaptiveGrid=true,navigationCache=true,layoutImport=true,layoutTemplates=3,phasedWorkCycle=true,dynamicPlantingBatch=true,plantingBatchMode="native-preview",requiresConnectedFarm=false,ratio="5:3:2",defaultIntervalMs=500,defaultPlantingBudget=1400,automaticStart=false}));Shutdown();return;
   }
   if((hostedArguments ?? e.Args).Length==3&&(hostedArguments ?? e.Args)[0]=="--check-client")
   {
    try{var prepared=await Task.Run(()=>HookCompiler.Prepare((hostedArguments ?? e.Args)[1]));File.WriteAllText((hostedArguments ?? e.Args)[2],JsonSerializer.Serialize(prepared.Report));Shutdown();}
    catch(Exception ex){File.WriteAllText((hostedArguments ?? e.Args)[2],JsonSerializer.Serialize(new{Status="unsupported",Error=ex.ToString(),Injection=false}));Shutdown(1);}return;
   }
   if((hostedArguments ?? e.Args).Length==2&&((hostedArguments ?? e.Args)[0]=="--smoke"||(hostedArguments ?? e.Args)[0]=="--smoke-en"))
   {
    if((hostedArguments ?? e.Args)[0]=="--smoke-en")System.Globalization.CultureInfo.CurrentUICulture=new System.Globalization.CultureInfo("en-US");
    var output=Path.GetFullPath((hostedArguments ?? e.Args)[1]);Directory.CreateDirectory(output);
    try{var window=new TerritoryWindow(Path.Combine(output,"isolated",Guid.NewGuid().ToString("N")));MainWindow=window;await window.SmokeAsync(output);Shutdown();}
    catch(Exception ex){File.WriteAllText(Path.Combine(output,"failure.txt"),ex.ToString());Shutdown(1);}return;
   }
   if((hostedArguments ?? e.Args).Length==2&&(hostedArguments ?? e.Args)[0]=="--connection-smoke")
   {
    var output=Path.GetFullPath((hostedArguments ?? e.Args)[1]);Directory.CreateDirectory(output);
    try{var window=new TerritoryWindow(Path.Combine(output,"isolated",Guid.NewGuid().ToString("N")));MainWindow=window;await window.ConnectionSmokeAsync(output);Shutdown();}
    catch(Exception ex){File.WriteAllText(Path.Combine(output,"failure.txt"),ex.ToString());Shutdown(1);}return;
   }
   single=new Mutex(true,"Local\\BD2Territory.Desktop",out bool first);
   if(!first){MessageBox.Show(new BD2Territory.Localization.LanguageCatalog().Text("领地工具已经打开，请使用现有窗口。"),"BD2 Territory");Shutdown();return;}
   ShutdownMode=ShutdownMode.OnMainWindowClose;MainWindow=new TerritoryWindow();MainWindow.Show();
  }
  catch(Exception ex){MessageBox.Show(new BD2Territory.Localization.LanguageCatalog().Text(ex.GetBaseException().Message),new BD2Territory.Localization.LanguageCatalog().Text("BD2 领地启动失败"),MessageBoxButton.OK,MessageBoxImage.Error);Shutdown(1);}
 }
 protected override void OnExit(ExitEventArgs e){single?.Dispose();base.OnExit(e);}
}

