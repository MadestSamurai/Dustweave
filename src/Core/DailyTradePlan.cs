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
    public static JsonObject ReadSettings(string root, string account)
    {
        string path = SettingsPath(root, account);
        // A malformed saved file must remain visible as an error, not become an unlimited budget.
        return DailyTradeOptimizer.Preferences(File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : null);
    }
    public static string Report(JsonObject p)
    {
        var b = new StringBuilder("# 跑商计划\n\n");
        var s = p["summary"]!;
        bool calendar = S(p["solver"]?["scope"]) == "dated_cash_inventory";
        b.AppendLine($"游戏日期：{S(p["game_date"])}；快照：{S(p["captured_utc"])}。").AppendLine()
            .AppendLine(calendar ? "按未来62天的采购、料理、高价日和金币余额共同分配。11月16日前的新价仅用于预测，实际出售必须核对游戏报价。优先保留周转资金，没有当日售卖或近期满仓压力时延后料理。下方订单只含本次可执行数量，预测表不作为订单。" : "本轮增益按120%原料卖价核账，扣除采购与药耗；保留食材不追加虚拟溢价。未来29天供货与掉落单独预测，不计入今日可用库存或垫资。两者都不是已到账金币。")
            .AppendLine().AppendLine("|指标|金币／数量|\n|---|---:|");
        foreach (var (key, name) in new[] { ("incremental_profit", "本轮加工与交易增益"), ("eventual_sale_value", "预计回款"), ("cash_required", "本轮垫资"), ("cash_remaining", "垫资后金币"), ("potion_unit_price", "天赋药单价"), ("potions_used", "天赋药总消耗"), ("potions_to_buy", "其中需补买") })
            b.AppendLine($"|{name}|{N(s[key]):N0}|");
        b.AppendLine();
        if (p["forecast"]?["turnover"] is JsonObject turnover)
            b.AppendLine($"未来周转：7天内可高价出售的现有库存按回款的50%计入，最多放宽可用资金的25%；本方案使用 {turnover["allowance_used"]!.GetValue<double>():N0} 金币额度。该额度只影响未来资金预留，今天采购仍须有足额金币。").AppendLine();
        if (B(p["settings"]?["include_break_even_resales"]))
            b.AppendLine($"已开启金币成就采购：额外零收益商品 {N(s["break_even_count"]):N0} 个，垫资／预计回款 {N(s["break_even_value"]):N0} 金币。仅比较折扣后买价与120%卖价，不分摊砍价费；实际药耗仍计入上方账目，卖出须等待高价日。").AppendLine();
        if (p["drop_forecast"] is JsonObject drops) b.AppendLine($"掉落预测：已知周收集路线 {Rows(drops["maps"]).Length} 张地图，按静态奖励权重计算期望；仅影响材料分配，不允许使用未到账物品。");
        if (S(p["forecast"]?["terminal_method"]) == "bounded_batch_residue_joint_supply_duals")
            b.AppendLine("预测窗口末尾不足一份配方的食材，会按已知后续供给估计用途；该估值只用于分配，不加入本轮增益或金币余额。每次以当前库存重新计算，不依赖历史保留记录。");
        foreach (var warning in Rows(p["warnings"]))
            b.AppendLine(S(warning["message"]));
        b.AppendLine(calendar ? "未来供货、掉落和价格可能变化，未来料理数量允许小数近似；期末库存单列估值，不是已到账现金。每天按当前库存和金币重算，不保证跨周期全局最优。" : B(p["solver"]!["optimal"]) ? "当前库存与29天供给预测模型已求到最优；这不代表全年或随机掉落下的最优保证。" : "当前可行方案，尚未证明最优。");
        void Table(string title, string[] headers, IEnumerable<object?[]> rows)
        {
            b.AppendLine().AppendLine("## " + title).AppendLine().AppendLine("|" + string.Join('|', headers) + "|").AppendLine("|" + string.Join('|', headers.Select(_ => "---")) + "|");
            foreach (var row in rows)
                b.AppendLine("|" + string.Join('|', row.Select(x => (x?.ToString() ?? "—").Replace('|', '／').Replace('\n', ' '))) + "|");
        }
        Table("采购", ["商店", "商品", "物品", "数量", "其中零收益", "单价", "金额"], Rows(p["purchases"]).Select(r => new object?[] { N(r["shop"]), N(r["product"]), S(r["name"]), N(r["count"]), N(r["break_even_count"]), N(r["price"]), N(r["cost"]) }));
        Table("料理分配", ["料理", "份数", "天赋药", "预计售日"], Rows(p["cooking"]).Select(r => new object?[] { S(r["name"]), N(r["count"]), N(r["potions"]), S(r["sale_date"]) }));
        Table("售卖分配", ["物品", "数量", "单价", "商店", "预计售日", "今日报价确认"], Rows(p["sales"]).Select(r => new object?[] { S(r["name"]), N(r["count"]), N(r["price"]), N(r["shop"]), S(r["date"]), B(r["today_quote_confirmed"]) ? "是" : "否" }));
        Table("后续料理保留", ["食材", "数量", "原料120%卖价"], Rows(p["holds"]).Select(r => new object?[] { S(r["name"]), N(r["count"]), N(r["price"]) }));
        if(calendar) Table("周转预测（不执行）", ["日期", "扣除保留额后的预测金币", "采购数量", "料理份数", "售出数量"],
            Rows(p["forecast"]?["calendar"]).Select(r => new object?[] { S(r["date"]), r["cash_above_reserve"], r["purchases"], r["cooking"], r["sales"] }));
        b.AppendLine().AppendLine("表中包含尚未买入或制作的数量。执行前仍须重读库存并保留已分配食材。").AppendLine($"输入校验值：`{S(p["input_hash"])}`。").AppendLine($"数据表校验值：`{S(p["catalog_hash"])}`。");
        return b.ToString();
    }
}

