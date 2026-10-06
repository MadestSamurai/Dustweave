using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Dustweave.Desktop;

/// <summary>Offline integration fixture. Opens only a labelled test window, never a game connector.</summary>
internal static class ToolMenuProbe
{
    public static int Run(string root, string id)
    {
        using var lease = new DailyActivityLease(root);
        var app = new Application();
        var window = new Window { Title = "日常工具菜单离线检查 · " + id, Content = new TextBlock { Text = "离线测试窗口" }, Width = 240, Height = 100,
            ShowActivated = false, Left = -12000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual };
        window.Closing += (_, e) => e.Cancel = File.Exists(Path.Combine(root, "hold-close"));
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
        timer.Tick+=(_,_)=>{bool active=File.Exists(Path.Combine(root,"enable-"+id));if(active)lease.Enter();lease.Observe(active,true,true,File.Exists(Path.Combine(root,"pending-"+id)));};
        window.Closed+=(_,_)=>timer.Stop();
        window.Loaded += (_, _) => { timer.Start();File.WriteAllText(Path.Combine(root, "ready-" + id), "ready"); };
        return app.Run(window);
    }
    public static async Task CheckAsync(string root)
    {
        Directory.CreateDirectory(root);
        string profile = Path.Combine(root, "profile"), exe = Environment.ProcessPath!;
        Directory.CreateDirectory(profile);
        ProcessStartInfo Launch(DailyToolDefinition tool)
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory, CreateNoWindow = true };
            foreach (var arg in new[] { "--tool-menu-probe", profile, tool.Id }) start.ArgumentList.Add(arg);
            return start;
        }
        DailyToolSession Session() => new(profile, exe, Launch, TimeSpan.FromMilliseconds(400));
        var session = Session(); var cases = new List<string>();
        void Check(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); cases.Add(message); }
        async Task Ready(string id)
        {
            var watch = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(profile, "ready-" + id)))
            { if (watch.ElapsedMilliseconds > 6000) throw new TimeoutException("Probe failed to open"); await Task.Delay(30); }
        }
        try
        {
            await session.OpenAsync("fishing"); await Ready("fishing");
            var original = session.Current!;
            Check(original.ToolId == "fishing", "tool process recorded");
            Check(Session().Current == original, "menu restart recognizes open tool");
            Check(!DailyToolControl.IsOccupied(profile), "open settings window allows daily operation");
            File.WriteAllText(Path.Combine(profile,"enable-fishing"),"enabled");
            var until=DateTime.UtcNow.AddSeconds(3);while(!DailyToolControl.IsOccupied(profile)&&DateTime.UtcNow<until)await Task.Delay(25);
            bool locked = false; try { using var invalid = DailyToolControl.Acquire(profile); } catch (InvalidOperationException) { locked = true; }
            Check(locked, "daily operation blocked while tool holds lease");
            File.WriteAllText(Path.Combine(profile,"pending-fishing"),"pending");File.Delete(Path.Combine(profile,"enable-fishing"));await Task.Delay(1000);
            Check(DailyToolControl.IsOccupied(profile),"disabled tool retains pending receipt ownership");
            File.Delete(Path.Combine(profile,"pending-fishing"));until=DateTime.UtcNow.AddSeconds(3);while(DailyToolControl.IsOccupied(profile)&&DateTime.UtcNow<until)await Task.Delay(25);
            Check(!DailyToolControl.IsOccupied(profile)&&session.Current==original,"acknowledged stop unlocks with window still open");
            File.WriteAllText(Path.Combine(profile, "hold-close"), "hold");
            await session.OpenAsync("sichuan");
            Check(session.Current == original, "cancelled close preserves original tool");
            Check(!File.Exists(Path.Combine(profile, "ready-sichuan")), "switch does not launch before close");
            File.Delete(Path.Combine(profile, "hold-close"));
            await session.OpenAsync("sichuan"); await Ready("sichuan");
            Check(session.Current?.ToolId == "sichuan", "switch starts next tool after normal close");
            Check(session.Current?.ProcessId != original.ProcessId, "tools isolated into distinct processes");
            Check(await session.CloseAsync(), "normal close accepted");
            Check(session.Current == null, "closed tool clears active view");
            using (DailyToolControl.Acquire(profile)) Check(true, "daily can resume after close");
            // Probe an exit independent of the parent menu. Only this test-owned process is ended.
            await session.OpenAsync("rhythm"); await Ready("rhythm");
            var crashed = session.Current!;
            using (var owned = Process.GetProcessById(crashed.ProcessId)) { owned.Kill(); await owned.WaitForExitAsync(); }
            Check(session.Current == null, "unexpected tool exit cannot strand the menu");
            // Windows may signal termination before the final file-object cleanup completes.
            var releaseWatch = Stopwatch.StartNew();
            while (true)
            {
                try { using var released = DailyToolControl.Acquire(profile); Check(true, "process exit releases control"); break; }
                catch (InvalidOperationException) when (releaseWatch.ElapsedMilliseconds < 2000) { await Task.Delay(25); }
            }
            DailyJson.Write(Path.Combine(root, "validation.json"), new { status = "passed", count = cases.Count, cases, realGameTouched = false });
        }
        finally
        {
            File.Delete(Path.Combine(profile, "hold-close"));
            await session.CloseAsync();
        }
    }
}
