namespace Dustweave;

// Standalone read-only clients must not own account switching, revoke a daily lease,
// perform startup UI actions, or write account profiles just to attach an observer.
public static class CurrentGameObservation
{
    public static async Task ConnectAsync(IAccountSessions sessions, IGameHost host, Action<string> progress, CancellationToken token, TimeSpan? timeout = null, TimeSpan? interval = null)
    {
        var key = sessions.Read().CurrentKey;
        if (!DailyProfiles.ValidKey(key))
            throw new InvalidOperationException("请先在游戏内完成登录，再连接装备助手。");
        var game = host.Find() ?? throw new InvalidOperationException("请先启动游戏并进入主城。");
        await host.ConnectAsync(game, progress, token);
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
            DailyLoginReadiness.Check(snapshot, game, DateTimeOffset.UtcNow.UtcTicks);
            if (guard.Observe(snapshot, game, key, "", DateTimeOffset.UtcNow))
            {
                if (sessions.Read().CurrentKey != key)
                    throw new InvalidOperationException("连接期间账号已切换，请重新连接。");
                return;
            }
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("尚未读到当前账号的游戏画面，请进入主城后重新连接。");
            await Task.Delay(interval ?? TimeSpan.FromMilliseconds(250), token);
        }
    }
}

