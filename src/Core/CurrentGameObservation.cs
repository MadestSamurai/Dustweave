namespace Dustweave;

// Standalone read-only clients must not own account switching, revoke a daily lease,
// perform startup UI actions, or write account profiles just to attach an observer.
public static class CurrentGameObservation
{
    public static async Task ConnectAsync(IAccountSessions sessions, IGameHost host, Action<string> progress, CancellationToken token, TimeSpan? timeout = null, TimeSpan? interval = null)
    {
        var game = host.Find() ?? throw new InvalidOperationException("请先启动游戏。");
        await host.ConnectAsync(game, progress, token);
        await WaitForIdentityAsync(sessions, host, game, token, timeout, interval);
    }

    public static async Task<string> WaitForIdentityAsync(IAccountSessions sessions, IGameHost host, GameInstance game,
        CancellationToken token, TimeSpan? timeout = null, TimeSpan? interval = null)
    {
        string key = sessions.Read().CurrentKey;
        var guard = new DailyIdentityGuard();
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (host.Find() != game)
                throw new InvalidOperationException("连接期间游戏进程已改变，请重新连接。");
            var status = host.ReadStatus();
            if (status?.ProcessId == game.ProcessId && status.ProcessStartTicks == game.StartTicks && status.State == "error")
                throw new InvalidOperationException("组件启动失败：" + status.Error);
            var snapshot = host.ReadSnapshot();
            var now = DateTimeOffset.UtcNow;
            DailyLoginReadiness.Check(snapshot, game, now.UtcTicks);
            var local = sessions.Read().CurrentKey;
            if (DailyProfiles.ValidKey(local))
            {
                if (DailyProfiles.ValidKey(key) && local != key)
                    throw new InvalidOperationException("连接期间账号已切换，请重新连接。");
                key = local;
            }
            if (!DailyProfiles.ValidKey(key) && DailyIdentityGuard.Ready(snapshot, game, now)) key = snapshot!.AccountKey;
            if (DailyProfiles.ValidKey(key) && guard.Observe(snapshot, game, key, "", now))
            {
                DailySandbox.RequireBoundAccount(key);
                return key;
            }
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("connection.login_identity_unavailable");
            await Task.Delay(interval ?? TimeSpan.FromMilliseconds(250), token);
        }
    }
}

