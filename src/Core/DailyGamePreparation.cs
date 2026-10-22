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
