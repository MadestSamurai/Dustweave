using System.Text.Json.Nodes;
namespace BD2Daily;

/// <summary>Refresh only an unsubmitted transaction whose added trade resources are proven rewards.</summary>
public static class DailyTradeResume
{
    private static readonly string[] BalanceKeys = ["gold", "potions", "items", "other", "protected"];
    public static JsonObject Wallet(JsonObject evidence)
    {
        var groups = DailyEvidence.Reading(evidence, "trade.inventory", "$items")!.AsArray();
        if (groups.Count != DailyEvidence.Integer(DailyEvidence.Reading(evidence, "trade.inventory", "Count")))
            throw new InvalidDataException("跑商库存分组不完整。");
        var foods = groups.Where(g => g?["Key"]?.GetValue<string>() == "Food").ToArray();
        if (foods.Length != 1)
            throw new InvalidDataException("食材库存有歧义。");
        var stacks = foods[0]!["Value.Values"]!.AsArray();
        if (stacks.Count != DailyEvidence.Integer(foods[0]!["Value.Count"]))
            throw new InvalidDataException("食材库存被截断。");
        var items = new JsonObject();
        var kept = new JsonObject();
        var indexes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stack in stacks)
        {
            if (DailyEvidence.Integer(stack!["type"]) != 5)
                throw new InvalidDataException("跑商库存含有非食材。");
            string id = DailyEvidence.Integer(stack["id"]).ToString(), index = stack["invenIndex"]!.GetValue<string>();
            long count = DailyEvidence.Integer(stack["count"]);
            if (count < 0 || index.Length == 0 || !indexes.Add(index))
                throw new InvalidDataException("食材数量或堆叠标识无效。");
            items[id] = checked((items[id] == null ? 0 : DailyEvidence.Integer(items[id])) + count);
            if (DailyTradeSnapshot.Flag(stack["keepFlag"]))
                kept[index] = stack.DeepClone();
        }
        long gold = DailyEvidence.Integer(DailyEvidence.Reading(evidence, "trade.currency", "Gold")), potions = DailyEvidence.Integer(DailyEvidence.Reading(evidence, "trade.currency", "Catalyst"));
        if (gold < 0 || potions < 0)
            throw new InvalidDataException("跑商货币无效。");
        return new()
        {
            ["gold"] = gold,
            ["potions"] = potions,
            ["items"] = items,
            ["stacks"] = stacks.DeepClone(),
            ["other"] = DailyTradeCatalog.Fingerprint(new JsonArray(groups.Where(g => g?["Key"]?.GetValue<string>() != "Food").Select(g => g!.DeepClone()).ToArray())),
            ["protected"] = kept
        };
    }
    public static bool MatchesInitial(JsonObject job, JsonObject evidence)
    {
        var current = Wallet(evidence);
        return BalanceKeys.All(k => JsonNode.DeepEquals(job["initial"]![k], current[k]));
    }
    public static JsonObject? Evaluate(JsonObject job, JsonObject context, JsonObject catalog, JsonObject evidence, JsonObject planning, IReadOnlyList<JsonObject> operations)
    {
        if (job["state"]?.GetValue<string>() == "completed" || job["phase"]?.GetValue<string>() != "purchase" || job["operations"] is not JsonArray confirmed || confirmed.Count != 0)
            return null;
        var current = Wallet(evidence);
        var initial = job["initial"]!.AsObject();
        if (BalanceKeys.All(k => JsonNode.DeepEquals(initial[k], current[k])))
            return null;
        void Refuse(string detail) => throw new InvalidDataException(detail + "；保留原交易，没有刷新库存基准或下单。");
        string account = context["actor"]![3]!.GetValue<string>(), id = job["id"]?.GetValue<string>() ?? "";
        if (job["state"]?.GetValue<string>() is not ("stopped" or "prepared") || id.Length != 32 || !id.All(char.IsAsciiHexDigit) || !DailyProfiles.ValidKey(account))
            Refuse("交易不是可核对的未下单状态");
        var original = job["context"]!.AsObject();
        foreach (var (key, value) in new[] { ("account", context["actor"]![3]), ("player", context["actor"]![4]), ("server", context["server"]), ("cycle", context["cycle"]), ("client", catalog["client"]), ("database_sha256", catalog["database_sha256"]) })
            if (!JsonNode.DeepEquals(original[key], value))
                Refuse("交易身份、周期或规则变化");
        if (job["account"]?.GetValue<string>() != account || job["catalog_hash"]?.GetValue<string>() != DailyTradeCatalog.Fingerprint(catalog) || !JsonNode.DeepEquals(job["plan"]?["context"], original) || job["plan"]?["catalog_hash"]?.GetValue<string>() != job["catalog_hash"]!.GetValue<string>())
            Refuse("原计划绑定不一致");
        var frame = evidence["Frame"]!.AsObject();
        if (frame["AccountKey"]?.GetValue<string>() != account || !JsonNode.DeepEquals(frame["PlayerKey"], context["actor"]![4]) || evidence["Error"]?.GetValue<string>() != "")
            Refuse("库存观察不属于当前角色");
        var policy = DailyNavigationPolicy.Load();
        if (DailyNavigationDecision.Phase(frame, "trade") != "page" || !DailyNavigationDecision.Types(frame).Any(t => t is "ShopUI" or "MenuUI" or "GameFieldDefaultUI" or "QuickMenuUI") || DailyNavigationDecision.Rows(frame).Any(r => r["Popup"]?.GetValue<bool>() == true && !policy.Background.Contains(r["Type"]!.GetValue<string>()) && r["Type"]?.GetValue<string>() is not ("ShopUI" or "MenuUI" or "GameFieldDefaultUI" or "QuickMenuUI")))
            Refuse("不是已确认的跑商入口现场");
        var native = DailyEvidence.Reading(evidence, "trade.native", "$self")!.AsObject();
        if (DailyEvidence.Reading(evidence, "trade.bargain", "$self")!.GetValue<bool>() || native["BargainActive"]!.GetValue<bool>() || !JsonNode.DeepEquals(native["Date"], job["plan"]!["game_date"]) || DailyEvidence.Integer(native["ServerTicks"]) >= long.Parse(context["cycle"]!.GetValue<string>()))
            Refuse("砍价已开始或商店日期变化");
        if (!JsonNode.DeepEquals(planning["context"], original) || !JsonNode.DeepEquals(planning["game_date"], job["plan"]!["game_date"]))
            Refuse("原规划快照身份或日期变化");
        var offers = native["Offers"]!.AsArray();
        foreach (var offer in planning["offers"]!.AsArray())
        {
            var matches = offers.Where(o => JsonNode.DeepEquals(o!["Shop"], offer!["shop"]) && JsonNode.DeepEquals(o["Product"], offer["product"])).ToArray();
            if (matches.Length != 1 || !JsonNode.DeepEquals(matches[0]!["Remaining"], offer!["remaining"]) || !JsonNode.DeepEquals(matches[0]!["Price"], offer["price"]))
                Refuse("商店库存或原报价变化");
        }
        long baseline = job["baseline_at"] == null ? DailyEvidence.Integer(job["at"]) : DailyEvidence.Integer(job["baseline_at"]), at = DailyEvidence.Integer(evidence["AtUtcTicks"]);
        long gold = 0, potions = 0;
        var gains = new Dictionary<string, long>();
        var receipts = new JsonArray();
        var sequences = new HashSet<string>();
        var connections = new HashSet<string>();
        bool nonFoodReward = false;
        void Reward(JsonNode? node)
        {
            if (node is JsonArray array)
            {
                foreach (var child in array)
                    Reward(child);
                return;
            }
            if (node is not JsonObject row)
                return;
            if (row["type"] != null && row["count"] != null)
            {
                long type = DailyEvidence.Integer(row["type"]), count = DailyEvidence.Integer(row["count"]);
                if (count < 0)
                    Refuse("奖励含有资源扣减");
                switch (type)
                {
                    case 4:
                        gold = checked(gold + count);
                        break;
                    case 12:
                        potions = checked(potions + count);
                        break;
                    case 5:
                        string item = DailyEvidence.Integer(row["id"]).ToString();
                        gains[item] = checked(gains.GetValueOrDefault(item) + count);
                        break;
                    default:
                        nonFoodReward |= count > 0;
                        break;
                }
                return;
            }
            foreach (var pair in row)
                if (pair.Key != "viewItemInfo")
                    Reward(pair.Value);
        }
        foreach (var op in operations)
        {
            if (op["account"]?.GetValue<string>() != account)
                continue;
            string role = op["role"]?.GetValue<string>() ?? "";
            var scope = op["scope"] as JsonObject;
            bool trade = role.StartsWith("trade.", StringComparison.Ordinal) || role == "dispatch.start" && (scope?["trade_session"] != null || scope?["trade"] != null);
            if (scope?["trade_session"]?.GetValue<string>() == id || trade && DailyEvidence.Integer(op["at"]) >= DailyEvidence.Integer(job["at"]))
                Refuse("已有跑商请求或回执");
            long stamp = DailyEvidence.Integer(op["at"]);
            if (stamp <= baseline)
                continue;
            if (stamp >= at || op["state"]?.GetValue<string>() is "prepared" or "dispatching" or "unknown")
                Refuse("后续业务尚未确认");
            if (role is not ("event_rewards.1" or "event_rewards.2" or "event_rewards.4" or "missions.clear" or "pass.claim_all" or "mail.collect"))
                continue;
            if (op["state"]?.GetValue<string>() != "completed" || !JsonNode.DeepEquals(op["player"], original["player"]) || !JsonNode.DeepEquals(op["server"], original["server"]) || !JsonNode.DeepEquals(op["cycle"], original["cycle"]))
                Refuse("奖励回执身份或状态变化");
            var before = op["before"]!.AsObject();
            var after = op["after"]!.AsObject();
            var historic = before["Frame"]!.AsObject();
            // Completed receipts retain their own connection identity. A new queue
            // may reconnect the same game process; every frame within this receipt
            // must still belong to that one historical connection, and the current
            // bound driver continues rejecting any connection change during review.
            if (!DailyEvidence.SameActor(historic, after["Frame"]!.AsObject()) || new[] { "ProcessId", "ProcessStartTicks", "AccountKey", "PlayerKey" }.Any(k => historic[k] == null || !JsonNode.DeepEquals(historic[k], frame[k])) || !JsonNode.DeepEquals(before["Config"], after["Config"]) || before["Error"]?.GetValue<string>() != "" || after["Error"]?.GetValue<string>() != "")
                Refuse("奖励观察身份或配置变化");
            string connection = historic["Instance"]!.GetValue<string>();
            connections.Add(connection);
            var events = op["events"]!.AsArray();
            var responses = events.Where(e => e?["Kind"]?.GetValue<string>() == "response").ToArray();
            if (responses.Length == 0)
                Refuse("奖励缺少原生响应");
            foreach (var response in responses)
            {
                string responseRole = response!["Role"]!.GetValue<string>();
                long sequence = DailyEvidence.Integer(response["Sequence"]);
                if (!sequences.Add(connection + ":" + sequence) || response["Accepted"]?.GetValue<bool>() != true || response["Error"]?.GetValue<string>() != "" || DailyEvidence.Integer(response["ErrorCode"]) != 0 || !DailyEvidence.SameActor(response["Frame"]!.AsObject(), historic) || !events.Any(e => e?["Kind"]?.GetValue<string>() == "request" && e["Role"]?.GetValue<string>() == responseRole && DailyEvidence.Integer(e["Sequence"]) < sequence && e["Frame"] is JsonObject request && DailyEvidence.SameActor(request, historic)))
                    Refuse("奖励原生响应无法确认");
                if (responseRole is not ("missions.clear" or "missions.section" or "pass.reward" or "rewards.roulette" or "rewards.exchange" or "mail.collect"))
                    Refuse("奖励包含未知响应");
                foreach (var pair in DailyEvidence.Values(response.AsObject()))
                    if (pair.Key is "RewardInfoBundle" or "ItemInfo" or "RouletteReward" or "RouletteAccumulatedReward")
                        Reward(pair.Value);
            }
            receipts.Add(op["id"]!.DeepClone());
        }
        if (gold == 0 && potions == 0 && gains.Values.All(v => v == 0))
            Refuse("库存变化没有对应的已确认奖励");
        if (checked(DailyEvidence.Integer(initial["gold"]) + gold) != DailyEvidence.Integer(current["gold"]) || checked(DailyEvidence.Integer(initial["potions"]) + potions) != DailyEvidence.Integer(current["potions"]))
            Refuse("金币或催化剂变化不能由奖励解释");
        var expected = initial["items"]!.DeepClone().AsObject();
        foreach (var pair in gains)
            expected[pair.Key] = checked((expected[pair.Key] == null ? 0 : DailyEvidence.Integer(expected[pair.Key])) + pair.Value);
        if (!JsonNode.DeepEquals(expected, current["items"]) || !JsonNode.DeepEquals(initial["protected"], current["protected"]))
            Refuse("食材或保护标记变化不能安全接续");
        bool otherChanged = !JsonNode.DeepEquals(initial["other"], current["other"]);
        if (otherChanged && !nonFoodReward)
            Refuse("其他库存变化缺少奖励依据");
        return new()
        {
            ["engine"] = "dotnet-unsubmitted-trade-resume-v1",
            ["initial"] = current,
            ["at"] = at,
            ["gold_gain"] = gold,
            ["potions_gain"] = potions,
            ["food_gains"] = new JsonObject(gains.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, JsonValue.Create(p.Value)))),
            ["reward_receipts"] = receipts,
            ["reward_connections"] = new JsonArray(connections.Order(StringComparer.Ordinal).Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
            ["non_food_baseline_refreshed"] = otherChanged,
            ["plan_preserved"] = true,
            ["actions"] = 0,
            ["resources_spent"] = false
        };
    }
}

