namespace Dustweave;

// Volatile identity only. Never promotes an observation into reusable credentials.
public sealed class DailyLiveAccountIdentity
{
    private DailySnapshot? previous;
    private string verified = "";
    private readonly object sync = new();
    public string Resolve(GameInstance? game, DailySnapshot? snapshot, DateTimeOffset now)
    {
        lock (sync) return ResolveLocked(game, snapshot, now);
    }
    private string ResolveLocked(GameInstance? game, DailySnapshot? snapshot, DateTimeOffset now)
    {
        if (game == null || !DailyIdentityGuard.Ready(snapshot, game, now))
        {
            previous = null; return verified = "";
        }
        var s = snapshot!;
        bool same = previous != null && previous.ProcessId == s.ProcessId && previous.ProcessStartTicks == s.ProcessStartTicks
            && previous.InstanceId == s.InstanceId && previous.AccountKey == s.AccountKey && previous.PlayerKey == s.PlayerKey;
        if (!same || s.Sequence < previous!.Sequence || s.FrameUtcTicks < previous.FrameUtcTicks) verified = "";
        else if (s.Sequence > previous.Sequence && s.FrameUtcTicks > previous.FrameUtcTicks) verified = s.AccountKey;
        previous = s;
        return verified;
    }
}
