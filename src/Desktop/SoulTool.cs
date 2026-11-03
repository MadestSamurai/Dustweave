using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dustweave.Soul;
namespace Dustweave.Desktop;

internal static class SoulTool
{
    internal static int Run(string[] args)
    {
        if(args.Length==2&&args[0]=="--identity"){File.WriteAllText(args[1],"Dustweave Soul · "+DailyProductVersion.Current);return 0;}
        if(args.Length==3&&args[0]=="--check-client"){SoulSession.PrepareOffline(DailyIdentity.DataRoot,Path.Combine(args[1],"Assembly-CSharp.dll"),args[2]);return 0;}
        var app=new App{StartupArguments=["--soul-window",..args]};app.InitializeComponent();return app.Run();
    }
    internal static void Start(Application app,string[] args,bool verify=false)
    {
        bool smoke=args.Length==2&&args[0]=="--smoke";
        string root=smoke?Path.Combine(Path.GetFullPath(args[1]),"isolated"):DailyIdentity.DataRoot;
        DailyLanguage.Current.Initialize(root);var theme=new DailyTheme(root);app.Exit+=(_,_)=>theme.Dispose();
        if(!smoke){
            var single=new Mutex(false,verify?DailyApplication.InstanceMutex:DailyToolCatalog.Find("soul").MutexName);
            bool acquired;try{acquired=single.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
            if(!acquired){single.Dispose();throw new IOException("Dustweave is already running; close it before verification.");}
            app.Exit+=(_,_)=>{single.ReleaseMutex();single.Dispose();};
        }
        var window=new SoulToolWindow(root);app.MainWindow=window;
        if(smoke){
            window.ShowInTaskbar=false;window.ShowActivated=false;window.Left=-12000;window.Top=0;window.WindowStartupLocation=WindowStartupLocation.Manual;
            window.Loaded+=async(_,_)=>{
                var folder=Path.GetFullPath(args[1]);Directory.CreateDirectory(folder);
                foreach(var language in DailyLanguage.Codes)foreach(var appearance in new[]{DailyAppearance.Light,DailyAppearance.Dark}){
                    DailyLanguage.Current.Select(language);theme.Select(appearance);
                    window.Width=720;window.Height=580;
                    window.Render(new("planning","", "Preview",13,7,10,4,52,2,17,1,220,"",DecisionPhase:"combinations",EvaluatedPlans:762,DecisionSeconds:4.3,ProposedCandidates:1000,CandidateTarget:3000,SearchWorkers:1));
                    window.UpdateLayout();await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                    window.CheckSettingsForSmoke(root);
                    var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(Path.Combine(folder,$"soul-{language}-{appearance}.png")))encoder.Save(output);
                }
                DailyJson.Write(Path.Combine(folder,"result.json"),new{status="passed",realGameTouched=false,languages=3,themes=2});await window.CloseSafelyAsync();app.Shutdown();
            };
        }else if(verify){
            if(args.Length is not (2 or 3)||args.Length==3&&args[2]!="--resume")throw new ArgumentException("Expected account name, output folder and optional --resume.");
            window.Loaded+=async(_,_)=>{
                try{
                    var host=new DailyGameHost(root);var game=host.Find();
                    if(game is null||host.ReadIdentity(game).Snapshot?.PlayerName!=args[0]){
                        using var sessions=new AccountSessions(host);
                        var profiles=new DailyProfiles(root).Read();
                        var keys=profiles.Where(p=>p.PlayerName==args[0]).Select(p=>p.AccountKey).ToHashSet();
                        var target=sessions.Read().Accounts.Single(a=>a.Valid&&(a.Name==args[0]||keys.Contains(a.AccountKey)));
                        var coordinator=new DailyCoordinator(sessions,host,root);
                        coordinator.Progress+=p=>window.Render(window.Session.Status with{State="connecting",Message=p.Message});
                        await host.PrepareAccessAsync(m=>window.Render(window.Session.Status with{State="connecting",Message=m}),CancellationToken.None);
                        await coordinator.InspectAsync([target]);
                    }
                }catch(Exception error){Directory.CreateDirectory(args[1]);DailyJson.Write(Path.Combine(args[1],"account-preparation-error.json"),new{error=error.ToString()});await window.CloseSafelyAsync();app.Shutdown(1);return;}
                await window.StartAsync(args.Length==3,args[0],args[1]);
                var status=window.Session.Status;
                DailyJson.Write(Path.Combine(args[1],"verification.json"),new{status=status.State=="complete"?"passed":"failed",automationFinished=status.State is "complete" or "defeated",outcome=status.State=="complete"?"cleared":status.State=="defeated"?"defeated":"interrupted",account=args[0],result=status,realGameTouched=true});
                await window.CloseSafelyAsync();app.Shutdown(status.State=="complete"?0:1);
            };
        }
        window.Show();
    }
}
