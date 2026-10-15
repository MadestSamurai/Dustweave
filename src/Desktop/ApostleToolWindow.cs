using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace Dustweave.Desktop;

// A modeless tool window in the host process. Opening it never enables automation.
internal sealed class ApostleToolWindow : Window
{
    internal ApostleToolPanel Panel { get; }
    Task<bool>? closing;
    bool finished, rendered;
    internal Task? InitialConnection { get; private set; }
    internal ApostleToolWindow(Window owner, ApostleToolSession session, Func<bool> canStart)
    {
        Owner=owner; Width=1020; Height=880; MinWidth=860; MinHeight=650;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Icon=owner.Icon;
        DailyLanguage.Current.Bind(this,TitleProperty,()=>"Dustweave · "+DailyLanguage.Current.Get("tool.apostle-defense"));
        Panel=new(session,canStart) { Margin=new Thickness(24,16,24,16) };
        Content=Panel;
        DailyDialogs.Prepare(this,toolWindow:true);
        DesktopWindowIdentity.Initialize(this);
        void UpdateInsets(object? sender,EventArgs args)=>MainWindow.UpdateWindowInsets(this,(FrameworkElement)Content);
        SourceInitialized+=UpdateInsets;StateChanged+=UpdateInsets;
        SizeChanged+=(_,_)=>MainWindow.UpdateWindowInsets(this,(FrameworkElement)Content);
        ContentRendered+=OnRendered;
        Closing+=OnClosing;
        Closed+=(_,_)=>{finished=true;Panel.Dispose();};
    }
    async void OnRendered(object? sender, EventArgs args)
    {
        if(rendered)return;
        rendered=true;
        // Let the window finish its first frame and release OpenTool's short reservation.
        await Dispatcher.Yield(DispatcherPriority.Background);
        if(finished || closing!=null)return;
        InitialConnection=Panel.ConnectAsync();
        await InitialConnection;
    }
    async void OnClosing(object? sender, CancelEventArgs args)
    {
        if(finished)return;
        args.Cancel=true;
        await CloseSafelyAsync();
    }
    internal Task<bool> CloseSafelyAsync() => closing ??= StopAndCloseAsync();
    async Task<bool> StopAndCloseAsync()
    {
        try
        {
            // Force an asynchronous boundary so repeated close clicks share this task.
            await Task.Yield();
            if(!await Panel.StopAsync())return false;
            finished=true;Close();return true;
        }
        finally { closing=null; }
    }
    internal void BringForward()
    {
        if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;
        Show();Activate();
    }
}
