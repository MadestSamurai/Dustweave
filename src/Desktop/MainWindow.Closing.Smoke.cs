using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    private async Task CheckClosingForSmoke()
    {
        if (sessions is not DemoEnvironment) throw new InvalidOperationException("Closing checks require isolated accounts.");
        var cases = new List<string>();
        string? package = Environment.GetEnvironmentVariable("DUSTWEAVE_UI_SMOKE_TOOL_PACKAGE");
        string[] modes = package == null ? ["idle", "completed-operation", "pending-operation"]
            : ["idle", "completed-operation", "pending-operation", "open-plugin", "closed-plugin"];
        foreach (string mode in modes)
        {
            string local = Path.Combine(root, "window-close", mode);
            var fixture = new DemoEnvironment(local);
            var window = new MainWindow(fixture, fixture, local, local, automatedSmoke: false)
            {
                ShowActivated = false, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = 0
            };
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int closingEvents = 0;
            window.Closing += (_, _) => closingEvents++;
            window.Closed += (_, _) => closed.TrySetResult();
            DailyToolPluginSession? plugin = null;
            try
            {
                window.Show();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                window.timer.Stop();
                TaskCompletionSource? pending = null;
                if (mode is "completed-operation" or "pending-operation")
                {
                    window.busy = true;
                    if (mode == "pending-operation") pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.operation = pending?.Task ?? Task.CompletedTask;
                }
                if (mode is "open-plugin" or "closed-plugin")
                {
                    var info = DailyPlugin.Inspect(package!);
                    if (!info.Available || !info.IsTool) throw new Exception("Offline tool fixture is unavailable");
                    plugin = await DailyToolPluginSession.StartAsync(local, info,
                        (_, _, _) => throw new Exception("Closing test must not request game access"));
                    window.toolPluginSessions[info.Id] = plugin;
                    if (mode == "closed-plugin")
                    {
                        plugin.Dispose();
                        for (int n = 0; n < 100 && plugin.Running; n++) await Task.Delay(20);
                        if (plugin.Running) throw new Exception("Fixture plugin did not exit");
                    }
                }
                window.Close();
                if (mode != "idle" && closed.Task.IsCompleted)
                    throw new Exception(mode + ": re-entered WPF Closing synchronously");
                pending?.SetResult();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (closingEvents != (mode == "idle" ? 1 : 2)) throw new Exception(mode + ": unexpected close transitions");
                if (window.toolPluginSessions.Count != 0) throw new Exception(mode + ": tool sessions retained after shutdown");
                if (plugin != null)
                {
                    for (int n = 0; n < 100 && plugin.Running; n++) await Task.Delay(20);
                    if (plugin.Running) throw new Exception(mode + ": tool child left running");
                }
                if (fixture.Calls.Count != 0) throw new Exception(mode + ": closing performed game/account operations");
                cases.Add(mode);
            }
            finally
            {
                plugin?.Dispose();
                if (!closed.Task.IsCompleted) { window.finalClose = true; window.Close(); }
            }
        }
        DailyJson.Write(Path.Combine(root, "window-close-checks.json"), new { cases, realGameTouched = false });
    }
}
