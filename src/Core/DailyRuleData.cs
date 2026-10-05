using System.Diagnostics;
using System.Text.Json.Nodes;
using static BD2Daily.DailyData;
namespace BD2Daily;

/// <summary>Complete daily table set, bound once to the installed client and database; no game input.</summary>
public sealed class DailyRuleData
{
    private readonly Dictionary<string, string> paths = new(StringComparer.Ordinal);
    private string assembly = "", database = "", assemblyHash = "", databaseHash = "";
    private (long, long) assemblyStamp, databaseStamp;
    public JsonObject Proof { get; } = O(("engine", "dotnet-rule-data-v1"), ("actions", 0));
    public static readonly Dictionary<string, string[]> Groups = new(StringComparer.Ordinal)
    {
        ["extra"] = ["EventMissionGroupTable"],
        ["gacha"] = ["GachaGroupTable", "GachaTable", "MissionSectionRewardTable"],
        ["missions"] = ["LocalTextTable", "MissionTable"],
        ["names"] = ["NameTextTable"],
        ["policy"] = ["CharTable", "DispatchTable", "EquipmentMakingTable", "EquipmentTable", "HuntDispatchTable", "MissionTable", "PassTable", "SkyWayFieldTable", "SquareRewardTable", "StatueRewardTable"]
    };
    private static (long, long) Stamp(string file)
    {
        var info = new FileInfo(file);
        return (info.Length, info.LastWriteTimeUtc.Ticks);
    }
    public string PathFor(string relative)
    {
        AssertReady();
        return paths.TryGetValue(relative.Replace('\\', '/'), out var path) ? path : throw new InvalidDataException("Unknown daily rules: " + relative);
    }
    public void AssertReady()
    {
        var a = Stamp(assembly);
        var d = Stamp(database);
        if (a == assemblyStamp && d == databaseStamp)
            return;
        Require(DailyTradeCatalog.Hash(assembly) == assemblyHash && DailyTradeCatalog.Hash(database) == databaseHash, "游戏规则在执行期间改变，未沿用旧计划；请重新开始队列");
        Require(a == Stamp(assembly) && d == Stamp(database), "验证期间规则文件改变");
        assemblyStamp = a;
        databaseStamp = d;
    }
    public static JsonObject ValidateExport(string directory, string dllHash, string dbHash, IEnumerable<string> names)
    {
        var manifest = DailyTradeCatalog.Read(Path.Combine(directory, "manifest.json"));
        Require(string.Equals(S(manifest["assemblySha256"]), dllHash, StringComparison.OrdinalIgnoreCase) && string.Equals(S(manifest["databaseSha256"]), dbHash, StringComparison.OrdinalIgnoreCase), "Daily table provenance differs from installed client");
        var hashes = new JsonObject();
        foreach (string name in names.Distinct(StringComparer.Ordinal))
        {
            string path = Path.Combine(directory, name + ".json");
            var rows = Rows(JsonNode.Parse(File.ReadAllText(path).TrimStart('\uFEFF')));
            Require(manifest["tables"]?.AsObject().ContainsKey(name) == true && N(manifest["tables"]![name]) == rows.Length, "Incomplete daily export: " + name);
            Require(rows.Select(r => (N(r["groupId"]), N(r["id"]))).Distinct().Count() == rows.Length, "Duplicate daily row key: " + name);
            hashes[name] = DailyTradeCatalog.Hash(path);
        }
        return hashes;
    }
    public static Task<DailyRuleData> PrepareAsync(string package, string root, GameInstance game, Func<Task<DailyStageFrame>> read, Func<bool> stopped, Action<string> report)
        => PrepareSetAsync(package, root, game, read, stopped, report, "daily", Path.Combine(package, "data", "daily"), Groups);
    public static async Task<DailyRuleData> PrepareSetAsync(string package, string root, GameInstance game, Func<Task<DailyStageFrame>> read, Func<bool> stopped, Action<string> report, string area, string source, IReadOnlyDictionary<string, string[]> groups)
    {
        Require(System.Text.RegularExpressions.Regex.IsMatch(area, "^[a-z][a-z0-9-]{0,63}$"), "Invalid rules namespace");
        void Stop()
        {
            if (stopped())
                throw new StageHostException("stopped", "规则读取已停止，没有游戏输入。");
        }
        Stop();
        var initial = await read();
        var result = new DailyRuleData { assembly = Path.Combine(Path.GetDirectoryName(game.Executable)!, "BrownDust II_Data", "Managed", "Assembly-CSharp.dll"), database = DailyTradeData.Database() };
        result.assemblyHash = DailyTradeCatalog.Hash(result.assembly);
        result.databaseHash = DailyTradeCatalog.Hash(result.database);
        string mvid = DailyTradeCatalog.Mvid(result.assembly);
        Require(N(initial.Frame["ProcessId"]) == game.ProcessId && N(initial.Frame["ProcessStartTicks"]) == game.StartTicks && initial.Daily?.Guild?.ClientMvid == mvid, "日常规则与当前游戏进程不一致");
        result.Proof["assembly_sha256"] = result.assemblyHash;
        result.Proof["database_sha256"] = result.databaseHash;
        result.Proof["client"] = mvid;
        string packaged = Path.GetFullPath(source);
        bool matching = groups.All(group => { string path = Path.Combine(packaged, group.Key, "manifest.json"); if (!File.Exists(path)) return false; var manifest = DailyTradeCatalog.Read(path); return string.Equals(S(manifest["assemblySha256"]), result.assemblyHash, StringComparison.OrdinalIgnoreCase) && string.Equals(S(manifest["databaseSha256"]), result.databaseHash, StringComparison.OrdinalIgnoreCase); });
        var hashes = new JsonObject();
        if (matching)
        {
            foreach (var group in groups)
            {
                string dir = Path.Combine(packaged, group.Key);
                hashes[group.Key] = ValidateExport(dir, result.assemblyHash, result.databaseHash, group.Value);
                foreach (string name in group.Value)
                    result.paths.Add(group.Key + "/" + name + ".json", Path.Combine(dir, name + ".json"));
            }
            result.Proof["state"] = "packaged_current";
        }
        else
        {
            Require(DailyTools.Available(package, "exporter"), "缺少.NET规则读取组件，请使用完整安装包");
            string cache = Path.Combine(root, "rules", area, result.assemblyHash + "-" + result.databaseHash);
            Directory.CreateDirectory(cache);
            using var lease = new FileStream(Path.Combine(cache, "prepare.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            string tables = Path.Combine(cache, "tables");
            string[] names = groups.Values.SelectMany(n => n).Distinct(StringComparer.Ordinal).Order().ToArray();
            bool valid = false;
            if (File.Exists(Path.Combine(cache, "verified.json")))
                try
                {
                    var prior = DailyTradeCatalog.Read(Path.Combine(cache, "verified.json"));
                    tables = Path.GetFullPath(S(prior["directory"]));
                    Require(tables.StartsWith(cache + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "日常规则缓存位置无效");
                    hashes = ValidateExport(tables, result.assemblyHash, result.databaseHash, names);
                    valid = JsonNode.DeepEquals(prior["hashes"], hashes);
                }
                catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or System.Text.Json.JsonException) { }
            if (!valid)
            {
                report("正在用.NET读取本机日常规则；同一版本只读取一次，不执行游戏操作。");
                tables = Path.Combine(cache, "export-" + Guid.NewGuid().ToString("N"));
                var start = DailyTools.StartInfo(package, "exporter");
                foreach (string arg in new[] { "--game-root", Path.GetDirectoryName(game.Executable)!, "--tables", string.Join(',', names), "--simulator-compatible", "--output", tables })
                    start.ArgumentList.Add(arg);
                string? dataRoot = Environment.GetEnvironmentVariable("BD2_DATA_ROOT");
                if (!string.IsNullOrWhiteSpace(dataRoot))
                {
                    start.ArgumentList.Add("--data-root");
                    start.ArgumentList.Add(dataRoot);
                }
                using var process = Process.Start(start) ?? throw new IOException("日常规则读取器未启动");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                try
                {
                    int code = await DailyHelperLifetime.WaitAsync(process, () => { Stop(); return Task.CompletedTask; }, TimeSpan.FromSeconds(90));
                    File.WriteAllText(Path.Combine(cache, "export.log"), await stdout + await stderr);
                    Require(code == 0, "日常规则无法完整读取，请查看诊断");
                }
                finally
                {
                    try
                    {
                        await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    catch (TimeoutException) { }
                }
                Stop();
                result.AssertReady();
                hashes = ValidateExport(tables, result.assemblyHash, result.databaseHash, names);
                // Publish the verified location, not an in-place replacement of a directory used by another queue.
                DailyJson.Write(Path.Combine(cache, "verified.json"), O(("directory", tables), ("hashes", hashes)));
            }
            foreach (var group in groups)
                foreach (string name in group.Value)
                    result.paths.Add(group.Key + "/" + name + ".json", Path.Combine(tables, name + ".json"));
            result.Proof["state"] = valid ? "cached_current" : "current_export";
            result.Proof["export"] = tables;
        }
        Stop();
        Require(JsonNode.DeepEquals(initial.Context, (await read()).Context), "规则读取期间账号、连接或周期改变");
        result.AssertReady();
        result.Proof["hashes"] = hashes;
        DailyJson.Write(Path.Combine(root, "rules", area + "-last.json"), result.Proof);
        return result;
    }
}


