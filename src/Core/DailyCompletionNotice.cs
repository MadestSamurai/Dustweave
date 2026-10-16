namespace Dustweave;

public sealed record DailyCompletionNotice(bool Windows = true, bool Popup = true, bool Sound = false)
{
    // Independent, best-effort channels, matching the infinite-draw tool.
    // Notification delivery must never change queue completion or block accounts.
    public string[] Deliver(Action<string> send)
    {
        var errors = new List<string>();
        foreach (var (enabled, channel) in new[] { (Windows, "windows"), (Sound, "sound"), (Popup, "popup") })
            if (enabled) try { send(channel); } catch (Exception error) { errors.Add(channel + ": " + error.Message); }
        return errors.ToArray();
    }
}
