namespace Dustweave;

// A manual quote read owns the same tool lease as a queue, but never starts a
// queue or clears its stop/progress files. Identity observation alone does not
// install the native read configuration; prepare must acknowledge that first.
public static class DailyTradeCaptureConnection
{
    public static async Task<T> RunAsync<T>(string root, IGameHost host, string? expectedAccount,
        Func<Task> prepare, Func<GameInstance, string, Task<T>> capture)
    {
        using var control = DailyToolControl.Acquire(root);
        var game = host.Find() ?? throw new InvalidOperationException("游戏已退出，请重新连接日常工具。");
        DailySnapshot Identity()
        {
            var snapshot = host.ReadSnapshot();
            if (host.Find() != game)
                throw new InvalidOperationException("游戏进程已变化，请重新连接。");
            if (!DailyIdentityGuard.Ready(snapshot, game, DateTimeOffset.UtcNow))
                throw new InvalidOperationException("suite.identity-unavailable");
            if (expectedAccount != null && snapshot!.AccountKey != expectedAccount)
                throw new InvalidOperationException("游戏登录的账号与目标账号不一致，未读取商店。");
            return snapshot!;
        }
        var before = Identity();
        var phase = "prepare";
        var state = "failed";
        string error = "";
        var started = DateTimeOffset.UtcNow;
        void SameIdentity()
        {
            var current = Identity();
            if (current.AccountKey != before.AccountKey || current.PlayerKey != before.PlayerKey
                || current.Guild?.ServerKey != before.Guild?.ServerKey || current.Guild?.CycleKey != before.Guild?.CycleKey)
                throw new InvalidOperationException("连接期间账号已切换或每日周期变化，未采用旧商店数据。");
        }
        try
        {
            await prepare();
            SameIdentity();
            phase = "capture";
            var result = await capture(game, before.AccountKey);
            SameIdentity();
            state = "ready";
            return result;
        }
        catch (Exception failure) { error = failure.ToString(); throw; }
        finally
        {
            try { DailyJson.Write(Path.Combine(root, "trade", "diagnostics", Guid.NewGuid().ToString("N") + ".json"),
                new { atUtc = DateTimeOffset.UtcNow, engine = "dotnet-trade-capture-connection-v1", operation = "capture-connection", phase, state, error,
                    elapsedSeconds = (DateTimeOffset.UtcNow - started).TotalSeconds, game.ProcessId, game.StartTicks, account = before.AccountKey,
                    transport = "local-named-pipe", gameplayActions = 0 }); }
            catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException) { }
        }
    }
}
