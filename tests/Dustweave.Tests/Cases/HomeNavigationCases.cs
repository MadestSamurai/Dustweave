using BD2Daily;
using System.Text.Json;
using System.Text.Json.Nodes;
static class HomeNavigationCases
{
    internal static JsonObject Surface(string type, bool popup = false, string? field = null, int id = 1) => new() { ["Type"] = type, ["Id"] = id, ["Order"] = id, ["Popup"] = popup, ["InputReady"] = true, ["Path"] = "Root/" + type, ["Text"] = new JsonArray(), ["Targets"] = field == null ? new JsonArray() : new JsonArray(new JsonObject { ["Id"] = id + 100, ["Field"] = field, ["Enabled"] = true, ["Route"] = "ui" }) };
    internal static JsonObject Frame(params JsonObject[] rows)
    {
        var f = CommandDriverCases.Frame();
        f["BridgeVersion"] = 104;
        f["Surfaces"] = new JsonArray(rows.Select(r => (JsonNode)r.DeepClone()).ToArray());
        return f;
    }
    internal static JsonObject Menu() => Frame(Surface("MenuUI"));
    internal static JsonObject Field() => Frame(Surface("GameFieldDefaultUI", false, "_buttonMenu"));
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string label)
        {
            if (!ok)
                throw new Exception(label);
            cases.Add(label);
        }
        var policy = DailyNavigationPolicy.Load();
        var pages = new Dictionary<string, string?>(DailyNavigationDecision.Closeable) { ["MyRoomUI"] = null, ["GuildUI"] = null, ["EventUI"] = null, ["ShopUI"] = null, ["EquipmentMakingUI"] = null, ["EquipmentUpgradePopupUI"] = null, ["SichuanStagePopupUI"] = null, ["HuntDispatchPopupUI"] = null, ["TarosTacticsBingoUI"] = "_objBackButton", ["EventBattleUI"] = null, ["EventMainUI"] = null, ["TarosTacticsSeasonFinishUI"] = "_btnBack", ["PVPClassUpUI"] = "_objCancelButton" };
        foreach (var page in pages)
        {
            var row = Surface(page.Key, false, page.Value);
            var f = Frame(row);
            var p = DailyHomeDecision.Inspect(f, policy);
            Check(p.Kind == "action" && p.Action?["ui"]?.GetValue<string>() == page.Key, "startup recognizes return from " + page.Key);
            f["Surfaces"]![0]!["InputReady"] = false;
            Check(DailyHomeDecision.Inspect(f, policy).Kind == "waiting", "startup waits native readiness on " + page.Key);
            var unknown = Surface("PurchaseConfirmationUI", true, null, 99);
            f = Frame(row, unknown);
            Check(DailyHomeDecision.Inspect(f, policy).Kind == "blocked", "startup never backs through a confirmation above " + page.Key);
            f = Frame(row, row);
            Check(DailyHomeDecision.Inspect(f, policy).Kind == "blocked", "startup refuses duplicate " + page.Key);
        }
        var update = Surface("UpdateUI", true, "_objBackButton");
        var entrance = Surface("EntranceBackgroundCoverUI", true, null, 2);
        Check(DailyHomeDecision.Inspect(Frame(update, entrance), policy).Action?["ui"]?.GetValue<string>() == "UpdateUI", "update slideshow is actionable through its native entrance cover");
        Check(DailyHomeDecision.Inspect(Frame(update, entrance, Surface("LoadingUI", false, null, 3)), policy).Kind == "waiting", "actual loading still blocks update slideshow input");
        var disabled = Field();
        disabled["Surfaces"]![0]!["Targets"]![0]!["Enabled"] = false;
        Check(DailyHomeDecision.Inspect(disabled, policy).Kind == "waiting", "disabled startup field menu waits instead of failing immediately");
        Check(DailyHomeDecision.Inspect(Frame(Surface("GameFieldDefaultUI", false, "_buttonMenu"), Surface("CafeteriaFieldDefaultUI", false, "_buttonMenu.button", 2)), policy).Kind == "blocked", "two field menu owners remain ambiguous");
        var menu = Menu();
        menu["Surfaces"]![0]!.AsObject().Remove("InputReady");
        Check(DailyHomeDecision.Inspect(menu, policy).Kind == "waiting", "startup requires explicit menu input readiness");
        var harbor = Surface("AvatarFishingHarborUI", false, "_objHomeMenuButton");
        var harborPlan = DailyHomeDecision.Inspect(Frame(harbor, Surface("NoticeUI")), policy);
        Check(harborPlan.Action?["field"]?.GetValue<string>() == "_objHomeMenuButton" && harborPlan.Action?["expect"]?.GetValue<string>() == "MenuUI", "observed fishing harbor home button returns to daily menu");
        harbor["Targets"]![0]!["Enabled"] = false;
        Check(DailyHomeDecision.Inspect(Frame(harbor), policy).Kind == "waiting", "disabled fishing harbor home button is observed without sending input");
        harbor["Targets"]![0]!["Enabled"] = true;
        harbor["InputReady"] = false;
        Check(DailyHomeDecision.Inspect(Frame(harbor), policy).Kind == "waiting", "harbor entry animation waits native input readiness");
        harbor["InputReady"] = true;
        Check(DailyHomeDecision.Inspect(Frame(harbor, Surface("MessagePopupUI", true, "_buttonOK", 99)), policy).Kind == "blocked", "harbor home does not bypass unknown confirmation");
        Check(DailyHomeDecision.Inspect(Frame(harbor, Surface("FishingGameFieldDefaultUI")), policy).Kind == "blocked", "active fishing retains ownership even with a harbor page present");
        Check(DailyHomeDecision.Inspect(Frame(harbor, harbor.DeepClone().AsObject()), policy).Kind == "blocked", "duplicate fishing harbors cannot select a home button");
        Check(DailyHomeDecision.Inspect(Frame(harbor, Surface("GameFieldDefaultUI", false, "_buttonMenu")), policy).Kind == "blocked", "two native menu owners remain ambiguous including the harbor");
        foreach (string ui in new[] { "ItemGetPopupUI", "EquipmentUpgradeResultPopupUI", "EquipmentBatchUpgradeResultPopupUI", "SichuanStageClearPopupUI" })
        {
            string? field = ui switch
            {
                "ItemGetPopupUI" => "_objBackButton",
                "EquipmentUpgradeResultPopupUI" => "_objOKButton",
                "SichuanStageClearPopupUI" => "_buttonExit",
                _ => null
            };
            Check(DailyHomeDecision.Inspect(Frame(Surface(ui, true, field)), policy).Kind == "action", "startup closes existing presentation " + ui);
        }
        foreach (string ui in new[] { "BattleUI_FieldBattle", "BattleResultUI", "SichuanBoardUI", "SichuanStageFailedPopupUI", "FishingGameFieldDefaultUI", "MiniGameDiceUI", "UnknownPopupUI" })
            Check(DailyHomeDecision.Inspect(Frame(Surface(ui, true)), policy).Kind == "blocked", "startup preserves active or unknown state " + ui);
        Check(DailyHomeDecision.Inspect(Frame(Surface("MiniEventMainUI"), Surface("MiniGameDiceUI", false, null, 2)), policy).Kind == "blocked", "active dice cannot be closed through its enclosing mini-event hub");
        Check(DailyHomeDecision.Inspect(Frame(Surface("ScriptUI", false, "_objButtonTouch")), policy).Kind == "blocked", "unknown script context receives no generic story advance");
        var gacha = Surface("GachaResultUI", false, "_objSkipButton");
        gacha["InputReady"] = false;
        Check(DailyHomeDecision.Inspect(Frame(gacha), policy).Action?["field"]?.GetValue<string>() == "_objSkipButton", "gacha animation permits only its guarded native skip while busy");
        var balloon = Surface("BalloonScriptUI");
        var skip = Surface("StorySkipUI", false, "_objSkipButton", 2);
        skip["NativeContext"] = "story_skip";
        Check(DailyHomeDecision.Inspect(Frame(balloon, skip), policy).Reason == "quiz", "balloon never skips before checking whether it owns an active quiz");
        var reset = Surface("MessagePopupUI", true, "_buttonOK");
        reset["Path"] = "Root/ErrorMessagePopupUI";
        reset["Text"] = new JsonArray("error : 112003");
        Check(DailyHomeDecision.Inspect(Frame(reset), policy).Reason == "mission_reset", "mission reset requires a separate proof-based managed recovery");
        reset["InputReady"] = false;
        Check(DailyHomeDecision.Inspect(Frame(reset), policy).Kind == "waiting", "known reset waits through native popup animation without treating it as unknown");
        reset["InputReady"] = true;
        reset["Targets"]![0]!["Enabled"] = false;
        Check(DailyHomeDecision.Inspect(Frame(reset), policy).Kind == "waiting", "known reset waits its disabled OK without confirming");
        reset["Targets"]![0]!["Enabled"] = true;
        reset["Targets"]!.AsArray().Add(new JsonObject { ["Id"] = 9, ["Field"] = "_buttonCancel", ["Enabled"] = true });
        Check(DailyHomeDecision.Inspect(Frame(reset), policy).Kind == "blocked", "reset text with an enabled cancel never authorizes OK");
        int counter = 0;
        async Task Route(string name, JsonObject first, Action<Flow>? configure = null, bool expected = true, string? expectedKind = null)
        {
            var f = new Flow(Path.Combine(output, "home-route-" + (++counter)), first);
            configure?.Invoke(f);
            string kind = "";
            JsonObject? result = null;
            try
            {
                result = await f.Navigation.EnterAsync("guild", f.Context.DeepClone().AsObject(), f.Relay);
            }
            catch (StageHostException e) { kind = e.Kind; }
            Check(expected ? result?["state"]?.GetValue<string>() == "ready" : kind == expectedKind, "startup route " + name);
            f.Inspect?.Invoke();
        }
        await Route("already open menu settles without inputs", Menu(), f => f.Inspect = () => Check(f.Time >= 2 && f.Actions.Count == 0, "existing startup menu retains two-second stability"));
        await Route("live menu counters and rotating links do not restart native settle", Menu(), f =>
        {
            f.OnWait = () =>
            {
                f.Current["UiToken"] = "live-counter-" + f.Time;
                f.Current["Surfaces"]![0]!["Text"] = new JsonArray("fish timer " + f.Time);
                f.Current["Surfaces"]![0]!["Targets"] = new JsonArray(new JsonObject { ["Id"] = (int)(f.Time * 100), ["Field"] = "$pointer/Management/ScrollRect/Content", ["Enabled"] = true });
            };
            f.Inspect = () => Check(f.Time >= 2 && f.Time < 3 && f.Actions.Count == 0, "unchanged ready menu settles despite countdown and carousel changes");
        });
        await Route("new menu instance restarts native settle", Menu(), f => { f.OnWait = () => { if (f.Time >= 1) f.Current["Surfaces"]![0]!["Id"] = 900; }; f.Inspect = () => Check(f.Time >= 3, "replacement menu requires its own stable readiness interval"); });
        await Route("fishing harbor home observed in the running client", Frame(harbor, Surface("NoticeUI")), f => { f.OnAction = _ => f.Current = Menu(); f.Inspect = () => Check(f.Actions.Count == 1 && f.Actions[0]["field"]?.GetValue<string>() == "_objHomeMenuButton", "startup exits fishing harbor once without starting fishing or claiming traps"); });
        await Route("passive and loading states then disabled field button", Frame(Surface("NoticeUI")), f => { f.OnWait = () => { if (f.Time < .4) f.Current = Frame(Surface("LoadingUI")); else if (f.Time < 1) { f.Current = Field(); f.Current["Surfaces"]![0]!["Targets"]![0]!["Enabled"] = false; } else if (f.Actions.Count == 0) f.Current = Field(); }; f.OnAction = _ => f.Current = Menu(); f.Inspect = () => Check(f.Actions.Count == 1 && f.Time >= 3.4, "field button opens once after transient readiness"); });
        await Route("update slideshow pages retain the same surface identity", Frame(update, entrance), f => { int page = 0; f.OnAction = a => { if (++page == 3) f.Current = Field(); else { f.Current = Frame(update, entrance); f.Current["Surfaces"]![0]!["Text"] = new JsonArray("page" + page); } }; f.AfterMenu = true; f.Inspect = () => Check(f.Actions.Count == 4, "three update pages and one field-menu input remain distinct progress"); });
        var script = Surface("ScriptUI", false, "_objButtonTouch");
        script["NativeContext"] = "story_dialogue";
        await Route("first guild stage advances field story", Frame(script), f => { f.OnAction = a => f.Current = a["operation"]?.GetValue<string>() == "story_advance" ? Field() : Menu(); f.Inspect = () => Check(f.Actions.First()["operation"]?.GetValue<string>() == "story_advance" && f.Actions.Count == 2, "non-field first stage uses managed story recovery"); });
        var quest = Surface("QuestClearPopupUI", true, "_objTodayBackButton");
        await Route("first stage closes weekly result", Frame(quest), f => { f.OnAction = a => f.Current = a["ui"]?.GetValue<string>() == "QuestClearPopupUI" ? Field() : Menu(); });
        var ad = Surface("NewsPopupEventUI", true, null, 2);
        ad["NoticeSuppression"] = "unchecked";
        await Route("multipage ad suppression and login notices", Frame(Surface("MenuUI"), ad), f => { int closed = 0; f.OnAction = a => { if (a["operation"]?.GetValue<string>() == "notice_suppress") f.Current["Surfaces"]![1]!["NoticeSuppression"] = "checked"; else if (++closed == 1) f.Current["Surfaces"]![1]!["Text"] = new JsonArray("second page"); else f.Current = Menu(); }; f.Inspect = () => Check(f.Actions.Count == 3, "one checkbox confirmation precedes two distinct notice pages"); });
        await Route("menu readiness regresses during settle", Menu(), f => { f.OnWait = () => { if (f.Time > .6 && f.Time < 1.2) f.Current = Frame(Surface("LoadingUI")); else if (f.Time >= 1.2) f.Current = Menu(); }; f.Inspect = () => Check(f.Time >= 3.2, "a startup loading interruption restarts menu settle"); });
        await Route("unknown confirmation receives no compatibility request", Frame(Surface("MessagePopupUI", true, "_buttonOK")), f => f.Inspect = () => Check(f.Actions.Count == 0 && f.Specials.Count == 0, "unknown startup confirm sends zero inputs"), false, "adapter");
        await Route("issued page close with no progress is never retried", Frame(Surface("MailUI")), f => { f.OnAction = _ => { }; f.Inspect = () => Check(f.Actions.Count == 1, "no-progress startup close is a single command"); }, false, "adapter");
        await Route("uncertain native receipt is never replayed", Frame(Surface("MailUI")), f => { f.Pending = true; f.Inspect = () => Check(f.Actions.Count == 1, "pending startup input remains one submission"); }, false, "pending");
        await Route("reset during startup stops before new input", Field(), f => f.OnWait = () => f.Context["cycle"] = "tomorrow", false, "identity");
        await Route("operator stop during readiness wait", disabled, f => f.OnWait = () => f.Stopped = true, false, "stopped");
        var stuck = new Flow(Path.Combine(output, "home-route-preserved"), Frame(Surface("MailUI")));
        stuck.OnAction = _ => { };
        try
        {
            await stuck.Navigation.EnterAsync("guild", stuck.Context, stuck.Relay);
        }
        catch (StageHostException) { }
        var recovered = await stuck.Navigation.RecoverAsync("guild", stuck.Context, "no progress", stuck.Relay);
        Check(recovered["safe"]?.GetValue<bool>() == false && stuck.Actions.Count == 1, "failure recovery retains the same original startup input without resending");
    }
    private sealed class Flow
    {
        public JsonObject Current, Context = CommandDriverCases.Context(); public DailyStageNavigation Navigation; public double Time; public bool Stopped, Pending, AfterMenu;
        public List<JsonObject> Actions = []; public List<string> Specials = []; public Action<JsonObject>? OnAction; public Action? OnWait, Inspect;
        public Flow(string root, JsonObject current)
        {
            Current = current.DeepClone().AsObject();
            Navigation = new(() => Task.FromResult(new DailyStageFrame(Current.DeepClone().AsObject(), Context.DeepClone().AsObject())), () => Stopped, clock: () => Time, delay: t => { Time += t.TotalSeconds; OnWait?.Invoke(); return Task.CompletedTask; });
        }
        public Task<JsonObject> Relay(string op, string? stage, JsonObject? args)
        {
            if (op == "home_recovery")
            {
                Specials.Add(args!["kind"]!.GetValue<string>());
                throw new Exception("Unexpected special recovery in route fixture");
            }
            if (op != "navigation_step")
                throw new Exception("Startup called compatibility: " + op);
            var action = args!["action"]!.DeepClone().AsObject();
            Actions.Add(action);
            if (Pending)
                throw new StageHostException("pending", "Unknown native result");
            if (AfterMenu && action["expect"]?.GetValue<string>() == "MenuUI")
                Current = Menu();
            else
                OnAction?.Invoke(action);
            return Task.FromResult(new JsonObject { ["id"] = "offline" });
        }
    }
}
