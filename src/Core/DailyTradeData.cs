using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
namespace Dustweave;

/// <summary>Build current trade inputs from validated game tables, never reuse old plans.</summary>
public sealed class DailyTradeData
{
    public string Directory { get; private set; } = "";
    public string Error { get; private set; } = "";
    public JsonObject Proof { get; private set; } = new() { ["engine"] = "dotnet-trade-data-v1", ["actions"] = 0, ["resources_spent"] = false };
    private JsonObject? catalog; private string assembly = "", database = ""; 
    public JsonObject? VerifiedCatalog => catalog?.DeepClone().AsObject();
    public static string Database(string? dataRoot = null)
    {
        string root = dataRoot ?? Environment.GetEnvironmentVariable("BD2_DATA_ROOT") ?? Path.Combine(System.IO.Directory.GetParent(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))!.FullName, "LocalLow", "Gamfs", "BrownDust II", "Data");
        string name = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes("common_v1")));
        return new[] { Path.Combine(root, "t", name), Path.Combine(root, name) }.FirstOrDefault(File.Exists) ?? throw new InvalidDataException("未找到当前游戏数据库；跑商未派发。");
    }
    public static string CatalogPath(string package) => Path.Combine(package, "data", "trade-catalog.json");
    public static void CheckTransactions(string root, JsonObject context, JsonObject? verifiedCatalog = null)
    {
        string account = context["actor"]![3]!.GetValue<string>(), cycle = context["cycle"]!.GetValue<string>();
        if (!DailyProfiles.ValidKey(account) || cycle.Length == 0 || !cycle.All(char.IsAsciiDigit))
            throw new StageHostException("identity", "跑商准备身份无效。");
        string job = Path.Combine(root, "trade", "executions", account, cycle, "execution.json");
        if (File.Exists(job))
        {
            var previous = DailyTradeCatalog.Read(job);
            if (previous["account"]?.GetValue<string>() != account || previous["context"]?["cycle"]?.GetValue<string>() != cycle)
                throw new InvalidDataException("跑商原记录身份不一致；保留原记录。");
            if (previous["state"]?.GetValue<string>() != "completed" && (verifiedCatalog == null || previous["catalog_hash"]?.GetValue<string>() != DailyTradeCatalog.Fingerprint(verifiedCatalog) || !JsonNode.DeepEquals(previous["context"]?["database_sha256"], verifiedCatalog["database_sha256"]) || !JsonNode.DeepEquals(previous["context"]?["player"], context["actor"]![4]) || !JsonNode.DeepEquals(previous["context"]?["server"], context["server"])))
                throw new InvalidDataException("数据库已更新，但本周期已有未完成交易。请先核对原成交，不能更换目录或重新下单。");
        }
        CheckPending(root, context);
    }
    public static void CheckPending(string root, JsonObject context)
    {
        CheckPending(new[] { "business", "managed-business" }.Select(name => Path.Combine(root, "live", name)).Where(System.IO.Directory.Exists).SelectMany(path => System.IO.Directory.EnumerateFiles(path, "*.json")).Select(DailyTradeCatalog.Read), context);
    }
    public static void CheckPending(IEnumerable<JsonObject> operations, JsonObject context)
    {
        string account = context["actor"]![3]!.GetValue<string>();
        foreach (var op in operations)
        {
            if (op["account"]?.GetValue<string>() != account)
                continue;
            string role = op["role"]?.GetValue<string>() ?? "", state = op["state"]?.GetValue<string>() ?? "";
            bool trade = DailyTradeJournal.IsTrade(op);
            if (trade && DailyManagedBusiness.Pending(op))
                throw new InvalidDataException("存在未确认的跑商成交；数据库更新不会清除或重放原请求。");
        }
    }
    public static async Task<DailyTradeData> PrepareAsync(string package, string root, GameInstance game, DailyStageFrame frame, Func<bool> stopped, Action<string> report, bool allowCompleted = true)
    {
        using var storage = DailyCacheStorage.Lease(root);
        var result = new DailyTradeData { Directory = package };
        try
        {
            void Stop()
            {
                if (stopped())
                    throw new StageHostException("stopped", "跑商数据准备已停止；没有交易输入。");
            }
            Stop();
            result.assembly = Path.Combine(Path.GetDirectoryName(game.Executable)!, "BrownDust II_Data", "Managed", "Assembly-CSharp.dll");
            result.database = Database();
            result.catalog = DailyTradeCatalog.Read(CatalogPath(package));
            string originalHash = DailyTradeCatalog.Hash(CatalogPath(package));
            string db = DailyTradeCatalog.Hash(result.database), dll = DailyTradeCatalog.Hash(result.assembly), client = DailyTradeCatalog.Mvid(result.assembly);
            result.Proof["database_sha256"] = db;
            result.Proof["assembly_sha256"] = dll;
            result.Proof["original_catalog_sha256"] = originalHash;
            result.Proof["context"] = frame.Context.DeepClone();
            if (client != frame.Daily?.Guild?.ClientMvid || DailyData.N(frame.Frame["ProcessId"]) != game.ProcessId || DailyData.N(frame.Frame["ProcessStartTicks"]) != game.StartTicks)
                throw new InvalidDataException("跑商规则与当前游戏进程不一致，请重新连接当前游戏。");
            bool sameClient = string.Equals(dll, result.catalog["assembly_sha256"]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase) && client == result.catalog["client"]?.GetValue<string>();
            if (sameClient && string.Equals(db, result.catalog["database_sha256"]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
            {
                result.Proof["state"] = "current";
                DailyTradeCatalog.ValidateFiles(result.catalog, result.assembly, result.database);
                return result;
            }
            if (!DailyTools.Available(package, "exporter"))
                throw new InvalidDataException("缺少.NET跑商表读取组件，请使用完整新版安装包。");
            // Cache only the catalog. Planning executes inside this .NET build.
            string key = typeof(DailyTradeOptimizer).Assembly.ManifestModule.ModuleVersionId.ToString() + originalHash + dll + db;
            string cache = Path.Combine(DailyCacheStorage.Area(root, "trade/catalogs"), Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant());
            System.IO.Directory.CreateDirectory(cache);
            using var lease = new FileStream(Path.Combine(cache, "prepare.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            string bundle = Path.Combine(cache, "bundle");
            if (TryCached(bundle, Path.Combine(cache, "proof.json"), result.assembly, result.database, out var cached))
            {
                Stop();
                result.Directory = bundle;
                result.catalog = cached;
                result.Proof["state"] = "current_cached_rules";
                result.Proof["catalog_sha256"] = DailyTradeCatalog.Hash(CatalogPath(bundle));
                return result;
            }
            string tables = Path.Combine(cache, "tables");
            report("正在核对游戏更新后的跑商规则；没有下单。");
            var start = DailyTools.StartInfo(package, "exporter");
            foreach (string arg in new[] { "--game-root", Path.GetDirectoryName(game.Executable)!, "--tables", string.Join(',', DailyTradeCatalog.Tables), "--simulator-compatible", "--output", tables })
                start.ArgumentList.Add(arg);
            string? overrideRoot = Environment.GetEnvironmentVariable("BD2_DATA_ROOT");
            if (!string.IsNullOrWhiteSpace(overrideRoot))
            {
                start.ArgumentList.Add("--data-root");
                start.ArgumentList.Add(overrideRoot);
            }
            using (var process = Process.Start(start) ?? throw new IOException(".NET跑商表读取未启动。"))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                try
                {
                    int code = await DailyHelperLifetime.WaitAsync(process, () => { Stop(); return Task.CompletedTask; }, TimeSpan.FromSeconds(90));
                    File.WriteAllText(Path.Combine(cache, "export.log"), await stdout + await stderr);
                    if (code != 0)
                        throw new InvalidDataException("当前跑商表无法完整读取；保留现场，请更新适配器。");
                }
                catch (TimeoutException) { throw new InvalidDataException("跑商数据核对超时；保留记录，没有下单。"); }
                finally
                {
                    try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (TimeoutException) { }
                }
            }
            Stop();
            var next = DailyTradeCatalog.Build(tables, client);
            DailyTradeCatalog.ValidateFiles(next, result.assembly, result.database);
            if (!string.Equals(next["database_sha256"]!.GetValue<string>(), db, StringComparison.OrdinalIgnoreCase) || !string.Equals(next["assembly_sha256"]!.GetValue<string>(), dll, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("导出期间游戏数据已再次更新；本次目录未启用。");
            string[] changed = DailyTradeCatalog.RuleChanges(result.catalog, next);
            result.Proof["client_rebound"] = !sameClient;
            result.Proof["changed_rules"] = new JsonArray(changed.Select(n => (JsonNode)JsonValue.Create(n)!).ToArray());
            // Build validates the supported formulas and complete cross-table
            // references. Prices, recipes, limits and character values are INPUTS
            // to the optimizer, not a version allowlist. Always replan with them.
            // Current tables are validated above; old plans are not reused.
            DailyJson.Write(CatalogPath(bundle), next);
            // Current tables are validated above; old plans are not reused.
            DailyTradeCatalog.ValidateFiles(next, result.assembly, result.database);
            Stop();
            result.Directory = bundle;
            result.catalog = next;
            result.Proof["state"] = changed.Length == 0 ? "compatible_rebound" : "current_rules_loaded";
            result.Proof["source_hashes"] = next["source_hashes"]!.DeepClone();
            result.Proof["character_sha256"] = next["character_sha256"]!.DeepClone();
            result.Proof["catalog_sha256"] = DailyTradeCatalog.Hash(CatalogPath(bundle));
            DailyJson.Write(Path.Combine(cache, "proof.json"), result.Proof);
            report("已读取当前游戏的商品、价格与料理配方，将按现有库存重新规划。");
        }
        catch (StageHostException) { throw; }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or System.Text.Json.JsonException or ArgumentException or KeyNotFoundException or OverflowException or FormatException or UnauthorizedAccessException) { result.Error = error.Message; result.Proof["state"] = "blocked"; result.Proof["detail"] = error.Message; DailyJson.Write(Path.Combine(root, "trade", "data-preflights", Guid.NewGuid().ToString("N") + ".json"), result.Proof); report("跑商数据准备：" + error.Message); }
        return result;
    }
    private (long Length, long Write) assemblyStamp, databaseStamp;
    internal static bool TryCached(string bundle, string proofPath, string assembly, string database, out JsonObject? catalog)
    {
        catalog = null;
        try
        {
            var proof = DailyTradeCatalog.Read(proofPath);
            if (proof["state"]?.GetValue<string>() is not ("compatible_rebound" or "current_rules_loaded") ||
                !string.Equals(proof["catalog_sha256"]?.GetValue<string>(), DailyTradeCatalog.Hash(CatalogPath(bundle)), StringComparison.OrdinalIgnoreCase)) return false;
            var candidate = DailyTradeCatalog.Read(CatalogPath(bundle));
            DailyTradeCatalog.ValidateFiles(candidate, assembly, database);
            catalog = candidate;
            return true;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException) { return false; }
    }
    private static (long, long) Stamp(string path)
    {
        var file = new FileInfo(path);
        return (file.Length, file.LastWriteTimeUtc.Ticks);
    }
    public void AssertReady()
    {
        if (Error.Length > 0)
            throw new StageHostException("adapter", Error);
        if (catalog != null)
            try
            {
                var a = Stamp(assembly);
                var d = Stamp(database);
                if (a != assemblyStamp || d != databaseStamp)
                {
                    DailyTradeCatalog.ValidateFiles(catalog, assembly, database);
                    if (a != Stamp(assembly) || d != Stamp(database))
                        throw new InvalidDataException("验证期间规则文件改变");
                    assemblyStamp = a;
                    databaseStamp = d;
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException) { throw new StageHostException("adapter", e.Message + "；没有开始或重放交易。"); }
    }
}



