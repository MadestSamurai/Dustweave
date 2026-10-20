using System.ComponentModel;
using System.Windows;

namespace Dustweave.Desktop;

// A modeless tool window in the host process. Opening it never enables automation.
internal sealed class ApostleToolWindow : Window
{
    internal ApostleToolPanel Panel { get; }
    Task<bool>? closing;
    bool finished;
    internal Task? InitialConnection { get; private set; }
    internal ApostleToolWindow(Window owner, ApostleToolSession session, Func<bool> canStart)
    {
        Owner=owner; Width=1020; Height=880; MinWidth=860; MinHeight=650;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Icon=owner.Icon;
        Panel=new(session,canStart) { Margin=new Thickness(24,0,24,24) };
        Content=Panel;
        DailyDialogs.Prepare(this,toolWindow:true);
        DesktopWindowIdentity.Initialize(this);
        HostedToolPreparation.AfterFirstLayout(this,ConnectAfterLayoutAsync);
        Closing+=OnClosing;
        Closed+=(_,_)=>{finished=true;Panel.Dispose();};
    }
    async Task ConnectAfterLayoutAsync()
    {
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
