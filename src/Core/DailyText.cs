namespace Dustweave;

/// <summary>
/// Display metadata only. Key and arguments select a translation; Fallback keeps
/// the original diagnostic readable to older hosts. Never classify, authorize or
/// resume an operation using localized text. Arguments are opaque unless the
/// registered template explicitly declares a nested message or stage identifier.
/// </summary>
public sealed record DailyText(string Key, string Fallback, string[] Arguments)
{
    public static DailyText Of(string key, string fallback, params string[] arguments) => new(key, fallback, arguments);
}
