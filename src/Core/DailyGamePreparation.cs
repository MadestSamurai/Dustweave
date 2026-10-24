namespace Dustweave;

// Losing the process during connection preparation is a stopped operation, not
// a compiler failure. Propagate it so a caller cannot continue into its queue.
public sealed class DailyGameExitedException : OperationCanceledException
{
    public const string Notice = "游戏已关闭或重新启动，本次连接已停止。可以重新启动游戏后连接。";
    public DailyGameExitedException() : base(Notice) { }
}

public static class DailyGamePreparation
{
    internal static async Task<DailySnapshot> WaitForIdentityAsync(IGameHost host, GameInstance game,
        DailySnapshot? expected, Action<string> progress, CancellationToken token, string? diagnosticRoot = null, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
        bool reported = false;
        DailyIdentityRead read = new(null, "not-read");
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested(); RequireRunning(host, game);
                read = host.ReadIdentity(game);
                var value = read.Snapshot;
                if (value != null)
                {
                    if (value.ProcessId != game.ProcessId || value.ProcessStartTicks != game.StartTicks)
                        throw new DailyGameExitedException();
                    if (expected != null &&
                        (!string.IsNullOrEmpty(value.AccountKey) && value.AccountKey != expected.AccountKey ||
                         !string.IsNullOrEmpty(value.PlayerKey) && value.PlayerKey != expected.PlayerKey))
                        throw new InvalidOperationException("准备功能期间账号或连接变化，未启用组件。");
                    bool fresh = value.FrameUtcTicks >= DateTime.UtcNow.AddSeconds(-5).Ticks && value.FrameUtcTicks <= DateTime.UtcNow.AddSeconds(2).Ticks;
                    // Activation can replace a module; wait for the real native
                    // loading flag, not a scene name or a particular open page.
                    if (fresh && !value.Loading && value.Startup?.Visible != true)
                    {
                        if (value.State != "identified" || string.IsNullOrEmpty(value.AccountKey) || string.IsNullOrEmpty(value.PlayerKey))
                            throw new InvalidOperationException("suite.identity-unavailable: " + DailyLoginDiagnostics.WaitReason(read, game, DateTimeOffset.UtcNow));
                        return value;
                    }
                }
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException("suite.identity-unavailable: " + DailyLoginDiagnostics.WaitReason(read, game, DateTimeOffset.UtcNow));
                if (!reported) { progress("等待游戏加载完成"); reported = true; }
                await Task.Delay(200, token);
            }
        }
        catch
        {
            if(diagnosticRoot != null)
                DailyLoginDiagnostics.Record(diagnosticRoot, "preparation-failed", null, game, read.Snapshot, read.Problem, read.Detail);
            throw;
        }
    }
    internal static void RequireRunning(IGameHost host, GameInstance game)
    {
        if (!host.IsRunning(game)) throw new DailyGameExitedException();
    }

    // Only use for preparation with no game input/injection. Some compiler APIs
    // cannot cancel mid-call; they retain their compiler/cache lease until they
    // finish, but must not retain UI/control ownership or report late progress.
    public static async Task<T> RunAsync<T>(IGameHost host, GameInstance game,
        Func<Action<string>, CancellationToken, Task<T>> prepare, Action<string> progress,
        CancellationToken cancellation)
    {
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var progressGate = new object();
        bool reporting = true;
        Task<T>? pending = null;
        void Report(string message)
        {
            lock (progressGate)
                if (reporting && !lifetime.IsCancellationRequested) progress(message);
        }
        try
        {
            cancellation.ThrowIfCancellationRequested();
            RequireRunning(host, game);
            pending = prepare(Report, lifetime.Token);
            while (!pending.IsCompleted)
            {
                await Task.WhenAny(pending, Task.Delay(200, cancellation));
                cancellation.ThrowIfCancellationRequested();
                RequireRunning(host, game);
            }
            cancellation.ThrowIfCancellationRequested();
            RequireRunning(host, game);
            return await pending;
        }
        finally
        {
            lock (progressGate) reporting = false;
            lifetime.Cancel();
            if (pending == null || pending.IsCompleted)
            {
                _ = pending?.Exception;
                lifetime.Dispose();
            }
            else
            {
                _ = pending.ContinueWith(completed =>
                {
                    _ = completed.Exception;
                    lifetime.Dispose();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }
}
