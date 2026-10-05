using System.Text.Json.Nodes;
using static BD2Daily.DailyData;
using static BD2Daily.DailyFieldRoute;
namespace BD2Daily;

public static class DailyWeeklyNpcBoard
{
    public static async Task Close(DailyWorkflow w)
    {
        var frame = (await w.Observe()).Frame;
        var popup = DailyNavigationDecision.Rows(frame).SingleOrDefault(r => S(r["Type"]) == "QuestPopupUI");
        if (popup != null)
        {
            if (!S(popup["NativeContext"]).StartsWith("weekly_npc_accept:", StringComparison.Ordinal) || !DailyNavigationDecision.Types(frame).Contains("QuestBoardUI"))
                throw new DailyNpcUnavailable("当前任务弹窗不属于公告板接取，保留现场");
            await w.Step("QuestPopupUI", back: true, absent: "QuestPopupUI");
        }
        if (await w.Has("QuestBoardUI"))
            await w.Step("QuestBoardUI", back: true, absent: "QuestBoardUI");
    }
    public static async Task Open(DailyFieldRoute route, DailyNpcData data, long pack)
    {
        var w = route.W;
        if (await w.Has("QuestBoardUI")) return;
        var boards = Rows(data.Native["Boards"] ?? new JsonArray());
        if (boards.Length == 0) throw new DailyNpcUnavailable("当前卡带没有可用公告板，未通过接口接取任务");
        long map = N(Map(await route.Evidence())["id"]);
        if (!boards.Any(b => N(b["Map"]) == map))
        {
            long destination = N(boards.OrderBy(b => N(b["Map"])).First()["Map"]);
            // Board visits use the same observed door/waypoint graph as weekly collection.
            // Walking out of an interior may already reach the board's map.
            try
            {
                var nav = new DailyCollectionNavigator(route, pack, await route.Evidence());
                await nav.GoTo(destination);
            }
            catch (DailyTravelBlocked ex)
            {
                throw new DailyNpcUnavailable("暂时无法到达公告板所在地图，保留待办：" + ex.Message);
            }
        }
        var frame = (await w.Observe()).Frame;
        string scene = S(frame["Scene"]);
        await w.Step("GameFieldDefaultUI", operation: "weekly_npc_board_nav", value: checked((int)pack));
        double end = w.Time + 245, stoppedSince = -1;
        bool near = false;
        try { while (w.Time < end)
        {
            var observed = await route.Evidence();
            var current = observed["Frame"]!.AsObject();
            if (!DailyEvidence.SameActor(frame, current) || S(current["Scene"]) != scene)
                throw new DailyNpcUnavailable("前往公告板期间账号或地图改变，未接取任务");
            var native = State(observed, "weekly_npc.native", "$self");
            near = Rows(native["Boards"] ?? new JsonArray()).Any(b => N(b["Instance"]) != 0 && B(b["Near"]));
            if (near) break;
            // Cartridge boards use the game's field-object navigation, never plaza A*.
            if (B(native["Navigating"])) stoppedSince = -1;
            else if (stoppedSince < 0) stoppedSince = w.Time;
            else if (w.Time - stoppedSince >= 3)
                throw new DailyNpcUnavailable("游戏原生导航已停止，尚未到达公告板交互范围；保留待办");
            await w.Delay(200);
        }
        if (!near) throw new DailyNpcUnavailable("前往公告板超时，未接取任务");
        } finally {
            var last = (await w.Observe()).Frame;
            if (S(last["Scene"]) == scene && DailyNavigationDecision.Types(last).Contains("GameFieldDefaultUI") && DailyNavigationDecision.Blockers(last,"GameFieldDefaultUI",DailyNavigationPolicy.Load()).Length == 0)
                await w.Step("GameFieldDefaultUI",operation:"weekly_npc_board_stop",value:checked((int)pack));
        }
        await w.Step("GameFieldDefaultUI", operation: "weekly_npc_board_open", value: checked((int)pack), expect: "QuestBoardUI");
    }
    public static async Task Select(DailyWorkflow w, Func<Task<DailyNpcData>> observe, long id)
    {
        var data = await observe();
        if (N(data.Native["Board"]?["Ui"]) == 0) throw new DailyNpcUnavailable("任务板尚未打开，未接取任务");
        bool Visible(DailyNpcData d) => (d.Native["Board"]?["VisibleQuests"] as JsonArray ?? new()).Select(N).Contains(id);
        if (!Visible(data))
        {
            await w.Step("QuestBoardUI", operation: "weekly_npc_board_scroll", value: checked((int)id));
            double end = w.Time + 10;
            while (!Visible(await observe()))
            {
                if (w.Time >= end) throw new DailyNpcUnavailable("公告板目标任务未显示，未接取任务");
                await w.Delay(200);
            }
        }
        await w.Step("QuestBoardUI", operation: "weekly_npc_board_select", value: checked((int)id), expect: "QuestPopupUI");
        data = await observe();
        if (N(data.Native["Board"]?["PopupQuest"]) != id || !B(data.Native["Board"]?["PopupCanAccept"]))
            throw new DailyNpcUnavailable("公告板确认页与选定任务不符，未接取任务");
    }
}
