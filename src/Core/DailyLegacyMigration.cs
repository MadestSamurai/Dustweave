namespace Dustweave;

// This policy belongs only to the retired layout-2 maintenance branch.
public static class DailyLegacyMigration
{
    public const string ChineseAcknowledgement = "我不需要更新";
    public const string EnglishAcknowledgement = "I do not need an update";
    public const string Download = "https://github.com/MadestSamurai/Dustweave/releases/latest";
    public const string Mirror = "https://pan.quark.cn/s/0ee553c60c0d?pwd=5AgC";
    public const string MirrorCode = "5AgC";

    // The requested Simplified and Traditional Chinese phrases are identical.
    public static bool Accepts(string? input) => input?.Trim() == ChineseAcknowledgement ||
        string.Equals(input?.Trim(), EnglishAcknowledgement, StringComparison.OrdinalIgnoreCase);

    public static bool RequiresAcknowledgement(string[] original)
    {
        var args = DailyDesktopLaunch.Normalize(original);
        if (args.Length == 0) return true;
        // Exact offline entry points only. Unknown/new entry points fail closed.
        if (args.Length == 2 && args[0] is "--identity" or "--tool-catalog" or "--smoke" or "--design-preview"
            or "--check-stage-host" or "--check-tool-session" or "--check-suite-host" or "--check-plugin"
            or "--check-tool-languages" or "--check-tool-language-ui" or "--check-migration-gate"
            or "--legacy-migration-gate" or "--apply-update" or "--recover-update" or "--loading-experiment-analyze") return false;
        if (args.Length == 3 && args[0] is "--check-client" or "--check-suite" or "--replay" or "--trade-smoke" or "--tool-menu-probe") return false;
        if (args.Length == 4 && args[0] is "--check-tool-client" or "--create-update-delta") return false;
        if (args.Length == 4 && args[0] == "--tool" && args[2] == "--smoke") return false;
        if (args.Length >= 2 && args[0] == "--utility")
        {
            if (args[1] == "exporter") return false;
            if (args.Length == 4 && args[2] == "--describe") return false;
            if (args[1] == "live" && (args.Length == 3 && args[2] == "self-test"
                || args.Length == 5 && args[2] == "prepare" || args.Length == 6 && args[2] == "taps-all")) return false;
        }
        return true;
    }
}
