namespace Dustweave;

// Capture after login verification, then close only that same account's process.
// Queue results remain intact if normal window close fails; never force termination.
public sealed record DailyQueueCompletion(GameInstance Game, string Account, string Player)
{
    public static DailyQueueCompletion? Capture(IGameHost host, string account)
    {
        var game = host.Find();
        if (game == null) return null;
        var snapshot = host.ReadSnapshot(game);
        return DailyIdentityGuard.Ready(snapshot, game, DateTimeOffset.UtcNow) && snapshot!.AccountKey == account
            ? new(game, account, snapshot.PlayerKey) : null;
    }

    public static bool Eligible(QueueView result, string account, bool stopped) =>
        !stopped && !result.Expired && result.State == "completed" && result.Account == account
        && result.Stages.Count > 0 && result.Stages.All(s => s.State is "completed" or "skipped");

    public static async Task CloseAsync(IGameHost host, DailyQueueCompletion? target, string account, QueueView result,
        bool stopped, CancellationToken cancellation = default)
    {
        if (!Eligible(result, account, stopped)) return;
        cancellation.ThrowIfCancellationRequested();
        var current = host.Find();
        if (current == null) return;
        if (target == null || target.Account != account || current != target.Game)
            throw new InvalidOperationException("completion.game_changed");
        var snapshot = host.ReadSnapshot(current);
        if (!DailyIdentityGuard.Ready(snapshot, current, DateTimeOffset.UtcNow)
            || snapshot!.AccountKey != account || snapshot.PlayerKey != target.Player)
            throw new InvalidOperationException("completion.identity_changed");
        await host.CloseAsync(current, cancellation);
        if (host.Find() == current) throw new InvalidOperationException("completion.not_closed");
    }
}
