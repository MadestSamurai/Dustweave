using Dustweave;
using System.Text.Json.Nodes;
using static HomeNavigationCases;

static class EmptySceneCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        JsonObject Empty(params JsonObject[] rows) { var f = Frame(rows); f["Scene"] = "Empty"; return f; }
        var policy = DailyNavigationPolicy.Load();
        var menu = Empty(Surface("MenuUI", field: "_objBackButton"), Surface("NoticeUI", id: 2));
        Check(!DailyNavigationDecision.SceneLoading(menu) && !DailyHomeDecision.Transitioning(menu), "native Empty menu is not a loading scene");
        Check(DailyHomeDecision.MenuReady(menu, policy) && DailyHomeDecision.Inspect(menu, policy).Kind == "ready", "native Empty menu and home decisions agree");
        foreach (string stage in new[] { "guild", "room", "mail", "free_draws", "weekly_npc" })
            Check(DailyNavigationDecision.Ready(menu, stage, policy), "Empty menu can prepare stage: " + stage);
        foreach (string ui in new[] { "MailUI", "GuildUI", "MyRoomUI", "PackListUI" })
            Check(DailyHomeDecision.Inspect(Empty(Surface(ui)), policy).Kind == "action", "known page remains navigable on Empty: " + ui);
        foreach (string ui in DailyTravel.Fields)
        {
            var field = Empty(Surface(ui, field: "_buttonMenu"));
            Check(!DailyNavigationDecision.SurfaceSceneReady(field, ui) && !DailyNavigationDecision.Ready(field, "weekly_npc", policy), "Empty does not prove field arrival: " + ui);
        }
        Check(DailyHomeDecision.Inspect(Empty(Surface("GameFieldDefaultUI", field: "_buttonMenu")), policy).Kind == "waiting", "home does not click residual field UI before map arrival");
        Check(DailyHomeDecision.Inspect(Empty(Surface("NoticeUI")), policy).Kind == "waiting", "Empty with only background UI remains waiting");
        var disabled = menu.DeepClone().AsObject(); disabled["Surfaces"]![0]!["InputReady"] = false;
        Check(DailyHomeDecision.Inspect(disabled, policy).Kind == "waiting" && !DailyNavigationDecision.Ready(disabled, "guild", policy), "Empty does not bypass disabled menu");
        var unknown = Empty(Surface("MenuUI"), Surface("UnknownPaymentPopupUI", true, "_buttonOK", 99));
        Check(DailyHomeDecision.Inspect(unknown, policy).Kind == "blocked" && !DailyNavigationDecision.Ready(unknown, "guild", policy), "Empty does not bypass unknown confirmation");
        var battle = Empty(Surface("BattleUI_PVP"));
        Check(DailyHomeDecision.Inspect(battle, policy).Kind == "blocked" && !DailyNavigationDecision.Ready(battle, "mirror", policy), "Empty does not treat residual battle UI as a loaded battle");
        var missing = menu.DeepClone().AsObject(); missing["Scene"] = "";
        Check(DailyHomeDecision.Transitioning(missing) && !DailyNavigationDecision.Ready(missing, "room", policy), "missing scene remains unavailable");

        foreach (string stage in new[] { "guild", "room" })
        {
            double time = 0; int commands = 0;
            var context = CommandDriverCases.Context();
            var route = new DailyStageNavigation(() => Task.FromResult(new DailyStageFrame(menu.DeepClone().AsObject(), context.DeepClone().AsObject())), () => false,
                clock: () => time, delay: t => { time += t.TotalSeconds; return Task.CompletedTask; });
            Task<JsonObject> Relay(string op, string? s, JsonObject? a) { commands++; throw new Exception("Ready Empty menu must not send a recovery command"); }
            var result = await route.EnterAsync(stage, context, Relay);
            Check(result["state"]?.GetValue<string>() == "ready" && time >= policy.SettleSeconds && time < 3 && commands == 0, "reported Empty menu reaches stage without 120-second timeout: " + stage);
            var recovery = await route.RecoverAsync(stage, context, "test", Relay);
            Check(recovery["safe"]?.GetValue<bool>() == true && commands == 0, "Empty menu recovery is already safe: " + stage);
        }
        {
            var frame = battle.DeepClone().AsObject();
            double time = 0;
            var context = CommandDriverCases.Context();
            var route = new DailyStageNavigation(() => Task.FromResult(new DailyStageFrame(frame.DeepClone().AsObject(), context.DeepClone().AsObject())), () => false,
                clock: () => time, delay: t => { time += t.TotalSeconds; if (time >= 1) frame["Scene"] = "Map3008_001"; return Task.CompletedTask; });
            await route.EnterAsync("mirror", context, (_, _, _) => throw new Exception("Residual battle test submitted input"));
            Check(time >= 1 && time < 2, "stage navigation waits for map before resuming owned battle");
        }
        foreach (string cover in new[] { "LoadingUI", "EntranceBackgroundCoverUI" })
        {
            var frame = menu.DeepClone().AsObject();
            frame["Surfaces"]!.AsArray().Add(Surface(cover, id: 3));
            Check(DailyHomeDecision.Transitioning(frame) && !DailyNavigationDecision.Ready(frame, "room", policy), "actual transition overrides ready Empty menu: " + cover);
            double time = 0;
            var context = CommandDriverCases.Context();
            var route = new DailyStageNavigation(() => Task.FromResult(new DailyStageFrame(frame.DeepClone().AsObject(), context.DeepClone().AsObject())), () => false,
                clock: () => time, delay: t => { time += t.TotalSeconds; if (time >= 1) frame = menu.DeepClone().AsObject(); return Task.CompletedTask; });
            await route.EnterAsync("room", context, (_, _, _) => throw new Exception("Loading test submitted input"));
            Check(time >= 1 + policy.SettleSeconds && time < 4, "Empty menu settles after actual cover disappears: " + cover);
        }
        using (var f = new WorkflowHarness(Path.Combine(output, "empty-menu-ready"), []))
        {
            f.Frame["Scene"] = "Empty"; f.Page("MenuUI");
            await DailyTravel.Ready(f.Workflow, "MenuUI", 2);
            Check(f.Time >= .4 && f.Time < 1 && f.Box.Commands.Count == 0, "travel can await a menu that remains on Empty");
        }
        using (var f = new WorkflowHarness(Path.Combine(output, "empty-menu-pack-list"), []))
        {
            f.Frame["Scene"] = "Empty"; f.Page("MenuUI", "_buttonPack");
            f.OnCommand = _ => {
                if (f.Box.Commands.Count == 1) { f.Frame["Scene"] = "Map3007_001"; f.Page("CafeteriaFieldDefaultUI", "_buttonPackList"); }
                else f.Page("PackListUI");
            };
            await DailyTravel.PackList(f.Workflow, 3);
            Check(f.Box.Commands.Count == 2 && f.Box.Commands[0]["TargetId"]?.GetValue<int>() == 11, "Empty menu returns to cartridge before using pack list; never opens guide");
        }
        using (var f = new WorkflowHarness(Path.Combine(output, "empty-menu-reuse"), []))
        {
            f.Frame["Scene"] = "Empty"; f.Page("MenuUI");
            f.Readings = () => [WorkflowCases.Reading("navigation.pack", (DailyTravel.CurrentPack + ".Id", 3007), (DailyTravel.CurrentPack + ".PackType", 9))];
            f.OnCommand = _ => f.Page("CafeteriaFieldDefaultUI");
            f.OnDelay = () => { if (f.Time >= 1) f.Frame["Scene"] = "Map3007_001"; };
            bool ready = await DailyTravel.Reuse(f.Workflow, packType: 9, surfaces: ["CafeteriaFieldDefaultUI"], seconds: 3);
            Check(ready && f.Time >= 1.3 && f.Box.Commands.Count == 1, "return from Empty menu once, then require actual map before reuse succeeds");
        }
        using (var f = new WorkflowHarness(Path.Combine(output, "empty-stale-pack-entry"), []))
        {
            f.Frame["Scene"] = "Empty"; f.Page("CafeteriaFieldDefaultUI", "_buttonPackList");
            f.OnDelay = () => { if (f.Time >= 1) f.Frame["Scene"] = "Map3007_001"; };
            f.OnCommand = _ => { if (f.Time < 1) throw new Exception("Used unloaded map"); f.Page("PackListUI"); };
            await DailyTravel.PackList(f.Workflow, 3);
            Check(f.Time >= 1.4 && f.Box.Commands.Count == 1, "stale field cartridge button waits for actual map");
        }
    }
}