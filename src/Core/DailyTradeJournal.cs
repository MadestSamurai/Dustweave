using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

/// <summary>Business journals have role-specific scope shapes. Never inspect unrelated scopes as trade objects.</summary>
public static class DailyTradeJournal
{
    public static void ArchivePlan(string directory)
    {
        if (!File.Exists(Path.Combine(directory, "execution.json"))) return;
        string archive = Path.Combine(directory, "history", DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(archive);
        foreach (string file in Directory.EnumerateFiles(directory))
            if (Path.GetExtension(file) is ".json" or ".md") File.Copy(file, Path.Combine(archive, Path.GetFileName(file)));
    }
    public static bool IsTrade(string role, JsonObject scope) =>
        role.StartsWith("trade.", StringComparison.Ordinal) ||
        role == "dispatch.start" && (scope.ContainsKey("trade_session") || scope.ContainsKey("trade") || scope["_proof"] is JsonObject p && S(p["kind"]) == "bargain");
    public static bool IsTrade(JsonObject operation)
    {
        string role = S(operation["role"]);
        if (role.StartsWith("trade.", StringComparison.Ordinal)) return true;
        if (role != "dispatch.start" || operation["scope"] is not JsonObject scope) return false;
        return scope.ContainsKey("trade_session") || scope.ContainsKey("trade") ||
            scope["_proof"] is JsonObject proof && S(proof["kind"]) == "bargain";
    }
}
