using System.Diagnostics;
using System.Text.Json.Nodes;
namespace BD2Daily;

/// <summary>Rules are read and frozen before any free preview; a hot update cannot silently change a consuming plan.</summary>
public sealed class DailyFreeDrawData
{
    public DailyFreeDrawRules Rules { get; private set; } = null!;
    public JsonObject Proof { get; private set; } = new() { ["engine"] = "dotnet-free-draw-data-v1", ["actions"] = 0 };
    private DailyRuleData data = null!;
    public void AssertReady() => data.AssertReady();
    public static DailyFreeDrawRules ReadExport(string directory, string assemblyHash, string databaseHash)
    {
        var manifest = DailyTradeCatalog.Read(Path.Combine(directory, "manifest.json"));
        if (!string.Equals(manifest["assemblySha256"]?.GetValue<string>(), assemblyHash, StringComparison.OrdinalIgnoreCase) || !string.Equals(manifest["databaseSha256"]?.GetValue<string>(), databaseHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Free draw export provenance mismatch");
        JsonArray Read(string name)
        {
            var rows = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, name + ".json")).TrimStart('\uFEFF'))!.AsArray();
            if (DailyEvidence.Integer(manifest["tables"]?[name]) != rows.Count)
                throw new InvalidDataException("Incomplete free draw table export: " + name);
            return rows;
        }
        var rules = DailyFreeDrawRules.Build(Read("GachaTable"), Read("GachaGroupTable"));
        rules.Projection["source"] = new JsonObject { ["assembly_sha256"] = assemblyHash, ["database_sha256"] = databaseHash, ["tables"] = manifest["tables"]!.DeepClone() };
        return rules;
    }
    public static async Task<DailyFreeDrawData> PrepareAsync(string package, string root, GameInstance game, Func<Task<DailyStageFrame>> read, Func<bool> stopped, Action<string> report)
    {
        var data = await DailyRuleData.PrepareSetAsync(package, root, game, read, stopped, report,
            "free-draws", Path.Combine(package, "data", "daily"),
            new Dictionary<string, string[]> { ["gacha"] = ["GachaTable", "GachaGroupTable"] });
        var result = new DailyFreeDrawData { data = data };
        result.Proof = data.Proof.DeepClone().AsObject();
        result.Proof["engine"] = "dotnet-free-draw-data-v1";
        result.Rules = ReadExport(Path.GetDirectoryName(data.PathFor("gacha/GachaTable.json"))!,
            result.Proof["assembly_sha256"]!.GetValue<string>(), result.Proof["database_sha256"]!.GetValue<string>());
        if (stopped())
            throw new StageHostException("stopped", "免费抽取数据准备已停止，没有游戏输入。");
        result.AssertReady();
        return result;
    }
}
