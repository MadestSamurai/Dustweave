using System.Diagnostics;
using System.IO;
using Dustweave.Accounts;

namespace Dustweave.Desktop;

// No second UI or alternate automation implementation. The same coordinator,
// ownership lease, packaged executor and durable queue run within the bound box.
internal static class DailyParallelWorker
{
    public static async Task<int> RunAsync(string path)
    {
        var job = DailyJson.TryRead<DailyParallelJob>(path) ?? throw new InvalidDataException("parallel.invalid_job"); job.Validate();
        SandboxProcessScope.Require(DailySandbox.BoxName(job.Account)); DailySandbox.RequireBoundAccount(job.Account);
        string directory = DailyParallelRuntime.WorkerDirectory(DailyIdentity.DataRoot, job.Id);
        if (!Path.GetFullPath(path).Equals(Path.Combine(directory, "job.json"), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("parallel.invalid_job");
        using var process = Process.GetCurrentProcess();
        string state = "connecting", detail = "parallel.connecting"; QueueView? view = null; GameInstance? instance = null;
        var stateGate = new object();
        void Publish()
        {
            lock (stateGate) DailyJson.Write(Path.Combine(directory, "status.json"), new DailyParallelStatus(job.Id, job.Account, state, detail, DateTimeOffset.UtcNow,
                process.Id, process.StartTime.ToUniversalTime().Ticks, instance?.ProcessId ?? 0, instance?.StartTicks ?? 0, view));
        }
        void State(string value, string message = "") { lock (stateGate) { state = value; detail = message; } Publish(); }
        using var mutex = new Mutex(true, DailyApplication.InstanceMutex, out bool first);
        if (!first) { State("failed", "parallel.window_open"); return 1; }
        using var sessions = new AccountSessions();
        var host = new DailyGameHost();
        var ownedGame = job.StartedGame ? host.Find() : null;
        var coordinator = new DailyCoordinator(sessions, host, DailyIdentity.DataRoot, onVerified: token => DailySuite.ActivateAsync(host, DailyIdentity.DataRoot, "daily", _ => { }, token));
        var queue = new DailyQueueSession(DailyIdentity.DataRoot, new PackagedDailyQueueExecutor(AppContext.BaseDirectory));
        coordinator.Progress += p => { lock (stateGate) detail = p.Message; };
        queue.Progress += v => { lock (stateGate) view = v; };
        using var monitorStop = new CancellationTokenSource();
        var control = new DailyParallelLease(job.Id, job.Account, DateTimeOffset.UtcNow);
        var monitor = Task.Run(async () =>
        {
            while (!monitorStop.IsCancellationRequested)
            {
                try
                {
                    var command = DailyJson.TryRead<DailyParallelCommand>(Path.Combine(directory, "control.json"));
                    control.Observe(command, DateTimeOffset.UtcNow);
                    if (control.Stopped || control.Paused) { queue.Stop(); coordinator.Stop(); }
                    instance = host.Find(); Publish();
                }
                catch (Exception error) { control.Stop(); lock (stateGate) detail = error.Message; queue.Stop(); coordinator.Stop(); }
                try { await Task.Delay(500, monitorStop.Token); } catch (OperationCanceledException) { }
            }
        });
        string outcome = "failed";
        try
        {
            while (!control.Authorized && !control.Stopped) await Task.Delay(100);
            if (control.Stopped) throw new OperationCanceledException("parallel.parent_lost");
            using var owner = DailyToolControl.Acquire(DailyIdentity.DataRoot);
            new DailyPreferenceStore(DailyIdentity.DataRoot).Save(job.Account, DailyPreferences.Parse(job.Preferences));
            var target = sessions.Read().Accounts.SingleOrDefault(a => a.Valid && a.AccountKey == job.Account) ?? throw new InvalidOperationException("parallel.account_missing");
            bool resume = false;
            while (!control.Stopped)
            {
                if (control.Paused)
                {
                    State("paused", "parallel.paused");
                    while (control.Paused && !control.Stopped) await Task.Delay(200);
                    if (control.Stopped) break;
                }
                try
                {
                    State("connecting", "parallel.connecting");
                    await coordinator.InspectAsync([target]);
                    if (control.Stopped || control.Paused) continue;
                    State("running");
                    var result = await queue.RunAsync(job.Account, resume: resume);
                    lock (stateGate) { view = result; detail = result.Message; }
                    if (control.Stopped) break;
                    if (control.Paused || result.State == "paused")
                    {
                        control.Paused = true; resume = result.Record.Length > 0; continue;
                    }
                    outcome = result.State == "completed" ? "completed" : "partial"; break;
                }
                catch (OperationCanceledException) when (control.Paused || control.Stopped) { }
            }
            if (control.Stopped) outcome = control.ParentLost ? "interrupted" : "stopped";
        }
        catch (Exception error) { outcome = control.Stopped ? (control.ParentLost ? "interrupted" : "stopped") : "failed"; lock (stateGate) detail = error.Message; }
        finally
        {
            queue.Stop();
            try { coordinator.Stop(); } catch { }
            // A paused or failed task preserves the game for diagnosis. Close only
            // the exact process this launch created after a successful completion.
            if (outcome == "completed" && job.CloseGame && ownedGame != null)
            {
                State("closing", "parallel.closing");
                try { await host.CloseAsync(ownedGame, CancellationToken.None); instance=host.Find(); }
                catch (Exception error) { outcome = "partial"; lock (stateGate) detail = error.Message; }
            }
            monitorStop.Cancel(); await monitor;
            if (outcome == "interrupted") detail = "parallel.parent_lost";
            State(outcome, detail);
        }
        return outcome == "completed" ? 0 : 1;
    }

}
