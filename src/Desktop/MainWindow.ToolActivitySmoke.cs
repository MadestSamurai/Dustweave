using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    async Task CheckToolActivityForSmoke()
    {
        var checks=new List<string>();
        void Check(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);}
        foreach(var tool in DailyToolCatalog.All)
        {
            string profile=Path.Combine(root,"activity-fixture",tool.Id);
            var fixture=new DemoEnvironment(profile) { Game=new(42,123,"fixture.exe") };
            var state=new SuiteStatus {Tool=tool.Id,State="ready"};
            int denied=0;
            ManualResetEventSlim? readGate=null;
            var readEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var window=new ActivityFixtureWindow {Owner=this};window.Show();
            using var bridge=new HostedToolActivity(window,tool.Id,fixture,profile,()=>{
                if(readGate is {} gate){readEntered.TrySetResult();if(!gate.Wait(5000))throw new TimeoutException("fixture read wait");}
                state.At=DateTime.UtcNow.Ticks;return state;
            },_=>denied++);
            try
            {
                using(var other=DailyToolControl.Acquire(profile))
                {
                    await bridge.Poll();
                    Check(!window.Connect.IsEnabled&&window.Stop.IsEnabled&&window.IsEnabled,tool.Id+": competing connection disables commands, not pause or window");
                    // Also cover a tool timer locally re-enabling the button before the next poll.
                    window.Connect.IsEnabled=true;
                    window.Connect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(window.Connections==0&&denied==1,tool.Id+": conflicting connect rejected before the handler");
                    var auto=ToolApplicationHost.ConnectWhenIdle(window,window.Connect,false,profile);
                    await Task.Delay(100);
                    Check(!auto.IsCompleted&&window.Connections==0,tool.Id+": child waits for parent launch reservation");
                    other.Dispose();await bridge.Poll();await auto;
                }
                Check(window.Connections==1&&DailyToolControl.IsOccupied(profile),tool.Id+": connection acquires shared control");
                await Task.Delay(900);await bridge.Poll();
                Check(DailyToolControl.IsOccupied(profile),tool.Id+": slow connection retains control without game actions");
                window.HostedAutomationEnabled=false;state.Pending="pending response";
                await bridge.Poll();await Task.Delay(800);await bridge.Poll();
                Check(DailyToolControl.IsOccupied(profile),tool.Id+": pending settlement retains control");
                state.Pending="";await Task.Delay(850);await bridge.Poll();
                Check(!DailyToolControl.IsOccupied(profile)&&window.IsVisible,tool.Id+": settled idle window releases control");
                if(tool.Id=="fishing")
                {
                    using var gate=new ManualResetEventSlim();readGate=gate;
                    var observation=bridge.Poll();await readEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    window.Connect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(900);gate.Set();await observation;readGate=null;await bridge.Poll();
                    Check(DailyToolControl.IsOccupied(profile),"new connection cannot be unlocked by an older slow observation");
                }
                window.Connect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));fixture.Game=null;
                await bridge.Poll();
                Check(!DailyToolControl.IsOccupied(profile),tool.Id+": process exit releases control");
            }
            finally {window.Close();}
        }
        DailyJson.Write(Path.Combine(smoke!,"tool-activity-checks.json"),new{status="passed",count=checks.Count,checks,realGameTouched=false});
    }
    sealed class ActivityFixtureWindow : Window
    {
        internal Button Connect=new(){Name="ConnectButton",Content="fixture connect"};
        internal Button Stop=new(){Name="StopButton",Content="fixture stop"};
        internal int Connections;
        public bool HostedAutomationEnabled {get;set;}
        internal ActivityFixtureWindow()
        {
            Width=250;Height=120;ShowInTaskbar=false;ShowActivated=false;Opacity=0;
            NameScope.SetNameScope(this,new NameScope());RegisterName(Connect.Name,Connect);RegisterName(Stop.Name,Stop);
            var panel=new StackPanel();panel.Children.Add(Connect);panel.Children.Add(Stop);Content=panel;
            Connect.Click+=(_,_)=>{Connections++;HostedAutomationEnabled=true;};
            Stop.Click+=(_,_)=>HostedAutomationEnabled=false;
        }
    }
}
