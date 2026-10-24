using System.IO;
namespace Dustweave.Desktop;

public partial class MainWindow
{
    internal string CaptureSessionId { get; init; }="";
    private bool captureClosing;
    private async void CheckCaptureLifetime()
    {
        if(CaptureSessionId.Length==0||captureClosing||DailyLoadingExperiment.Active()?.Id==CaptureSessionId)return;
        captureClosing=true;
        try{
            operationPreparation?.Cancel();coordinator.Stop();dailyQueue.Stop();guild.Stop();
            if(operation!=null)try{await operation;}catch{}
            if(ToolOpen)await CloseToolWindowsAsync();
        }catch(Exception error){try{DailyJson.Write(Path.Combine(root,"capture-close-error.json"),new{error=error.ToString()});}catch{}}
        finally{finalClose=true;timer.Stop();Close();}
    }
    internal async Task StartLoadingExperimentAsync(string path)
    {
        string output=Path.GetDirectoryName(Path.GetFullPath(path))!;
        try{
            if(Unavailable)throw new InvalidOperationException("织尘正在执行其他操作，请结束本组后重试。");
            var request=DailyLoadingExperimentLaunch.Read(path);
            var target=DailyLoadingExperimentLaunch.Resolve(request,sessions.Read().Accounts);
            DailySandbox.RequireHost();DailyLoadingExperimentLaunch.RequireNoGames();
            bool started=false;
            await OperateAsync(async()=>{
                started=true;
                using var watch=new CancellationTokenSource();
                using var launch=CancellationTokenSource.CreateLinkedTokenSource(operationPreparation?.Token??CancellationToken.None);
                using var stop=launch.Token.Register(()=>Dispatcher.BeginInvoke(new Action(coordinator.Stop)));
                var observer=Task.Run(async()=>{
                    try{while(!watch.IsCancellationRequested){await Task.Delay(250,watch.Token);
                        if(DailyLoadingExperiment.Active()?.Id!=request.SessionId){launch.Cancel();return;}}
                    }catch(OperationCanceledException){}
                });
                bool verified=false;
                void Progress(DailyProgress p){if(p.State=="completed")verified=true;DailyLoadingExperimentLaunch.Report(output,p.State=="error"?"failed":"connecting",p.Message);}
                coordinator.Progress+=Progress;
                try{
                    DailyLoadingExperimentLaunch.RequireActive(output,request);
                    await DailyLoadingExperimentLaunch.DispatchAsync(request,target,
                        async a=>{await coordinator.InspectFreshAsync([a]);if(!verified)throw new OperationCanceledException("账号启动与连接没有完成，本组记录已保留。");},
                        async a=>{await DailySandbox.LaunchAsync(a,Environment.ProcessPath!,m=>DailyLoadingExperimentLaunch.Report(output,"connecting",m),launch.Token,captureSessionId:request.SessionId);});
                    launch.Token.ThrowIfCancellationRequested();
                    if(DailyLoadingExperiment.Active()?.Id==request.SessionId)
                        DailyLoadingExperimentLaunch.Report(output,"launched",DailyLoadingExperimentLaunch.Isolated(request.Mode)?"游戏已隔离启动，隔离窗口会自动连接；请等待进入主菜单。":"账号启动与连接已完成，请按实验路线操作游戏。");
                }catch(Exception error){DailyLoadingExperimentLaunch.TryFail(path,error);throw;}
                finally{coordinator.Progress-=Progress;watch.Cancel();await observer;}
            },connectsGame:true);
            if(!started)throw new InvalidOperationException("本组启动未能取得工具控制权，请结束本组，退出正在使用的织尘后重试。");
        }catch(Exception error){DailyLoadingExperimentLaunch.TryFail(path,error);ShowError(error);}
    }
}
