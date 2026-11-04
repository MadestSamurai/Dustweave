using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;

static class MenuOpenRetryCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        JsonObject Open(double seconds = 8) => O(("ui", "GameFieldDefaultUI"), ("field", "_buttonPackList"), ("expect", "PackListUI"), ("timeout", seconds));
        WorkflowHarness New(string name)
        {
            var h = new WorkflowHarness(Path.Combine(output, "menu-retry-" + name), []);
            h.Frame["Scene"] = "Map0007_003";
            h.Page("GameFieldDefaultUI", "_buttonPackList");
            return h;
        }
        foreach (string mode in new[] { "first", "second", "third", "exhausted", "short-deadline", "late", "late-before-submit", "late-final-read", "loading", "opening", "overlay", "disabled", "battle", "scene", "account", "process", "stop", "pause", "unknown", "lost-retry", "rejected-retry", "new-target", "passive" })
        {
            using var h = New(mode);
            int expectedCommands = mode switch { "third" or "exhausted" or "rejected-retry" => 3, "second" or "loading" or "opening" or "lost-retry" or "new-target" or "passive" => 2, _ => 1 };
            h.OnCommand = _ =>
            {
                int count = h.Box.Commands.Count;
                if (mode == "first" || count == expectedCommands && mode is "second" or "third" or "loading" or "opening" or "new-target" or "passive") h.Page("PackListUI");
                if (mode == "lost-retry") h.Box.ThrowCreate = true;
                if (mode == "rejected-retry") { h.Box.State = "rejected"; h.Box.Dispatched = false; h.Box.Error = "ui_not_ready"; }
            };
            if (mode == "unknown") h.Box.State = "unknown";
            if (mode == "passive")
                foreach (string type in new[] { "OverheadManageUI", "NoticeUI", "CurrencyManageUI" })
                    h.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface(type, id: 40 + h.Frame["Surfaces"]!.AsArray().Count));
            h.OnDelay = () =>
            {
                if (h.Box.Commands.Count != 1) return;
                if (mode == "late" && h.Time >= 1) h.Page("PackListUI");
                if (mode is "loading" or "opening")
                {
                    h.Page("GameFieldDefaultUI", "_buttonPackList");
                    if (h.Time < 3)
                    {
                        if (mode == "opening") h.Frame["Surfaces"]![0]!["InputReady"] = false;
                        else h.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("LoadingUI", id: 20));
                    }
                }
                if (mode == "overlay" && h.Frame["Surfaces"]!.AsArray().Count == 1) h.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("UnknownPopupUI", true, "_close", 20));
                if (mode == "disabled") h.Frame["Surfaces"]![0]!["Targets"]![0]!["Enabled"] = false;
                if (mode == "battle") h.Page("BattleUI_PVP");
                if (mode == "scene") h.Frame["Scene"] = "Map0008_001";
                if (mode == "account") h.Context["actor"]![3] = new string('c', 64);
                if (mode == "process") h.Context["actor"]![0] = 99;
                if (mode == "stop") h.Stopped = true;
                if (mode == "pause") h.Box.Values["live:pause"] = [];
                if (mode == "new-target") { h.Frame["Surfaces"]![0]!["Id"] = 90; h.Frame["Surfaces"]![0]!["Targets"]![0]!["Id"] = 100; }
            };
            h.Driver.SubmissionGuard = () => { if (mode == "late-before-submit" && h.Box.Commands.Count == 1) h.Page("PackListUI"); };
            int lateReads = 0;
            h.OnRead = () => { if (mode == "late-final-read" && h.Time >= 2 && h.Box.Commands.Count == 1 && ++lateReads == 3) h.Page("PackListUI"); };
            JsonObject? result = null;
            DailyStepException? failure = null;
            try { result = await h.Driver.SendObservedAsync(Open(mode == "short-deadline" ? 1 : 8)); }
            catch (DailyStepException e) { failure = e; }
            bool success = mode is "first" or "second" or "third" or "late" or "late-before-submit" or "late-final-read" or "loading" or "opening" or "new-target" or "passive";
            Check(h.Box.Commands.Count == expectedCommands && (success ? result != null && failure == null : failure != null), "menu retry count and outcome: " + mode);
            Check(h.Time <= 8.3, "menu retry keeps one bounded observation budget: " + mode);
            if (mode is "loading" or "opening") Check(h.Time >= 3, "menu retries wait for actual readiness: " + mode);
            if (mode is "account" or "process" or "stop" or "pause") Check(h.Time < 1, "menu retry promptly respects identity and cancellation: " + mode);
            if (mode == "new-target") Check(N(h.Box.Commands[1]["SurfaceId"]) == 90 && N(h.Box.Commands[1]["TargetId"]) == 100, "menu retry resolves fresh native targets");
            if (mode == "lost-retry") Check(JsonNode.DeepEquals(failure!.Command!["Id"], h.Box.Commands[1]["Id"]), "uncertain retry keeps its own command identity and is not retried");
            if (mode is "second" or "third" or "exhausted")
            {
                var first = JsonNode.Parse(File.ReadAllText(Path.Combine(h.Root, "live", "steps", S(h.Box.Commands[0]["Id"]), "result.json")));
                Check(S(first?["state"]) == "menu_open_retried" && JsonNode.DeepEquals(first?["next_command_id"], h.Box.Commands[1]["Id"]), "menu retry retains linked receipts: " + mode);
                if (result != null) Check(result["menu_retries"]!.AsArray().Count == expectedCommands - 1, "menu success includes retry evidence: " + mode);
                else Check(failure!.NativeReceipt != null, "menu timeout carries native receipt into failure diagnostics");
            }
            if (mode == "exhausted")
            {
                h.OnCommand = _ => h.Page("PackListUI");
                await h.Driver.SendObservedAsync(Open());
                Check(h.Box.Commands.Count == 4, "explicit continuation can recover after exhausted menu retries");
            }
        }

        using (var h = new WorkflowHarness(Path.Combine(output, "menu-retry-short-deadline"), []))
        {
            h.Frame["Scene"] = "Map0007_003"; h.Page("PackListUI");
            await DailyTravel.PackList(h.Workflow);
            Check(h.Box.Commands.Count == 0, "restart adopts late menu arrival after a prior timeout without deleting journals");
        }

        foreach (var entry in new[] {
            ("GameFieldDefaultUI", "_buttonMenu", "MenuUI"),
            ("MenuUI", "_buttonInventory", "InventoryManageUI"),
            ("MenuUI", "_buttonMission", "MissionUI"),
            ("MenuUI", "_buttonEvent", "EventUI"),
            ("MenuUI", "_buttonPass", "PassUI"),
            ("MenuUI", "_buttonMiniGame", "MiniGameHubUI"),
            ("MenuUI", "_goQuickHuntDispatchButton", "HuntOrAirwayUI"),
            ("HuntOrAirwayUI", DailyHunting.SelectField, "HuntDispatchPopupUI"),
            ("EventBattleUI", "_buttonSkipBattle._button", "BattleSkipPopupUI"),
            ("EventBattleUI", "_buttonAutoBattle._button", "BattleSkipPopupUI") })
        {
            using var h = New(entry.Item2);
            h.Page(entry.Item1, entry.Item2);
            if (entry.Item1 == "HuntOrAirwayUI")
            {
                h.Frame["Surfaces"]![0]!["Targets"]![0]!["Route"] = "pointer";
                h.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("WorldMapUI", id: 40));
            }
            if (entry.Item1 == "MenuUI") h.Frame["Scene"] = "Empty"; // Native home menu is not a loading scene.
            if (entry.Item1 == "MenuUI") h.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("GameFieldDefaultUI", id: 40));
            h.OnCommand = _ => { if (h.Box.Commands.Count == 2) h.Page(entry.Item3); };
            await h.Workflow.Step(O(("ui", entry.Item1), ("field", entry.Item2), ("expect", entry.Item3), ("timeout", 8)));
            Check(h.Box.Commands.Count == 2, "shared non-consuming menu recovery: " + entry.Item2);
        }
        foreach (string mode in new[] { "parent-opening", "parent-final-read", "late-popup", "other-popup", "loading", "changed-account", "cancel", "unrelated-parent" })
        {
            using var h = New("hunt-" + mode);
            var action = O(("ui", "HuntOrAirwayUI"), ("field", DailyHunting.SelectField), ("expect", "HuntDispatchPopupUI"), ("timeout", 8));
            h.Page("HuntOrAirwayUI", DailyHunting.SelectField);
            h.Frame["Surfaces"]![0]!["Targets"]![0]!["Route"] = "pointer";
            var parent = HomeNavigationCases.Surface(mode == "unrelated-parent" ? "OtherUI" : "WorldMapUI", id: 40);
            parent["InputReady"] = mode is not ("parent-opening" or "unrelated-parent");
            h.Frame["Surfaces"]!.AsArray().Add(parent);
            h.OnCommand = _ => {
                if (mode == "parent-final-read" && h.Box.Commands.Count == 1) parent["InputReady"] = false;
                if (mode == "unrelated-parent" || mode is "parent-opening" or "parent-final-read" && B(parent["InputReady"])) h.Page("HuntDispatchPopupUI");
                if (mode == "other-popup") h.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("UnknownPopupUI", true, "_close", 50));
                if (mode == "loading") h.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("LoadingUI", id: 50));
            };
            h.OnDelay = () => {
                if (h.Time >= 1) parent["InputReady"] = true;
                if (h.Box.Commands.Count == 0) return;
                if (mode == "late-popup" && h.Time >= 1) h.Page("HuntDispatchPopupUI");
                if (mode == "changed-account") h.Context["actor"]![3] = "changed-account";
                if (mode == "cancel") h.Stopped = true;
            };
            JsonObject? result = null;
            try { result = await h.Driver.SendObservedAsync(action); } catch (DailyStepException) { }
            bool success = mode is "parent-opening" or "parent-final-read" or "late-popup" or "unrelated-parent";
            Check(h.Box.Commands.Count == (mode is "parent-opening" or "parent-final-read" ? 2 : 1) && (result != null) == success,
                "hunt checks owner only after an opener fails, and adopts late arrival: " + mode);
            if (mode is "parent-opening" or "parent-final-read") Check(h.Time >= 2, "hunt waits before checking and retrying its non-consuming preview: " + mode);
            if (mode == "unrelated-parent") Check(h.Time < 1, "hunt owner readiness does not extend to unrelated UI");
        }
        foreach (string mode in new[] { "purchase", "claim", "battle", "back", "unknown-field", "owned", "preview", "native", "pointer" })
        {
            using var h = New("excluded-" + mode);
            var action = Open();
            if (mode is "purchase" or "claim" or "battle" or "unknown-field")
            {
                action["ui"] = mode switch { "purchase" => "ShopPopupUI", "claim" => "MailUI", "battle" => "EventBattleUI", _ => "GameFieldDefaultUI" };
                action["field"] = mode switch { "purchase" => "_buttonBuy", "claim" => "_objAllReceiveButton", "battle" => "_buttonStart", _ => "_other" };
                h.Page(S(action["ui"]), S(action["field"]));
            }
            if (mode == "back") { action.Remove("field"); action["back"] = true; }
            if (mode == "native") action["native"] = true;
            if (mode == "pointer") h.Frame["Surfaces"]![0]!["Targets"]![0]!["Route"] = "pointer";
            bool pending = false;
            try
            {
                if (mode == "preview") await h.Driver.SendPreviewObservedAsync(action);
                else await h.Driver.SendObservedAsync(action, mode == "owned" ? h.Frame.DeepClone().AsObject() : null);
            }
            catch (DailyStepException e) { pending = e.Submitted; }
            Check(pending && h.Box.Commands.Count == 1, "menu retry excludes consuming, owned and unrelated inputs: " + mode);
        }
        foreach (string extra in new[] { "back", "operation", "items", "value", "absent", "allow_guild", "target_id", "mirror_entry", "pass_init", "startup" })
        {
            var action = Open(); action[extra] = null;
            Check(!DailyMenuNavigation.CanRepeatOpen(action), "extra action contract cannot opt into menu retry: " + extra);
        }

        // Each reported map continues past the recovered opener into the next
        // workflow step. Repeating a workflow / restarting adopts the open page.
        foreach (string map in new[] { "Map0007_003", "Map0011_003", "Map0012_003", "Map0013_002" })
        {
            string path = Path.Combine(output, "menu-retry-route-" + map);
            using (var h = new WorkflowHarness(path, []))
            {
                h.Frame["Scene"] = map; h.Page("GameFieldDefaultUI", "_buttonPackList");
                h.OnCommand = _ => { if (h.Box.Commands.Count == 2) h.Page("PackListUI", "_next"); else if (h.Box.Commands.Count == 3) h.Page("PackInfoUI"); };
                await DailyTravel.PackList(h.Workflow);
                await DailyTravel.PackList(h.Workflow);
                Check(h.Box.Commands.Count == 2, "recovered pack route reuses visible list: " + map);
                await h.Workflow.Step("PackListUI", "_next", expect: "PackInfoUI");
                Check(h.Box.Commands.Count == 3, "recovered pack route actually continues: " + map);
            }
            using (var h = new WorkflowHarness(path, []))
            {
                h.Frame["Scene"] = map; h.Page("PackListUI");
                await DailyTravel.PackList(h.Workflow);
                Check(h.Box.Commands.Count == 0, "restart adopts authoritative menu despite old retry records: " + map);
            }
        }
    }
}
