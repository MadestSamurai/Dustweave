extern alias apostle;
extern alias toolBD2ApostleDefense;
using A = apostle::BD2ApostleDefense;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async Task CheckApostleForSmoke()
    {
        L.Audit();
        var fake = new ApostleSmokePort(); int prepares = 0;
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool preparedAfterPaint=false;
        ApostleToolWindow? window=null;
        var session = new ApostleToolSession(root, async (progress,token) => {
            prepares++;
            preparedAfterPaint=await Dispatcher.InvokeAsync(()=>window is {IsVisible:true,IsLoaded:true});
            progress("apostle.reading");await ready.Task.WaitAsync(token);return fake;
        });
        session.SaveSettings(new() { IntervalMs=350,StopAfterRound=true });
        window=new ApostleToolWindow(this,session,()=>!Unavailable);
        apostleWindow=window;
        var view=window.Panel;
        view.ActivityChanged+=()=>{RefreshTools();SetBusy(busy);};
        window.Closed+=(_,_)=>{if(apostleWindow==window)apostleWindow=null;RefreshTools();SetBusy(busy);};
        // Reproduce a busy animated tool starving Background priority. Loading
        // must start after layout even while input-priority work keeps arriving.
        bool keepInputBusy=true;
        void InputWork(){if(keepInputBusy)Dispatcher.BeginInvoke((Action)InputWork,DispatcherPriority.Input);}
        InputWork();WorkspaceTabs.SelectedItem=ToolsTab;window.Show();
        try{for(int i=0;i<100&&!preparedAfterPaint;i++)await Task.Delay(50);}
        finally{keepInputBusy=false;}
        if(!preparedAfterPaint || fake.Writes!=0 || !DailyToolControl.IsOccupied(root))throw new Exception("Preparation must start after painting and reserve control without actions");
        view.CheckLoadingForSmoke();
        if(ConnectButton.IsEnabled || AccountsGrid.IsEnabled || ConnectionGuideButton.IsEnabled || !window.IsEnabled)throw new Exception("Connection conflicts must be locked without freezing the tool window");
        Capture("apostle-loading",(FrameworkElement)window.Content);
        ready.SetResult();await window.InitialConnection!;
        if(!session.Connected || session.Running || fake.Writes!=0 || DailyToolControl.IsOccupied(root) || !ConnectButton.IsEnabled)throw new Exception("Connecting alone must release ownership without enabling automation");
        Capture("apostle-ready",(FrameworkElement)window.Content);
        window.WindowState=WindowState.Maximized;
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        if(((FrameworkElement)window.Content).Margin.Left<=0)throw new Exception("Maximized tool content was not inset from the native frame");
        Capture("apostle-maximized",(FrameworkElement)window.Content);
        window.WindowState=WindowState.Normal;
        await session.StartAsync(session.LoadSettings());
        if(!session.Running)throw new Exception(session.Message);
        foreach(int language in new[]{0,1,2})foreach(int appearance in new[]{1,2})
        {
            LanguageSelector.SelectedIndex=language;ThemeSelector.SelectedIndex=appearance;
            window.Width=language==2?900:1020;window.Height=language==2?720:880;
            await session.PollAsync();view.Render();
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            view.CheckForSmoke();
            CheckToolChromeForSmoke(window,"apostle-defense",(value,message)=>{if(!value)throw new Exception(message);});
            if(!session.Running || prepares!=1 || session.LoadSettings().IntervalMs!=350)throw new Exception("Appearance change modified execution/preferences");
            if(Application.Current.Windows.Count!=2 || window.Owner!=this)throw new Exception("Tool must keep its own owned window");
            Capture($"apostle-{language}-{appearance}",(FrameworkElement)window.Content);
        }
        WorkspaceTabs.SelectedItem=RunTab;await session.PollAsync();
        if(!session.Running)throw new Exception("Changing host pages stopped execution");
        window.WindowState=WindowState.Minimized;window.BringForward();
        if(window.WindowState!=WindowState.Normal || Application.Current.Windows.Count!=2)throw new Exception("Reopening must restore the existing window");
        if(!await view.StopAsync() || DailyToolControl.IsOccupied(root))throw new Exception("Pause did not release control");
        view.CheckForSmoke();
        view.CheckInvalidSettingsForSmoke();
        await session.StartAsync(session.LoadSettings());
        WorkspaceTabs.SelectedItem=ToolsTab;
        if(!await window.CloseSafelyAsync() || session.Running || DailyToolControl.IsOccupied(root))throw new Exception("Closing active window must stop and release safely");
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        if(WorkspaceTabs.SelectedItem!=ToolsTab)throw new Exception("Closing a tool unexpectedly changed the selected host page");
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancelled=new ApostleToolSession(root,async(_,token)=>{entered.SetResult();await Task.Delay(Timeout.Infinite,token);return fake;});
        var cancelledWindow=new ApostleToolWindow(this,cancelled,()=>true);cancelledWindow.Show();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));cancelledWindow.Panel.CheckLoadingForSmoke();
        if(!await cancelledWindow.CloseSafelyAsync() || cancelled.Starting || cancelled.Occupied || cancelled.Connected)throw new Exception("Closing during preparation must cancel without starting automation");
        int retries=0,writesBeforeRetry=fake.Writes;
        using var retry=new ApostleToolSession(root,(_,_)=>++retries==1?throw new IOException("fixture connection failure"):Task.FromResult<IApostleToolPort>(fake));
        var retryWindow=new ApostleToolWindow(this,retry,()=>true);retryWindow.Show();
        for(int i=0;i<100&&retryWindow.InitialConnection==null;i++)await Task.Delay(30);
        await (retryWindow.InitialConnection??throw new Exception("Tool preparation never started"));
        retryWindow.Panel.CheckDisconnectedForSmoke();
        await Task.Delay(300);
        if(retries!=1||retry.Occupied||fake.Writes!=writesBeforeRetry)throw new Exception("Failure must release control without retrying or enabling gameplay");
        await retryWindow.Panel.ConnectAsync();
        if(!retry.Connected||retry.Running||retry.Occupied||retries!=2||fake.Writes!=writesBeforeRetry)throw new Exception("Manual retry must connect once without enabling automation");
        await retryWindow.CloseSafelyAsync();
        if(Application.Current.Windows.Count!=1)throw new Exception("Closed tool windows leaked");
        DailyJson.Write(Path.Combine(smoke!,"apostle-checks.json"),new{status="passed",ownedWindow=true,preparedAfterPaint,prepares,sharedLoadingAnimation=true,busyDispatcherStartsPreparation=true,manualRetryWithoutAutomation=true,themeAndLanguagePreserved=true,pausedWithoutClosing=true,connectWithoutAutomation=true,connectionControlsLocked=true,closeCancelsPreparation=true,closeStopsAutomation=true,invalidInputPreserved=true,realGameTouched=false});
    }
    private sealed class ApostleSmokePort : IApostleToolPort
    {
        readonly toolBD2ApostleDefense::BD2ApostleDefense.Desktop.DemoPort demo = new();
        public int Writes;
        public bool Alive=>true;public bool Ready=>true;public bool Pending=>false;
        public A.GameProcess? Find()=>demo.Find();public A.Snapshot? Read()=>demo.Read();
        public void Write(A.Control value){Writes++;demo.Write(value);}
        public Task ConnectAsync(Action<string> progress,CancellationToken token)=>Task.CompletedTask;
    }
}
