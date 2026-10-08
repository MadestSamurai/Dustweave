using System.Diagnostics;
using System.Runtime.InteropServices;
using Dustweave.Accounts;

namespace Dustweave;

public sealed class DailyParallelRuntime(string executable) : IDailyParallelRuntime
{
    public Task StartAsync(DailyAccount account, DailyParallelJob job, CancellationToken token)
        => DailySandbox.LaunchAsync(account, executable, _ => { }, token, job);
    public static string WorkerDirectory(string root, string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("parallel.invalid_job");
        return Path.Combine(root, "parallel-workers", id);
    }
    public static string HostDirectory(DailyParallelJob job)
    {
        job.Validate();
        return WorkerDirectory(Path.Combine(DailySandbox.Store, DailySandbox.BoxName(job.Account), "root", "user", "current", "AppData", "Local", "BD2DailyAssistant"), job.Id);
    }
    public DailyParallelStatus? Read(DailyParallelJob job) => DailyJson.TryRead<DailyParallelStatus>(Path.Combine(HostDirectory(job), "status.json"));
    public bool Alive(DailyParallelStatus status)
    {
        try
        {
            using var process = Process.GetProcessById(status.ProcessId);
            return process.StartTime.ToUniversalTime().Ticks == status.ProcessStartTicks && !process.HasExited && SandboxProcessScope.BoxOf(process.Id) == DailySandbox.BoxName(status.Account);
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
    public bool Busy(DailyParallelJob job)
    {
        var all = Process.GetProcessesByName("Dustweave");
        try { return all.Any(p => !p.HasExited && SandboxProcessScope.BoxOf(p.Id) == DailySandbox.BoxName(job.Account)); }
        finally { foreach (var process in all) process.Dispose(); }
    }
    public void Send(DailyParallelJob job, string action, long sequence)
    {
        string directory = HostDirectory(job);
        if (!Directory.Exists(directory)) return;
        DailyJson.Write(Path.Combine(directory, "control.json"), new DailyParallelCommand(job.Id, job.Account, action, sequence, DateTimeOffset.UtcNow));
    }
    public void ShowGame(DailyParallelItem item)
    {
        if (item.Status is not { GameId: > 0 } status) throw new InvalidOperationException("parallel.game_missing");
        using var game = Process.GetProcessById(status.GameId);
        if (game.StartTime.ToUniversalTime().Ticks != status.GameStartTicks || SandboxProcessScope.BoxOf(game.Id) != DailySandbox.BoxName(item.Account.AccountKey)) throw new InvalidOperationException("parallel.identity_changed");
        if (game.MainWindowHandle == 0) throw new InvalidOperationException("parallel.game_missing");
        ShowWindowAsync(game.MainWindowHandle, 9); SetForegroundWindow(game.MainWindowHandle);
    }
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
}
