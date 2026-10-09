using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public static class DailyTradePlan
{
    public static JsonObject Generate(JsonObject catalog, JsonObject state, JsonObject? settings, string output, string? expectedAccount = null, Action? check = null)
    {
        check?.Invoke();
        Require(expectedAccount == null || S(state["context"]?["account"]) == expectedAccount, "快照与所选账号不同");
        var stable = state.DeepClone().AsObject();
        stable.Remove("captured_utc");
        string engine = typeof(DailyTradeOptimizer).Assembly.ManifestModule.ModuleVersionId.ToString();
        string key = DailyTradeCatalog.Fingerprint(O(("engine", engine), ("catalog", catalog), ("state", stable), ("settings", DailyTradeOptimizer.Preferences(settings))));
        var old = DailyJson.TryRead<JsonObject>(output);
        JsonObject result;
        if (S(old?["cache_key"]) == key && B(old?["solver"]?["optimal"]))
        {
            DailyTradeOptimizer.Validate(catalog, state);
            result = old!.DeepClone().AsObject();
            result["captured_utc"] = Copy(state["captured_utc"]);
            result["input_hash"] = DailyTradeCatalog.Fingerprint(state);
            result["cache_reused"] = true;
        }
        else
        {
            result = DailyTradeOptimizer.Plan(catalog, state, settings, check: check);
            result["cache_key"] = key;
            result["cache_reused"] = false;
        }
        check?.Invoke();
        result["generated_utc"] = DateTimeOffset.UtcNow.ToString("O");
        DailyJson.Write(output, result);
        WriteText(Path.ChangeExtension(output, ".md"), Report(result));
        return result;
    }
    private static void WriteText(string path, string text)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                f.Write(Encoding.UTF8.GetBytes(text));
                f.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static string SettingsPath(string root, string account) => Path.Combine(root, "trade", "plans", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account))).ToLowerInvariant(), "settings.json");
    public static string Report(JsonObject p)
    {
        var b = new StringBuilder("# 跑商计划\n\n");
        var s = p["summary"]!;
        b.AppendLine($"游戏日期：{S(p["game_date"])}；快照：{S(p["captured_utc"])}。").AppendLine().AppendLine("本轮增益按120%原料卖价核账，扣除采购与药耗；保留食材不追加虚拟溢价。未来29天供货与掉落单独预测，不计入今日可用库存或垫资。两者都不是已到账金币。").AppendLine().AppendLine("|指标|金币／数量|\n|---|---:|");
        foreach (var (key, name) in new[] { ("incremental_profit", "本轮加工与交易增益"), ("eventual_sale_value", "预计回款"), ("cash_required", "本轮垫资"), ("cash_remaining", "垫资后金币"), ("potion_unit_price", "天赋药单价"), ("potions_used", "天赋药总消耗"), ("potions_to_buy", "其中需补买") })
            b.AppendLine($"|{name}|{N(s[key]):N0}|");
        b.AppendLine();
        if (p["drop_forecast"] is JsonObject drops) b.AppendLine($"掉落预测：已知周收集路线 {Rows(drops["maps"]).Length} 张地图，按静态奖励权重计算期望；仅影响材料分配，不允许使用未到账物品。");
        foreach (var warning in Rows(p["warnings"]))
            b.AppendLine(S(warning["message"]));
        b.AppendLine(B(p["solver"]!["optimal"]) ? "当前库存与29天供给预测模型已求到最优；这不代表全年或随机掉落下的最优保证。" : "当前可行方案，尚未证明最优。");
        void Table(string title, string[] headers, IEnumerable<object?[]> rows)
        {
            b.AppendLine().AppendLine("## " + title).AppendLine().AppendLine("|" + string.Join('|', headers) + "|").AppendLine("|" + string.Join('|', headers.Select(_ => "---")) + "|");
            foreach (var row in rows)
                b.AppendLine("|" + string.Join('|', row.Select(x => (x?.ToString() ?? "—").Replace('|', '／').Replace('\n', ' '))) + "|");
        }
        Table("采购", ["商店", "商品", "物品", "数量", "单价", "金额"], Rows(p["purchases"]).Select(r => new object?[] { N(r["shop"]), N(r["product"]), S(r["name"]), N(r["count"]), N(r["price"]), N(r["cost"]) }));
        Table("料理分配", ["料理", "份数", "天赋药", "预计售日"], Rows(p["cooking"]).Select(r => new object?[] { S(r["name"]), N(r["count"]), N(r["potions"]), S(r["sale_date"]) }));
        Table("售卖分配", ["物品", "数量", "单价", "商店", "预计售日", "今日报价确认"], Rows(p["sales"]).Select(r => new object?[] { S(r["name"]), N(r["count"]), N(r["price"]), N(r["shop"]), S(r["date"]), B(r["today_quote_confirmed"]) ? "是" : "否" }));
        Table("后续料理保留", ["食材", "数量", "原料120%卖价"], Rows(p["holds"]).Select(r => new object?[] { S(r["name"]), N(r["count"]), N(r["price"]) }));
        b.AppendLine().AppendLine("表中包含尚未买入或制作的数量。执行前仍须重读库存并保留已分配食材。").AppendLine($"输入校验值：`{S(p["input_hash"])}`。").AppendLine($"数据表校验值：`{S(p["catalog_hash"])}`。");
        return b.ToString();
    }
}

