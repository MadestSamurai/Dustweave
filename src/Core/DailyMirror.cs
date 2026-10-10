using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public static class DailyMirror
{
    private const string Auto = "BattleAutoSettingPopupUI", Boost = "BattleAutoCurrencyAccelSettingPopupUI", BoostValue = "ὣὤὥὪὥὡὥὢὧὮὠ", Count = "ὯὠὭὣὮὠὯὬὤὯὠ", Slider = "_sliderCount.ὮὢὥὯὥὧὤὭὨὨὪ";
    private static readonly string[] RankWindows = ["PVPClassUpUI", "PVPHistoryUI", "PVPSeasonRewardPopupUI"];
    private static readonly string[] ReadyPaths = ["ὣὤὥὦὯὦὩὤὨὠὪ", "ὪὯὣὥὬὫὬὩὠὮὠ", "ὮὬὧὦὣὠὠὤὮὦὧ"];
    public static DailyBusinessProof Proof() => new("mirror.end", "mirror", ["mirror"],
        (op, events, after) => Verify(op["before"]!.AsObject(), events, after, I(op["scope"]!["multiplier"]), BatchCount(op)),
        ["mirror.matching", "mirror.end"], CanResume: (op, current) =>
            JsonNode.DeepEquals(op["cycle"], current.Context["cycle"]) &&
            DailyEvidence.SameActor(op["before"]!["Frame"]!.AsObject(), current.Frame) &&
            S(current.Frame["Scene"]) == "Map3001_001" &&
            (DailyNavigationDecision.Phase(current.Frame, "mirror") == "owned_battle" ||
             DailyNavigationDecision.Types(current.Frame).Overlaps(["BattleUI_PVP", "PVPMatchingUI", "PVPAutoHistoryPopupUI", "BattleResultUI"])));
    // Old journals represent a single battle and remain readable after upgrading.
    private static int BatchCount(JsonObject op) => op["scope"]?["repetitions"] == null ? 1 : I(op["scope"]!["repetitions"]);
    private static double BatchTimeout(int repetitions) => Math.Min(1800, 120d * repetitions + 30);
    public static JsonObject Tickets(JsonObject e) => O(("free", R(e, "mirror.currency", "PvpTicket")), ("paid", R(e, "mirror.currency", "PvpTicketStack")));
    public static int Allocation(int multiplier, int free)
    {
        Require(multiplier is >= 1 and <= 40 && free >= 0, "Invalid Mirror free budget");
        return Math.Min(multiplier, free);
    }
    public static void Debit(JsonObject before, JsonObject after, int cost)
    {
        Require(new[] { "free", "paid" }.All(k => before[k] != null && after[k] != null && N(before[k]) >= 0 && N(after[k]) >= 0), "Mirror tickets incomplete");
        Require(N(before["free"]) - N(after["free"]) == cost && N(before["paid"]) == N(after["paid"]), "Mirror ticket debit differs; paid tickets must not change");
    }
    public static void ValidateReady(JsonObject e, int cost) => ValidateReady(e, cost, 1);
    public static void ValidateReady(JsonObject e, int cost, int repetitions)
    {
        Require(cost is >= 1 and <= 40 && repetitions > 0 && (long)cost * repetitions <= int.MaxValue, "Invalid Mirror free budget");
        Require(N(R(e, "mirror.auto", BoostValue)) == cost && N(R(e, "mirror.auto", Count)) == repetitions && B(R(e, "mirror.auto", "_buttonFreeOnly.IsEnable")) && N(Tickets(e)["free"]) >= (long)cost * repetitions, "Mirror multiplier/repetitions/free-only changed");
    }
    public static JsonObject Verify(JsonObject before, JsonArray events, JsonObject after, int cost) => Verify(before, events, after, cost, 1);
    public static JsonObject Verify(JsonObject before, JsonArray events, JsonObject after, int cost, int repetitions)
    {
        ValidateReady(before, cost, repetitions);
        Responses("mirror.matching", events, before, after, repetitions, repetitions, alternating: true);
        var results = Responses("mirror.end", events, before, after, repetitions, repetitions, alternating: true);
        var order = Rows(events).Where(e => S(e["Role"]) is "mirror.matching" or "mirror.end").OrderBy(e => N(e["Sequence"]));
        Require(order.Select(e => S(e["Role"]) + ":" + S(e["Kind"])).SequenceEqual(Enumerable.Range(0, repetitions).SelectMany(_ =>
            new[] { "mirror.matching:request", "mirror.matching:response", "mirror.end:request", "mirror.end:response" })), "Mirror batch response order differs");
        int total = checked(cost * repetitions);
        Debit(Tickets(before), Tickets(after), total);
        return O(("cost", total), ("multiplier", cost), ("repetitions", repetitions), ("remaining", Tickets(after)),
            ("native_result", results[^1]), ("native_results", Array(results)));
    }
    public static string BoostButton(int delta) => delta == 0 ? throw new ArgumentException("Multiplier already selected") : delta >= 10 ? "_objPlus10Button" : delta <= -10 ? "_objMinus10Button" : delta > 0 ? "_objPlusButton" : "_objMinusButton";
    private static async Task<JsonObject> Target(DailyWorkflow w, string ui, string? field = null, string? suffix = null)
    {
        var rows = DailyNavigationDecision.Rows((await w.Observe()).Frame).Where(r => S(r["Type"]) == ui).ToArray();
        Require(rows.Length == 1, "Mirror surface unavailable: " + ui);
        var choices = Rows(rows[0]["Targets"]).Where(t => B(t["Enabled"]) && (field != null ? S(t["Field"]) == field : S(t["Field"]).EndsWith(suffix!, StringComparison.Ordinal))).ToArray();
        Require(choices.Length > 0 && choices.Select(t => S(t["Id"])).Distinct().Count() == 1, "Mirror target is ambiguous");
        return choices[0];
    }
    private static async Task Click(DailyWorkflow w, string ui, string? field = null, string? suffix = null, string? expect = null, string? absent = null)
    {
        var t = await Target(w, ui, field, suffix);
        var action = O(("ui", ui), ("field", t["Field"]), ("target_id", t["Id"]), ("reason", "推进原生镜中日常"));
        if (ui is Auto or Boost)
            action["native"] = true;
        if (expect != null)
            action["expect"] = expect;
        if (absent != null)
            action["absent"] = absent;
        await w.Step(action);
    }
    private static bool PresentationReady(JsonObject e, string ui) =>
        ReadyPaths.All(p => B(R(e, "mirror.presentation." + ui, p))) &&
        (!B(R(e, "mirror.presentation." + ui, "ὫὥὧὬὭὫὡὠὠὪὠ")) || !B(R(e, "mirror.presentation." + ui, "ὦὡὮὫὧὠὮὡὭὭὬ")));
    private static async Task Presented(DailyWorkflow w, string ui) =>
        await w.WaitEvidence(["mirror"], e => PresentationReady(e, ui), 20, "镜中结算动画尚未结束");

    // Seasonal/rank presentation belongs to cartridge arrival as well as battle cleanup.
    // Handle one observed page per iteration so a delayed follow-up cannot deadlock the
    // outer wait for the field UI. This never selects a match or changes a ticket budget.
    public static async Task<bool> RecoverRankPresentation(DailyWorkflow w, JsonObject frame)
    {
        if (S(frame["Scene"]) != "Map3001_001") return false;
        var row = DailyNavigationDecision.Rows(frame).Where(r => RankWindows.Contains(S(r["Type"]))).OrderByDescending(r => N(r["Order"])).FirstOrDefault();
        if (row == null) return false;
        string ui = S(row["Type"]);
        var e = await w.Evidence("mirror");
        var current = e["Frame"]!.AsObject();
        var same = DailyNavigationDecision.Rows(current).SingleOrDefault(r => S(r["Type"]) == ui && N(r["Id"]) == N(row["Id"]));
        if (S(current["Scene"]) != S(frame["Scene"]) || same == null) return true;
        if (DailyNavigationDecision.Blockers(current, ui, DailyNavigationPolicy.Load()).Length != 0)
            throw new StageHostException("adapter", "镜中段位展示被其他窗口遮挡，保留现场。");
        if (!DailyNavigationDecision.ReadyInput(same) || !PresentationReady(e, ui))
        {
            await w.Delay(150);
            return true;
        }
        try
        {
            await w.Step(ui, ui == "PVPClassUpUI" ? "_objCancelButton" : null, back: ui != "PVPClassUpUI", absent: ui, reason: "结束原生段位与赛季奖励展示");
        }
        catch (DailyStepException error) when (!error.Submitted && error.Kind == "rejected" && error.RejectionCode is "surface_missing" or "target_missing")
        {
            // The page closed before input; re-observe, never repeat an uncertain click.
        }
        return true;
    }
    private static async Task Rank(DailyWorkflow w)
    {
        double end = w.Time + 45;
        while (w.Time < end)
        {
            var frame = (await w.Observe()).Frame;
            if (!DailyNavigationDecision.Types(frame).Overlaps(RankWindows)) return;
            if (!await RecoverRankPresentation(w, frame))
                throw new StageHostException("adapter", "镜中段位窗口不在镜中场景，保留现场。");
        }
        throw new StageHostException("adapter", "镜中段位窗口未结束，未重发战斗。");
    }
    private static async Task Ready(DailyWorkflow w)
    {
        double end = w.Time + 60;
        while (w.Time < end)
        {
            await Rank(w);
            await DailyTravel.Ready(w, "BattleUI_PVP");
            var e = await w.Evidence("mirror");
            var v = Readings(e, "mirror.input").Single();
            bool ready = B(v["ὨὬὬὯὫὨὦὧὨὩὭ"]) && B(v["ὤὬὮὠὨὮὣὯὩὩὡ"]) && !new[] { "ὣὩὫὪὫὨὣὣὦὯὭ", "ὯὩὮὪὦὡὠὤὬὯὭ", "ὨὠὪὠὪὠὠὬὨὪὩ", "ὧὡὭὫὯὫὨὮὡὬὨ", "ὢὩὫὪὪὦὤὨὠὬὢ", "ὡὥὩὮὢὧὢὫὡὯὢ" }.Any(p => B(v[p])) && ReadyPaths.Take(2).All(p => B(R(e, "mirror.ready", p)));
            if (ready)
            {
                await Target(w, "BattleUI_PVP", suffix: "/Button - AutoSetting");
                return;
            }
            await w.Delay(150);
        }
        throw new StageHostException("adapter", "镜中备战尚未就绪。");
    }
    private static async Task Enter(DailyWorkflow w)
    {
        await Rank(w);
        if (await w.Has("BattleUI_PVP"))
        {
            if (!await w.Has(Auto) && !await w.Has(Boost))
                await Ready(w);
            return;
        }
        if (!await DailyTravel.Enter(w, 1, "mirror.pack", packId: 3001))
            throw new StageHostException("adapter", "当前账号没有镜中卡带入口。");
        await Rank(w);
        await DailyTravel.Ready(w, "GameFieldDefaultUI");
        await w.Step(O(("ui", "GameFieldDefaultUI"), ("mirror_entry", true), ("expect", "BattleUI_PVP"), ("timeout", 60), ("reason", "进入原生镜中备战")));
        await Ready(w);
    }
    private static async Task Prepare(DailyWorkflow w, int cost, int repetitions)
    {
        await Rank(w);
        if (await w.Has(Boost))
            await w.Step(Boost, back: true, absent: Boost, expect: Auto);
        if (!await w.Has(Auto))
            await Click(w, "BattleUI_PVP", suffix: "/Button - AutoSetting", expect: Auto);
        if (N(R(await w.Evidence("mirror"), "mirror.auto", BoostValue)) != cost)
        {
            await Click(w, Auto, field: "_btnSettingBoost", expect: Boost);
            bool done = false;
            for (int i = 0; i < 45; i++)
            {
                var e = await w.Evidence("mirror");
                int current = I(R(e, "mirror.boost", Slider));
                if (current == cost)
                {
                    done = true;
                    break;
                }
                string button = BoostButton(cost - current), name = S(R(e, "mirror.boost", "_sliderCount." + button + ".name"));
                await Click(w, Boost, suffix: "/" + name);
                await w.WaitEvidence(["mirror"], x => N(R(x, "mirror.boost", Slider)) != current, 5, "镜中倍率选择未推进");
            }
            Require(done, "Mirror multiplier did not converge");
            await Click(w, Boost, field: "_btnOk", absent: Boost, expect: Auto);
            await w.WaitEvidence(["mirror"], e => N(R(e, "mirror.auto", BoostValue)) == cost);
        }
        if (!B(R(await w.Evidence("mirror"), "mirror.auto", "_buttonFreeOnly.IsEnable")))
            await Click(w, Auto, suffix: "/FreeOnly/Button - Toggle");
        if (!B(R(await w.Evidence("mirror"), "mirror.auto", "_buttonSkip.IsEnable")))
            await Click(w, Auto, suffix: "/BattleSkip/Button - Toggle");
        var configured = await w.Evidence("mirror");
        if (N(R(configured, "mirror.auto", Count)) != repetitions)
        {
            // The game's maximum is floor(free tickets / multiplier) after FreeOnly is enabled.
            string name = S(R(configured, "mirror.auto", "_sliderAutoCount._objPlusMaxButton.name"));
            Require(name.Length > 0, "Mirror continuous battle selector unavailable");
            await Click(w, Auto, suffix: "/" + name);
        }
        await w.WaitEvidence(["mirror"], e => { ValidateReady(e, cost, repetitions); return true; });
    }
    private static async Task Cleanup(DailyWorkflow w, bool reenter)
    {
        double end = w.Time + 45;
        while (w.Time < end)
        {
            var f = (await w.Observe()).Frame;
            if (await DailyTravel.RecoverPresentation(w, f))
                continue;
            var types = DailyNavigationDecision.Types(f);
            if (types.Overlaps(RankWindows))
            {
                await Rank(w);
                continue;
            }
            if (types.Contains("PVPAutoHistoryPopupUI"))
            {
                await Presented(w, "PVPAutoHistoryPopupUI");
                await w.Step("PVPAutoHistoryPopupUI", back: true, absent: "PVPAutoHistoryPopupUI", reason: "关闭已核账的自动战斗汇总");
                continue;
            }
            if (types.Contains("BattleResultUI"))
            {
                await Presented(w, "BattleResultUI");
                f = (await w.Observe()).Frame;
                var row = DailyNavigationDecision.Rows(f).FirstOrDefault(r => S(r["Type"]) == "BattleResultUI");
                if (row == null)
                    continue;
                var exits = Rows(row["Targets"]).Where(t => B(t["Enabled"]) && S(t["Field"]) is "_objectWinExitButton" or "_objectLoseExitButton").ToArray();
                if (exits.Length == 1)
                {
                    var pointer = Rows(row["Targets"]).Single(t => JsonNode.DeepEquals(t["Id"], exits[0]["Id"]) && S(t["Route"]) == "pointer");
                    try
                    {
                        await w.Step(O(("ui", "BattleResultUI"), ("field", pointer["Field"]), ("target_id", pointer["Id"]), ("reason", "退出已核账的镜中战斗")));
                    }
                    catch (DailyStepException e) when (e.Kind == "rejected" && e.Message.Contains("PVPAutoHistoryPopupUI", StringComparison.Ordinal)) { continue; }
                    await DailyTravel.Ready(w, "GameFieldDefaultUI", absent: ["BattleUI_PVP", "BattleResultUI"]);
                    if (reenter)
                        await Enter(w);
                    return;
                }
            }
            else if (types.Contains("BattleUI_PVP"))
            {
                try
                {
                    await Target(w, "BattleUI_PVP", suffix: "/Button - AutoSetting");
                    return;
                }
                catch (InvalidDataException) { }
            }
            else if (types.Contains("GameFieldDefaultUI"))
            {
                if (reenter)
                    await Enter(w);
                return;
            }
            await w.Delay(200);
        }
        throw new StageHostException("adapter", "镜中结算尚未结束；已确认的战斗不会重复执行。");
    }
    private static async Task Recover(DailyWorkflow w)
    {
        int repetitions = w.Business.Records(w.Context, "mirror.end").Where(DailyManagedBusiness.Pending).Select(BatchCount).DefaultIfEmpty(1).Max();
        double end = w.Time + BatchTimeout(repetitions);
        while (true)
        {
            var frame = await w.Observe();
            string phase = DailyNavigationDecision.Phase(frame.Frame, "mirror");
            var pending = w.Business.Records(w.Context, "mirror.end").Where(DailyManagedBusiness.Pending).ToArray();
            if (pending.Length > 0)
            {
                await w.Business.ReconcileAsync(w.Context);
                pending = w.Business.Records(w.Context, "mirror.end").Where(DailyManagedBusiness.Pending).ToArray();
            }
            if (phase != "owned_battle" && pending.Length == 0)
            {
                return;
            }
            Require(w.Business.Records(w.Context, "mirror.end").Any(r => S(r["state"]) is "dispatching" or "unknown" or "completed" && DailyEvidence.SameActor(r["before"]!["Frame"]!.AsObject(), frame.Frame)), "Active Mirror battle has no owned journal");
            if (w.Time >= end)
                throw new StageHostException("pending", "原镜中战斗仍在结算，保留现场。");
            await w.Delay(500);
        }
    }
    public static async Task<JsonObject> Run(DailyWorkflow w)
    {
        if (!w.Settings.Mirror.Enabled)
            return DailyWorkflow.Skipped("disabled");
        await Recover(w);
        var frame = await w.Observe();
        if (DailyNavigationDecision.Types(frame.Frame).Overlaps(["PVPAutoHistoryPopupUI", "BattleResultUI"]))
        {
            var prior = w.Business.Records(w.Context, "mirror.end").Where(r => JsonNode.DeepEquals(r["cycle"], w.Context["cycle"]) && DailyEvidence.SameActor(r["before"]!["Frame"]!.AsObject(), frame.Frame)).OrderByDescending(r => N(r["at"])).FirstOrDefault();
            Require(prior != null && S(prior["state"]) == "completed" && JsonNode.DeepEquals(prior["result"]?["remaining"], Tickets(await w.Evidence("mirror"))), "Mirror result lacks a confirmed settlement");
            await Cleanup(w, false);
        }
        var rounds = new JsonArray();
        int initial = I(Tickets(await w.Evidence("mirror"))["free"]);
        if (initial > 0)
            await Enter(w);
        while (true)
        {
            var before = Tickets(await w.Evidence("mirror"));
            int cost = Allocation(w.Settings.Mirror.Multiplier, I(before["free"]));
            Debit(before, before, 0);
            if (cost == 0)
                break;
            int repetitions = I(before["free"]) / cost;
            int total = checked(cost * repetitions);
            await Prepare(w, cost, repetitions);
            var t = await Target(w, Auto, field: "_currencyButtonOK");
            var op = await w.Transact("mirror.end", O(("multiplier", cost), ("repetitions", repetitions), ("free_only", true)), O(("ui", Auto), ("field", t["Field"]), ("target_id", t["Id"]), ("native", true), ("reason", "使用已核对的免费镜中次数")), BatchTimeout(repetitions));
            var after = Tickets(await w.Evidence("mirror"));
            Debit(before, after, total);
            rounds.Add(O(("cost", total), ("multiplier", cost), ("repetitions", repetitions), ("remaining_free", after["free"]), ("receipt", op["id"])));
            Require(rounds.Count <= initial, "Mirror free budget unexpectedly increased");
            await Cleanup(w, N(after["free"]) > 0);
        }
        await Rank(w);
        if (await w.Has("BattleUI_PVP"))
        {
            await w.Step("BattleUI_PVP", back: true, reason: "免费镜中次数已用完");
            await DailyTravel.Ready(w, "GameFieldDefaultUI", absent: ["BattleUI_PVP"]);
        }
        if (!await w.Has("MenuUI"))
        {
            await DailyTravel.Ready(w, "GameFieldDefaultUI", absent: ["BattleUI_PVP", "BattleResultUI"]);
            await w.Step("GameFieldDefaultUI", "_buttonMenu", expect: "MenuUI");
        }
        var result = O(("state", "completed"), ("rounds", rounds), ("free_spent", Rows(rounds).Sum(r => N(r["cost"]))), ("remaining_free", 0), ("engine", "dotnet-mirror-v1"));
        w.Save("mirror-latest.json", result);
        return result;
    }
}
