using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public static class DailyHunting
{
    public const string Ui = "HuntOrAirwayUI", TablePath = "ὫὠὡὥὣὠὪὥὧὮὣ", CountPath = "ὡὤὪὧὣὠὥὢὬὯὫ", SelectField = "$pointer/SelectParent/Layout - Select/Object - LevelParent/Button - Hunt";
    public static readonly string[] Elements = ["fire", "water", "wind", "light", "dark"];
    public static readonly string[] Currencies = ["FreeHuntingAp", "BonusHuntingAp", "FreeTorchLightAp", "TorchLightAp"];
    public static DailyBusinessProof Proof() => new("hunt.dispatch", "hunting", ["hunt", "daily.currency", "missions.cache"], (op, events, after) => Verify(op["before"]!.AsObject(), events, after, op["scope"]!.AsObject()), AffectedStages: ["hunting", "daily_hunt", "stones", "daily_hunt_minimal"]);
    public static bool Needed(JsonObject e) => Cached(e, "missions.cache").FirstOrDefault(r => N(r["id"]) == 109) is not JsonObject row || !B(row["isComplete"]) && N(row["value"]) < 1;
    public static (string Field, int Target) CountStep(int current, int target, int middle = 10)
    {
        int next = current == 1 && middle > 1 ? middle : current + middle;
        return current < next && next <= target ? ("_objPlus10Button", next) : ("_objPlusButton", current + 1);
    }
    public static JsonObject Verify(JsonObject before, JsonArray events, JsonObject after, JsonObject plan)
    {
        var table = State(before, "hunt.popup", TablePath);
        Require(new[] { "groupId", "id", "packId", "typeGroupId", "apPerTime" }.All(k => N(table[k]) == N(plan["table"]![k])) && N(R(before, "hunt.popup", CountPath)) == N(plan["count"]), "Hunt route/count changed");
        Require(B(R(before, "hunt.popup", "_buttonFreeOnly.IsEnable")), "Hunt free-only flag changed");
        long count = N(plan["count"]), cost = checked(count * N(table["apPerTime"]));
        string resource = S(plan["resource"]);
        Require(Currencies.Contains(resource) && cost > 0 && cost <= N(plan["budget"]) && N(R(before, "daily.currency", resource)) >= cost, "Hunt free budget exceeded");
        int packets = checked((int)((count + 19) / 20));
        var replies = Responses("hunt.dispatch", events, before, after, packets, packets, true);
        Require(replies.All(r => r["RewardInfoBundle"] is JsonObject or JsonArray), "Missing hunt reward");
        foreach (string key in Currencies)
            Require(N(R(before, "daily.currency", key)) - N(R(after, "daily.currency", key)) == (key == resource ? cost : 0), "Hunt debit differs: " + key);
        return O(("count", count), ("cost", cost), ("resource", resource), ("table", table), ("rewards", replies.Select(r => r["RewardInfoBundle"]).ToArray()));
    }
    private static JsonObject Selected(JsonObject e) => State(e, "hunt.selection", TablePath);
    private static async Task<JsonObject> Select(DailyWorkflow w, string kind, int chapter = 9, string? element = null)
    {
        int expected = kind switch
        {
            "ordinary" => 0,
            "gold" => 1,
            "slime" => 2,
            "stone" => 3 + System.Array.IndexOf(Elements, element ?? "fire"),
            _ => throw new ArgumentException("Unknown hunt category")
        };
        bool Matches(JsonObject t) => N(t["typeGroupId"]) == expected && (kind != "ordinary" || N(t["packId"]) == chapter);
        var evidence = await w.Evidence("hunt", "daily.currency", "missions.cache");
        var selected = Selected(evidence);
        if (Matches(selected))
            return selected;
        if (kind == "ordinary")
        {
            const string pin = "ὯὯὬὥὭὫὯὭὩὫὦ";
            var rows = Readings(evidence, "hunt.pins").Where(r => N(r[pin + ".HuntPackId"]) == chapter && N(r[pin + ".HuntTypeGroupId"]) == 0).ToArray();
            Require(rows.Length == 1, "Configured ordinary chapter pin unavailable");
            string path = "/" + S(rows[0]["transform.parent.gameObject.name"]) + "/" + S(rows[0]["gameObject.name"]);
            var target = await w.Pointer("WorldMapUI", t => S(t["Route"]) == "pointer" && S(t["Field"]).EndsWith(path, StringComparison.Ordinal));
            await w.Step(O(("ui", "WorldMapUI"), ("field", target["Field"]), ("target_id", target["Id"]), ("reason", "选择设置中的章节狩猎")));
        }
        else
        {
            string tab = kind switch
            {
                "gold" => "2 - Goblin",
                "slime" => "3 - Slime",
                _ => "4 - Crystal"
            };
            await w.Step(Ui, "$pointer/Layout - Button/Button - " + tab, reason: "选择狩猎分类");
            await w.WaitEvidence(["hunt", "daily.currency", "missions.cache"], e => kind == "stone" ? N(Selected(e)["typeGroupId"]) >= 3 : N(Selected(e)["typeGroupId"]) == expected);
            if (kind == "stone" && element != null)
                await w.Step(Ui, "$pointer/SelectParent/Layout - Select/Tab - Element/UIScrollView - Element/UIViewport/UIContent/Item - Element - " + char.ToUpperInvariant(element[0]) + element[1..] + "/Button - Element", reason: "选择圣石属性");
        }
        return Selected(await w.WaitEvidence(["hunt", "daily.currency", "missions.cache"], e => Matches(Selected(e))));
    }
    private static async Task SetCount(DailyWorkflow w, int count)
    {
        Require(count > 0, "Positive hunt count required");
        var e = await w.Evidence("hunt", "daily.currency", "missions.cache");
        if (!B(R(e, "hunt.popup", "_buttonFreeOnly.IsEnable")))
        {
            await w.Step("HuntDispatchPopupUI", "_buttonFreeOnly._objectRoot", reason: "仅使用免费次数");
            e = await w.WaitEvidence(["hunt", "daily.currency", "missions.cache"], x => B(R(x, "hunt.popup", "_buttonFreeOnly.IsEnable")));
        }
        int max = I(R(e, "hunt.popup", "_sliderAutoCount.ὩὧὩὦὫὮὨὢὦὠὯ")), middle = I(R(e, "hunt.popup", "_sliderAutoCount.ὧὬὩὬὢὦὩὥὥὩὢ"));
        Require(count <= max, "Requested hunt exceeds native free maximum");
        if (N(R(e, "hunt.popup", CountPath)) == count)
            return;
        if (count == max)
        {
            await w.Step("HuntDispatchPopupUI", "_sliderAutoCount._objPlusMaxButton", reason: "一次用完当前免费次数");
            await w.WaitEvidence(["hunt", "daily.currency", "missions.cache"], x => N(R(x, "hunt.popup", CountPath)) == count);
            return;
        }
        await w.Step("HuntDispatchPopupUI", "_sliderAutoCount._objMinusMaxButton", reason: "设置免费狩猎数量");
        await w.WaitEvidence(["hunt", "daily.currency", "missions.cache"], x => N(R(x, "hunt.popup", CountPath)) == 1);
        int current = 1;
        while (current < count)
        {
            var (field, target) = CountStep(current, count, middle);
            await w.Step("HuntDispatchPopupUI", "_sliderAutoCount." + field, reason: "设置已核对的批量次数");
            await w.WaitEvidence(["hunt", "daily.currency", "missions.cache"], x => N(R(x, "hunt.popup", CountPath)) == target);
            current = target;
        }
    }
    private static async Task<JsonObject> Batch(DailyWorkflow w, JsonObject table, int count, long budget, string resource)
    {
        var e = await w.Evidence("hunt", "daily.currency", "missions.cache");
        Require(S(R(e, "hunt.native", "ὤὫὣὡὫὤὦὯὢὡὬ")) == "ABLE", "Native hunt route unavailable");
        await w.Step(Ui, SelectField, expect: "HuntDispatchPopupUI", reason: "预览免费狩猎");
        await w.WaitEvidence(["hunt", "daily.currency", "missions.cache"], x => N(State(x, "hunt.popup", TablePath)["id"]) == N(table["id"]) && N(State(x, "hunt.popup", TablePath)["groupId"]) == N(table["groupId"]));
        await SetCount(w, count);
        var plan = O(("table", table), ("count", count), ("budget", budget), ("resource", resource));
        var op = await w.Transact("hunt.dispatch", plan, O(("ui", "HuntDispatchPopupUI"), ("field", "_buttonHuntDispatch._button"), ("expect", "RewardReceivePopupUI")));
        await w.Dismiss(Ui, true);
        return O(("id", op["id"]), ("result", op["result"]));
    }
    public static async Task<JsonObject> Run(DailyWorkflow w, string stage)
    {
        var h = w.Settings.Hunt;
        bool rice = stage != "stones" && h.Enabled, stones = stage is "hunting" or "stones" && h.StonesEnabled && h.TorchLimit > 0, minimal = stage == "daily_hunt_minimal";
        if (!rice && !stones)
            return DailyWorkflow.Skipped("disabled");
        if (await w.Has("HuntDispatchPopupUI"))
            await w.Step("HuntDispatchPopupUI", back: true, absent: "HuntDispatchPopupUI", reason: "关闭尚未提交的狩猎预览");
        if (!await w.Has(Ui))
            await w.Step("MenuUI", "_goQuickHuntDispatchButton", expect: Ui, reason: "打开狩猎与圣石环节");
        await w.WaitEvidence(["hunt", "daily.currency", "missions.cache"], e => Selected(e) != null);
        var operations = new JsonArray();
        var pending = new JsonArray();
        if (rice)
        {
            await w.Refresh(Ui);
            var e = await w.Evidence("hunt", "daily.currency", "missions.cache");
            bool required = Needed(e);
            var bonuses = new Dictionary<string, bool>();
            foreach (var pair in new[] { ("gold", "_tabButtonGoblin._goBonus.activeInHierarchy"), ("slime", "_tabButtonSlime._goBonus.activeInHierarchy") })
            {
                var value = R(e, "hunt.native", pair.Item2);
                Require(value is JsonValue v && v.TryGetValue<bool>(out _), "Hunt bonus unobserved");
                bonuses[pair.Item1] = B(value);
            }
            string chosen = minimal ? "ordinary" : h.Priority.First(k => k == "ordinary" || (k == "gold" ? h.FarmGold : h.FarmSlime) && bonuses[k]);
            if (required || chosen == "ordinary" && !minimal)
            {
                var table = await Select(w, "ordinary", h.OrdinaryChapter);
                long free = N(R(await w.Evidence("hunt", "daily.currency", "missions.cache"), "daily.currency", "FreeHuntingAp"));
                int cost = I(table["apPerTime"]);
                Require(cost > 0, "Invalid hunt AP cost");
                int count = chosen == "ordinary" && !minimal ? checked((int)(free / cost)) : free >= cost ? 1 : 0;
                if (count > 0)
                {
                    operations.Add(await Batch(w, table, count, free, "FreeHuntingAp"));
                    if (required)
                    {
                        await w.Refresh(Ui, true);
                        if (Needed(await w.Evidence("hunt", "daily.currency", "missions.cache")))
                            pending.Add("ordinary_daily_not_confirmed");
                    }
                }
                else if (required)
                    pending.Add("ordinary_daily_insufficient_free_rice");
            }
            if (chosen != "ordinary" && pending.Count == 0)
            {
                long free = N(R(await w.Evidence("hunt", "daily.currency", "missions.cache"), "daily.currency", "FreeHuntingAp"));
                if (free > 0)
                {
                    var table = await Select(w, chosen, h.OrdinaryChapter);
                    int cost = I(table["apPerTime"]);
                    Require(cost > 0, "Invalid hunt AP cost");
                    int count = checked((int)(free / cost));
                    if (count > 0)
                        operations.Add(await Batch(w, table, count, free, "FreeHuntingAp"));
                }
            }
        }
        if (stones)
        {
            var evidence = await w.Evidence("hunt", "daily.currency", "missions.cache");
            long spent = w.Business.Records(w.Context, "hunt.dispatch").Where(r => S(r["state"]) == "completed" && JsonNode.DeepEquals(r["cycle"], w.Context["cycle"]) && S(r["result"]?["resource"]) == "FreeTorchLightAp").Sum(r => N(r["result"]?["cost"]));
            long budget = Math.Min(N(R(evidence, "daily.currency", "FreeTorchLightAp")), Math.Max(0, h.TorchLimit - spent));
            if (budget > 0)
            {
                await w.Step(Ui, "$pointer/Layout - Button/Button - 4 - Crystal", reason: "读取五种圣石库存");
                var e = await w.WaitEvidence(["hunt", "daily.currency", "missions.cache"], x => N(Selected(x)["typeGroupId"]) >= 3);
                const string owned = "ὬὩὨὮὥὫὧὪὥὥὨ";
                var stock = Readings(e, "hunt.owned").ToDictionary(r => I(r[owned + ".ὯὫὪὡὭὤὪὮὫὬὣ"]), r => N(r[owned + ".ὮὢὥὯὥὧὤὭὨὨὪ"]));
                Require(Enumerable.Range(111, 5).All(stock.ContainsKey), "Stone inventory incomplete");
                string element = h.StoneElement == "least" ? Elements[Enumerable.Range(0, 5).OrderBy(i => stock[111 + i]).ThenBy(i => i).First()] : h.StoneElement;
                var table = await Select(w, "stone", element: element);
                int cost = I(table["apPerTime"]);
                Require(cost > 0, "Invalid torch cost");
                int count = checked((int)(budget / cost));
                if (count > 0)
                    operations.Add(await Batch(w, table, count, budget, "FreeTorchLightAp"));
            }
        }
        await w.Step(Ui, "_objBackButton", expect: "MenuUI", reason: "免费狩猎已核账");
        var result = O(("state", pending.Count > 0 ? "partial" : "completed"), ("engine", "dotnet-hunting-v1"), ("operations", operations), ("pending", pending));
        if (pending.Count > 0)
            result["reason"] = "章节狩猎任务未完成或服务器进度尚未确认，未继续消耗白饭";
        w.Save("hunt-latest.json", result);
        return result;
    }
}