public sealed partial class DailyCommandDriver
{
    private JsonObject? tradeResumeCatalog; private bool reviewingTradeResume, tradeResumeReviewed;
    public JsonObject? TradeResumeProof
    {
        get; private set;
    }
    public void ConfigureTradeResume(JsonObject? catalog)
    {
        tradeResumeCatalog = catalog?.DeepClone().AsObject();
        tradeResumeReviewed = false;
        TradeResumeProof = null;
    }
    private async Task ReviewTradeOnDemandAsync(DailyStageFrame observed)
    {
        // The native stock reader is available only after ShopUI is initialized.
        // Run on the worker's first shop evidence demand, before its wallet check
        // or any bargain/purchase, then let DemandAsync restore the requested scope.
        if (!TradeStageActive || tradeResumeCatalog == null || tradeResumeReviewed || reviewingTradeResume || !DailyNavigationDecision.Types(observed.Frame).Contains("ShopUI"))
            return;
        reviewingTradeResume = true;
        try
        {
            TradeResumeProof = await PrepareTradeResumeAsync(tradeResumeCatalog);
            tradeResumeReviewed = true;
        }
        finally { reviewingTradeResume = false; }
    }
    public async Task<JsonObject?> PrepareTradeResumeAsync(JsonObject catalog)
    {
        var bound = await ReadBound();
        string account = bound.Context["actor"]![3]!.GetValue<string>(), cycle = bound.Context["cycle"]!.GetValue<string>();
        string folder = Path.Combine(root, "trade", "executions", account, cycle), path = Path.Combine(folder, "execution.json");
        if (!File.Exists(path))
            return null;
        var job = DailyTradeCatalog.Read(path);
        if (job["state"]?.GetValue<string>() == "completed" || job["phase"]?.GetValue<string>() != "purchase" || job["operations"] is not JsonArray ops || ops.Count != 0)
            return null;
        if (!HasControl || mailbox.Read("live", "command.json") != null)
            throw new StageHostException("pending", "跑商库存核对前仍有输入；原交易保留。");
        using var lease = new FileStream(Path.Combine(folder, "baseline.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string originalHash = DailyTradeCatalog.Hash(path), planningPath = Path.Combine(folder, "planning-state.json");
        if (!File.Exists(planningPath))
            throw new StageHostException("adapter", "原交易缺少规划快照；不能刷新库存基准。");
        string business = Path.Combine(root, "live", "business");
        string[] LedgerFiles() => new[] { "business", "managed-business" }.Select(name => Path.Combine(root, "live", name)).Where(Directory.Exists).SelectMany(folder => Directory.GetFiles(folder, "*.json")).Order(StringComparer.Ordinal).ToArray();
        string LedgerHash(IEnumerable<KeyValuePair<string, string>> hashes) => DailyTradeCatalog.Fingerprint(new JsonObject(hashes.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new KeyValuePair<string, JsonNode?>(p.Key, JsonValue.Create(p.Value)))));
        try
        {
            var before = await EvidenceAsync(["trade.currency", "trade.inventory", "trade.bargain", "trade.native"]);
            if (DailyTradeResume.MatchesInitial(job, before))
                return null;
            // A stopped, never-submitted job may need one historical reward review.
            // Parse its ledger once; the final stability check hashes stored bytes
            // instead of parsing and cloning every historical receipt again.
            var ledger = new List<JsonObject>();
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string file in LedgerFiles())
            {
                var bytes = File.ReadAllBytes(file);
                ledger.Add(JsonNode.Parse(bytes)!.AsObject());
                hashes[file] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            }
            string ledgerHash = LedgerHash(hashes);
            DailyTradeData.CheckPending(ledger, bound.Context);
            var proof = DailyTradeResume.Evaluate(job, bound.Context, catalog, before, DailyTradeCatalog.Read(planningPath), ledger);
            if (proof == null)
                return null;
            foreach (string role in new[] { "trade.buy", "trade.sell", "trade.cook", "dispatch.start" })
                if (CollectEvents(role, DailyEvidence.Integer(job["at"])).Any(e => e?["Kind"]?.GetValue<string>() == "request" && e["Frame"]?["AccountKey"]?.GetValue<string>() == account))
                    throw new InvalidDataException("原生记录已有资源操作；未刷新交易基准。");
            var after = await EvidenceAsync(["trade.currency", "trade.inventory", "trade.bargain", "trade.native"]);
            DailyTradeClose.Unchanged(before, after);
            if (!JsonNode.DeepEquals(DailyEvidence.Reading(before, "trade.native", "$self")!["Offers"], DailyEvidence.Reading(after, "trade.native", "$self")!["Offers"]) || !JsonNode.DeepEquals(bound.Context, (await ReadBound()).Context) || DailyTradeCatalog.Hash(path) != originalHash || mailbox.Read("live", "command.json") != null)
                throw new InvalidDataException("核对期间原交易或现场变化。");
            if (ledgerHash != LedgerHash(LedgerFiles().Select(file => new KeyValuePair<string, string>(file, DailyTradeCatalog.Hash(file)))))
                throw new InvalidDataException("核对期间业务回执变化。");
            SubmissionGuard?.Invoke();
            if (stopped() || mailbox.Read("live", "pause") != null)
                throw new StageHostException("stopped", "已停止；原交易库存基准保留。");
            string history = Path.Combine(folder, "baseline-history", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(history);
            File.Copy(path, Path.Combine(history, "execution-before.json"));
            proof["context"] = bound.Context.DeepClone();
            proof["trade_session"] = job["id"]!.DeepClone();
            proof["before"] = before;
            proof["after"] = after;
            proof["original_sha256"] = originalHash;
            proof["state"] = "prepared";
            string proofPath = Path.Combine(history, "proof.json");
            DailyJson.Write(proofPath, proof);
            job["initial_original"] ??= job["initial"]!.DeepClone();
            job["initial"] = proof["initial"]!.DeepClone();
            job["baseline_at"] = proof["at"]!.DeepClone();
            job["baseline_updates"] ??= new JsonArray();
            job["baseline_updates"]!.AsArray().Add(proofPath);
            DailyJson.Write(path, job);
            proof["state"] = "completed";
            DailyJson.Write(proofPath, proof);
            return proof;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or InvalidOperationException or System.Text.Json.JsonException or ArgumentException or KeyNotFoundException or OverflowException or FormatException) { throw new StageHostException("adapter", error.Message); }
    }
}

