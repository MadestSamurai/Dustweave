namespace Dustweave;

// The parent renews an account-scoped lease. A duplicate heartbeat is not a new
// command: a worker paused by its queue must not restart itself on an old "run".
public sealed class DailyParallelLease(string id, string account, DateTimeOffset started)
{
    public long Sequence { get; private set; } = -1;
    public volatile bool Authorized;
    public volatile bool Paused;
    public volatile bool Stopped;
    public volatile bool ParentLost;
    public void Observe(DailyParallelCommand? command, DateTimeOffset now)
    {
        bool valid = command != null && command.Id == id && command.Account == account
            && command.Action is "run" or "pause" or "resume" or "stop" && command.Sequence >= 0
            && command.AtUtc <= now.AddSeconds(5) && now-command.AtUtc < TimeSpan.FromSeconds(20);
        if (!valid)
        {
            if (now-started > TimeSpan.FromSeconds(30)) { Stopped=true; ParentLost=true; }
            return;
        }
        Authorized=true;
        if (command!.Sequence <= Sequence) return;
        Sequence=command.Sequence;
        if (command.Action == "stop") Stopped=true;
        if (!Stopped) Paused=command.Action == "pause";
    }
    public void Stop() => Stopped=true;
}
