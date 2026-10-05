using System.Text.Json.Nodes;
using static BD2Daily.DailyData;
namespace BD2Daily;

public static class DailyWeeklyBook
{
    public const string Skip = "$pointer/HUD/ButtonLayout/Button - Skip - PVP/Button - Skip";
    public static DailyBusinessProof Proof() => new("weekly.book.start", "weekly_book", ["weekly.book", "missions.cache", "reward.presentation"], (op, events, after) => Verify(op, events, after), ["weekly.book.start", "weekly.book.end"]);
    public static (JsonArray Fighters, Dictionary<string, int> Cheering) Deck(JsonArray rows)
    {
        Require(Rows(rows).All(r => N(r["playType"]) is 0 or 1), "Unknown Total War deck format");
        return (Array(Rows(rows).Where(r => N(r["playType"]) == 0)), Rows(rows).Where(r => N(r["playType"]) == 1).GroupBy(r => Canonical(r)).ToDictionary(g => g.Key, g => g.Count()));
    }
    private static string Canonical(JsonNode? n) => n is JsonObject o ? "{" + string.Join(",", o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => System.Text.Json.JsonSerializer.Serialize(p.Key) + ":" + Canonical(p.Value))) + "}" : n is JsonArray a ? "[" + string.Join(",", a.Select(Canonical)) + "]" : n?.ToJsonString() ?? "null";
    public static JsonObject Verify(JsonObject op, JsonArray events, JsonObject after)
    {
        var before = op["before"]!.AsObject();
        Response("weekly.book.start", events, before, after);
        var r = Response("weekly.book.end", events, before, after)!;
        var tables = Rows(op["scope"]!["definitions"]);
        var old = DailyWeeklyMission.Progress(before, DailyWeeklyMission.Book, tables);
        var current = DailyWeeklyMission.Progress(after, DailyWeeklyMission.Book, tables);
        Require(!B(old["complete"]) && B(current["complete"]), "Weekly Total War progress not confirmed");
        var scores = Rows(r["ScoreInfo"]);
        Require(scores.Length > 0, "Total War score missing");
        var b = Deck(R(before, "weekly.book.deck", "$self")!.AsArray());
        var a = Deck(R(after, "weekly.book.deck", "$self")!.AsArray());
        Require(JsonNode.DeepEquals(b.Fighters, a.Fighters) && b.Cheering.All(p => a.Cheering.GetValueOrDefault(p.Key) >= p.Value), "Saved Total War deck changed");
        return O(("mission", current), ("damage", scores.Sum(r => N(r["value"]))), ("server_confirmed", true), ("native_added_cheering", a.Cheering.Sum(p => Math.Max(0, p.Value - b.Cheering.GetValueOrDefault(p.Key)))));
    }
    public static async Task Drive(DailyWorkflow w)
    {
        double end = w.Time + 180;
        bool sent = false;
        while (w.Time < end)
        {
            var frame = (await w.Observe()).Frame;
            var rows = DailyNavigationDecision.Rows(frame);
            if (rows.Any(r => S(r["Type"]) == "BattleResultUI" && DailyNavigationDecision.ReadyInput(r)))
                return;
            var ui = rows.SingleOrDefault(r => S(r["Type"]) == "BattleUI_TotalWar");
            if (!sent && ui != null && DailyNavigationDecision.Blockers(frame, "BattleUI_TotalWar", DailyNavigationPolicy.Load()).Length == 0 && Rows(ui["Targets"]).Any(t => S(t["Field"]) == Skip && B(t["Enabled"])))
            {
                var e = await w.Evidence("weekly.book", "missions.cache");
                bool ready;
                try
                {
                    ready = B(R(e, "weekly.book.hud", "_objSkipBtn.ὢὪὯὧὠὥὬὪὪὮὤ"));
                }
                catch (InvalidDataException) { ready = false; }
                if (ready)
                {
                    await w.Step("BattleUI_TotalWar", Skip, reason: "使用末日之书原生跳过");
                    sent = true;
                }
            }
            await w.Delay(200);
        }
        throw new StageHostException("pending", "末日之书尚未结算，原战斗保留，不重复入场。");
    }
    public static async Task Finish(DailyWorkflow w)
    {
        if (await w.Has("BattleResultUI") && await w.Has("BattleUI_TotalWar"))
        {
            await w.Step(O(("ui", "BattleResultUI"), ("field", "_objectWinExitButton"), ("expect", "GameFieldDefaultUI"), ("absent", "BattleResultUI"), ("timeout", 60)));
            await DailyTravel.Ready(w, "GameFieldDefaultUI", 60, ["BattleUI_TotalWar"]);
        }
        await w.Home("weekly_book");
    }
    public static async Task<JsonObject> Run(DailyWorkflow w)
    {
        if (!w.Settings.Weekly.Book)
            return DailyWorkflow.Skipped("disabled");
        var pending = w.Business.Records(w.Context, "weekly.book.start").Where(DailyManagedBusiness.Pending).ToArray();
        if (pending.Length > 0)
        {
            if (await w.Has("BattleUI_TotalWar"))
            {
                Require(pending.Length == 1 && DailyEvidence.SameActor(pending[0]["before"]!["Frame"]!.AsObject(), (await w.Observe()).Frame), "Total War battle does not match original operation");
                await Drive(w);
            }
            await w.Business.ReconcileAsync(w.Context);
            w.Business.RequireResolved(w.Context, "weekly.book.start");
            var completed = w.Business.Records(w.Context, "weekly.book.start").Single(op => S(op["id"]) == S(pending[0]["id"]));
            await Finish(w);
            return O(("state", "completed"), ("recovered", true), ("operation", completed["id"]), ("result", completed["result"]));
        }
        await w.Refresh();
        var progress = await DailyWeeklyMission.Read(w, DailyWeeklyMission.Book);
        if (B(progress["complete"]))
        {
            if (await w.Has("BattleResultUI") && await w.Has("BattleUI_TotalWar"))
                await Finish(w);
            return O(("state", "skipped"), ("reason", "本周末日之书任务已完成"), ("mission", progress));
        }
        if (!await DailyTravel.Reuse(w, packId: 3005, surfaces: ["GameFieldDefaultUI", "TotalWarUI"]))
        {
            await w.Home("weekly_book");
            await w.Step("MenuUI", operation: "weekly_book_open");
            Require(await DailyTravel.Reuse(w, packId: 3005, surfaces: ["GameFieldDefaultUI", "TotalWarUI"], waitForTarget: true), "Total War cartridge not reached");
        }
        double end = w.Time + 60;
        while (!await w.Has("TotalWarUI"))
        {
            Require(w.Time < end, "Total War lobby not ready");
            if (await w.Has("PackInfoUI"))
            {
                await w.Step("PackInfoUI", "_objBackButton", absent: "PackInfoUI");
                continue;
            }
            if (await w.Has("MenuUI"))
            {
                await w.Step("MenuUI", back: true, absent: "MenuUI");
                continue;
            }
            var e = await w.Evidence("weekly.book");
            if (Readings(e, "weekly.book.gate").Any(r => B(r["gameObject.activeInHierarchy"])) && await w.Has("GameFieldDefaultUI"))
                await w.Step("GameFieldDefaultUI", operation: "weekly_book_lobby", expect: "TotalWarUI");
            else
                await w.Delay(200);
        }
        await DailyTravel.Ready(w, "TotalWarUI");
        var evidence = await w.Evidence("weekly.book", "missions.cache");
        var deck = R(evidence, "weekly.book.deck", "$self")!.AsArray();
        Require(deck.Count == N(R(evidence, "weekly.book.deck", "Count")), "Saved Total War deck incomplete");
        if (Deck(deck).Fighters.Count == 0)
        {
            await w.Step("TotalWarUI", back: true, absent: "TotalWarUI");
            await w.Home("weekly_book");
            return DailyWorkflow.Partial("请先在游戏内保存末日之书编队，之后会自动沿用。");
        }
        await w.WaitEvidence(["weekly.book"], e => B(R(e, "weekly.book.ui", "_buttonBattleEnter._goEnable.gameObject.activeInHierarchy")));
        var op = await w.Transact("weekly.book.start", O(("mission", progress["id"]), ("definitions", Array(w.Table("policy/MissionTable.json")))), O(("ui", "TotalWarUI"), ("field", "_buttonBattleEnter._goEnable")), 25, () => Drive(w));
        await Finish(w);
        return O(("state", "completed"), ("operation", op["id"]), ("result", op["result"]));
    }
}
public static class DailyWeeklyRooms
{
    public const string Index = "ὡὧὧὬὨὬὥὨὦὠὤ", Lock = "ὥὨὪὯὢὮὯὡὫὬὧ", Like = "_btnLike.ὢὪὯὧὠὥὬὪὪὮὤ", Tab = "_myRoomVisitUI.ὦὬὠὪὫὡὣὤὠὮὨ";
    public static DailyBusinessProof Proof() => new("room.like", "weekly_room_likes", ["weekly.room", "missions.cache"], (op, events, after) => Verify(op["before"]!.AsObject(), events, after, N(op["scope"]!["owner"])));
    public static JsonObject Verify(JsonObject before, JsonArray events, JsonObject after, long owner)
    {
        var receipt = Response("room.like", events, before, after)!;
        var old = Rows(R(before, "weekly.room.list", "Values")).ToDictionary(r => N(r["ownerIndex"]));
        var current = Rows(R(after, "weekly.room.list", "Values")).ToDictionary(r => N(r["ownerIndex"]));
        Require(old.ContainsKey(owner) && current.ContainsKey(owner), "Liked room owner disappeared");
        var a = current[owner];
        var b = old[owner];
        Require(N(a["myRoomLikeCount"]) == N(receipt["TotalCount"]) && N(a["myRoomLikeCount"]) == N(b["myRoomLikeCount"]) + 1 && N(a["myRoomLikeDate"]) == N(receipt["Date"]), "Room like cache differs");
        return O(("owner", owner), ("name", a["userId"]), ("before", b["myRoomLikeCount"]), ("after", a["myRoomLikeCount"]), ("cache_matched", true));
    }
    public static JsonObject[] Rank(JsonObject[] rows) => rows.OrderBy(r => N(r["myRoomLikeCount"])).ThenBy(r => N(r["ownerIndex"])).ToArray();
    private static async Task<JsonObject> Ready(DailyWorkflow w, int? index = null)
    {
        double? since = null;
        return await w.WaitEvidence(["weekly.room", "missions.cache"], e => { var ui = DailyNavigationDecision.Rows(e["Frame"]!.AsObject()).SingleOrDefault(r => S(r["Type"]) == "MyRoomUI"); bool ready = ui != null && DailyNavigationDecision.ReadyInput(ui) && Rows(ui["Targets"]).Any(t => S(t["Field"]) == "_btnLike") && !B(R(e, "weekly.room.ui", Lock)) && (index == null || N(R(e, "weekly.room.controller", Index)) == index); if (!ready) { since = null; return false; } since ??= w.Time; return w.Time - since >= .4; }, 35, "小屋访问页尚未就绪");
    }
    public static async Task<JsonObject> Run(DailyWorkflow w)
    {
        await w.Refresh();
        var evidence = await w.Evidence("missions.cache");
        var progress = DailyWeeklyMission.Progress(evidence, DailyWeeklyMission.Likes, w.Table("policy/MissionTable.json"));
        int count = B(progress["complete"]) ? 0 : checked((int)(N(progress["required"]) - N(progress["progress"])));
        if (count == 0)
            return DailyWeeklyMission.Skipped("weekly_likes_complete", progress);
        if (!await w.Has("MyRoomUI"))
        {
            await w.Home("weekly_room_likes");
            await w.Step("MenuUI", "_buttonMyRoom", expect: "MyRoomUI");
        }
        evidence = await w.Evidence("weekly.room");
        if (S(R(evidence, "weekly.room.ui", Tab)) != "RANDOM")
        {
            await w.Step("MyRoomUI", "_btnVisit");
            await Ready(w);
            await w.Step("MyRoomUI", "$pointer/UIRoot/Mask/Main/Tab - Visit/SubTab - Visit/Layout/Item - Random");
        }
        evidence = await Ready(w);
        Require(S(R(evidence, "weekly.room.ui", Tab)) == "RANDOM", "Not random recommendations");
        var rows = Rows(R(evidence, "weekly.room.list", "Values"));
        Require(rows.Length == N(R(evidence, "weekly.room.list", "Count")) && rows.Length <= 100, "Incomplete room recommendations");
        var operations = new JsonArray();
        foreach (var row in Rank(rows))
        {
            if (operations.Count >= count)
                break;
            long owner = N(row["ownerIndex"]);
            int index = System.Array.FindIndex(rows, r => N(r["ownerIndex"]) == owner);
            bool reached = false;
            for (int i = 0; i <= rows.Length; i++)
            {
                evidence = await Ready(w);
                var current = Rows(R(evidence, "weekly.room.list", "Values"));
                Require(current.Select(r => N(r["ownerIndex"])).SequenceEqual(rows.Select(r => N(r["ownerIndex"]))), "Recommendation list changed");
                int selected = I(R(evidence, "weekly.room.controller", Index));
                if (selected == index)
                {
                    reached = true;
                    break;
                }
                int delta = index > selected ? 1 : -1;
                await w.Step("MyRoomUI", "$pointer/UIRoot/Mask/Main/Tab - Visit/Object - UserName/Button - " + (delta > 0 ? "Next" : "Prev"));
                await Ready(w, selected + delta);
            }
            Require(reached, "Target room not reached");
            if (B(R(evidence, "weekly.room.ui", Like)))
                continue;
            var op = await w.Transact("room.like", O(("owner", owner), ("source", "random_lowest")), O(("ui", "MyRoomUI"), ("field", "_btnLike")));
            operations.Add(O(("id", op["id"]), ("result", op["result"])));
        }
        await w.Step("MyRoomUI", back: true, expect: "MenuUI");
        var mission = operations.Count == count ? await DailyWeeklyMission.Confirm(w, DailyWeeklyMission.Likes) : await DailyWeeklyMission.Read(w, DailyWeeklyMission.Likes);
        return O(("state", B(mission["complete"]) ? "completed" : "partial"), ("mission", mission), ("operations", operations), ("candidates", Array(Rank(rows).Select(r => O(("owner", r["ownerIndex"]), ("likes", r["myRoomLikeCount"]))))));
    }
}
