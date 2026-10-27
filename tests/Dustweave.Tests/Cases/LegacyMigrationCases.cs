using Dustweave;
internal static class LegacyMigrationCases
{
    internal static void Run(string root, List<string> cases)
    {
        foreach (var input in new[] { "我不需要更新", " I do not need an update ", "i do not need an update" })
            if (!DailyLegacyMigration.Accepts(input)) throw new Exception("Acknowledgement rejected: " + input);
        foreach (var input in new string?[] { null, "", "我不需要更", "我需要更新", "继续", "yes", "I do not need update", "我不需要更新!" })
            if (DailyLegacyMigration.Accepts(input)) throw new Exception("Incomplete acknowledgement accepted.");
        string[][] blocked = [[], ["--scheduled"], ["--updated", "nonce"], ["--desktop-session"], ["--run-selected", "plan"],
            ["--parallel-worker", "job"], [DailySandbox.LaunchSwitch, "request"], [DailySandboxSessions.ExportSwitch, "a", "b"],
            ["--tool", "fishing"], ["--tool", "equipment"], ["--connection"], ["--mansion-window"], ["--utility", "live", "ready"],
            ["--utility", "minigame", "connect"], ["--connection", "attach", "bridge.dll"], ["--connection", "locate"],
            ["--utility", "diagnostics", "square-crossing", "out"], ["--trade-offline"], ["--unknown"]];
        foreach (var args in blocked)
            if (!DailyLegacyMigration.RequiresAcknowledgement(args)) throw new Exception("Startup entry bypasses gate: " + string.Join(' ', args));
        string[][] offline = [["--identity", "out"], ["--apply-update", "job"], ["--recover-update", "job"], ["--check-migration-gate", "out"],
            ["--create-update-delta", "base", "target", "out"], ["--smoke", "out"], ["--tool", "fishing", "--smoke", "out"],
            ["--utility", "live", "self-test"], ["--utility", "live", "prepare", "managed", "out"], ["--utility", "exporter", "--game-root", "path"],
            ["--connection", "self-test"], ["--connection", "prepare", "managed", "out"], ["--connection", "taps", "managed", "spec", "out"]];
        foreach (var args in offline)
            if (DailyLegacyMigration.RequiresAcknowledgement(args)) throw new Exception("Offline validation or update recovery blocked: " + string.Join(' ', args));
        cases.Add("Legacy migration gates all interactive/automatic entries, accepts exact multilingual acknowledgement and permits offline maintenance.");
    }
}
