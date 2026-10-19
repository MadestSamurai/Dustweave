using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;

static class TravelTransitionCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        WorkflowHarness New(string name)
        {
            var f = new WorkflowHarness(Path.Combine(output, "travel-transition-" + name), []);
            f.Readings = () => [WorkflowCases.Reading("navigation.pack", (DailyTravel.CurrentPack + ".Id", 3007), (DailyTravel.CurrentPack + ".PackType", 9))];
            return f;
        }
        foreach (string mode in new[] { "arrival", "repeat", "unknown-modal", "battle", "other-map" })
        {
            using var f = New("square-book-" + mode);
            f.Frame["Scene"] = mode == "other-map" ? "Map3005_001" : "Map3009_001";
            f.Readings = () => [WorkflowCases.Reading("navigation.pack", (DailyTravel.CurrentPack + ".Id", 3009), (DailyTravel.CurrentPack + ".PackType", 11))];
            f.Page("TotalWarUI");
            if (mode == "unknown-modal") f.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("MessagePopupUI", true, "_buttonOK", 99));
            if (mode == "battle") f.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("BattleUI", id: 2));
            f.OnCommand = _ => f.Page("GameFieldDefaultUI");
            if (mode == "repeat") f.OnDelay = () => f.Page("TotalWarUI");
            if (mode is "arrival" or "repeat")
            {
                string result;
                try { result = await DailyTravel.Reuse(f.Workflow, packType: 11, surfaces: ["GameFieldDefaultUI"], seconds: 5) ? "arrived" : "wrong"; }
                catch (StageHostException e) { result = e.Kind; }
                Check(mode == "arrival" ? result == "arrived" && f.Box.Commands.Count == 1 : result == "adapter" && f.Box.Commands.Count == 2,
                    "square travel leaves only idle Book lobby with bounded recovery: " + mode);
                Check(f.Box.Commands.All(c => S(c["Kind"]) == "back"), "square recovery never starts or retries a Book battle: " + mode);
            }
            else Check(!await DailyTravel.LeaveSquareBookLobby(f.Workflow, f.Frame) && f.Box.Commands.Count == 0, "square lobby recovery preserves unknown UI and unrelated scenes: " + mode);
        }
        foreach (string entry in new[] { "MenuUI", "MiniGameHubUI", "SichuanMainUI", "SichuanBoardUI" })
        {
            using var f = New("sichuan-entry-" + entry);
            f.Frame["Scene"] = "Map3009_001";
            f.Page(entry, "_buttonMiniGame");
            f.Readings = () => [WorkflowCases.Reading("weekly.sichuan", ("$self", O(("Hubs", new JsonArray(O(("Uid", 42), ("Event", 17)))))))];
            f.OnCommand = _ => f.Page(S(f.Frame["Surfaces"]![0]!["Type"]) == "MenuUI" ? "MiniGameHubUI" : "SichuanMainUI");
            await DailyWeeklySichuan.EnterLobby(f.Workflow);
            Check(f.Box.Commands.Count == (entry == "MenuUI" ? 2 : entry == "MiniGameHubUI" ? 1 : 0), "Sichuan opens its lobby before preparing solver and reuses existing play: " + entry);
            int before = f.Box.Commands.Count;
            await DailyWeeklySichuan.EnterLobby(f.Workflow);
            Check(f.Box.Commands.Count == before, "Sichuan rechecks current UI after preparation without reopening: " + entry);
        }
        foreach (string entry in new[] { "pack-list", "reuse", "ready" })
        {
            using var f = New("delayed-book-record-" + entry);
            f.Frame["Scene"] = "Map3005_001";
            f.Page("MenuUI");
            void Record()
            {
                f.Page("TotalWarNewcordPopupUI", "_objBackButton");
                f.Frame["Surfaces"]![0]!["Popup"] = false;
                f.Frame["Surfaces"]![0]!["InputReady"] = false;
            }
            if (entry == "ready") Record();
            var closedPages = new List<string>();
            f.OnDelay = () => { if (f.Time >= .6) f.Frame["Surfaces"]![0]!["InputReady"] = true; };
            f.OnCommand = c =>
            {
                string page = S(f.Frame["Surfaces"]![0]!["Type"]);
                closedPages.Add(page);
                if (page == "MenuUI") Record();
                else if (page == "TotalWarNewcordPopupUI") f.Page("GameFieldDefaultUI", "_buttonPackList");
                else f.Page("PackListUI");
            };
            if (entry == "pack-list") await DailyTravel.PackList(f.Workflow, 5);
            else if (entry == "ready") await DailyTravel.Ready(f.Workflow, "GameFieldDefaultUI", 5);
            else Check(await DailyTravel.Reuse(f.Workflow, packType: 9, surfaces: ["GameFieldDefaultUI"], seconds: 5), "delayed Book record permits reuse of the current cartridge");
            Check(closedPages.Count(page => page == "TotalWarNewcordPopupUI") == 1 && f.Time >= .6,
                "delayed Book record closes once after animation and continues route: " + entry);
            Check(f.Box.Commands.All(c => S(c["Kind"]) is "click" or "back") && closedPages.All(page => page is "MenuUI" or "TotalWarNewcordPopupUI" or "GameFieldDefaultUI"), "record cleanup uses presentation buttons without restarting Book battle: " + entry);
        }
        foreach (string mode in new[] { "duplicate", "unknown-modal", "pending", "vanished" })
        {
            using var f = New("book-record-guard-" + mode);
            f.Page("TotalWarNewcordPopupUI", "_objBackButton");
            f.Frame["Surfaces"]![0]!["Popup"] = false;
            if (mode == "duplicate") f.Frame["Surfaces"]!.AsArray().Add(f.Frame["Surfaces"]![0]!.DeepClone());
            if (mode == "unknown-modal") f.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("MessagePopupUI", true, "_buttonOK", 99));
            if (mode == "pending") f.Box.ThrowCreate = true;
            if (mode == "vanished") f.Driver.SubmissionGuard = () => f.Page("GameFieldDefaultUI");
            string outcome = "";
            try { outcome = await DailyTravel.RecoverPresentation(f.Workflow, f.Frame.DeepClone().AsObject()) ? "handled" : "none"; }
            catch (StageHostException e) { outcome = e.Kind; }
            Check(mode switch {
                "pending" => outcome == "pending" && f.Box.Commands.Count == 1,
                "vanished" => outcome == "handled" && f.Box.Commands.Count == 0,
                _ => outcome == "adapter" && f.Box.Commands.Count == 0
            }, "Book record cleanup preserves command safety: " + mode + " -> " + outcome);
        }
        foreach (string scene in new[] { "Empty", "" })
        {
            using var f = New("persistent-menu-" + scene);
            f.Frame["Scene"] = scene;
            f.Page("MenuUI");
            f.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("LoadingUI", id: 2));
            f.OnDelay = () => { if (f.Time >= .8 && S(f.Frame["Scene"]) != "Map3007_001") { f.Frame["Scene"] = "Map3007_001"; f.Page("MenuUI"); } };
            f.OnCommand = _ =>
            {
                if (S(f.Frame["Scene"]) != "Map3007_001" || f.Time < 1.1) throw new Exception("Used persistent menu before arrival");
                f.Page("CafeteriaFieldDefaultUI");
            };
            bool arrived = await DailyTravel.Reuse(f.Workflow, packType: 9, surfaces: ["CafeteriaFieldDefaultUI"], waitForTarget: true, seconds: 5);
            Check(arrived && f.Box.Commands.Count == 1, "persistent menu is untouched until target scene stabilizes: " + scene);
        }
        using (var f = New("ready-empty"))
        {
            f.Frame["Scene"] = "Empty"; f.Page("CafeteriaFieldDefaultUI");
            f.OnDelay = () => { if (f.Time >= .9) f.Frame["Scene"] = "Map3007_001"; };
            await DailyTravel.Ready(f.Workflow, "CafeteriaFieldDefaultUI", 3);
            Check(f.Time >= 1.3 && f.Box.Commands.Count == 0, "visible field UI in Empty scene cannot complete arrival");
        }
        foreach (string entry in new[] { "travel", "field-ready" })
        foreach (string mode in new[] { "arrived", "vanished", "identity", "unknown", "unstable" })
        {
            using var f = New("story-" + entry + "-" + mode);
            void Story()
            {
                f.Page("StorySkipUI", "_objSkipButton");
                f.Frame["Surfaces"]![0]!["NativeContext"] = "story_skip";
            }
            Story(); f.Frame["Scene"] = "Empty";
            var before = f.Frame.DeepClone().AsObject();
            bool changed = false;
            f.Driver.SubmissionGuard = () =>
            {
                if (changed && mode != "unstable") return;
                changed = true;
                f.Frame["Scene"] = S(f.Frame["Scene"]) == "Empty" ? "Map2002_005" : "Empty";
                if (mode == "vanished") f.Page("GameFieldDefaultUI");
                if (mode == "identity") f.Frame["PlayerKey"] = new string('c', 64);
            };
            if (mode == "unknown") { f.Driver.SubmissionGuard = null; f.Box.ThrowCreate = true; }
            f.OnCommand = _ => f.Page("GameFieldDefaultUI");
            string outcome = "";
            try {
                if (entry == "field-ready") { await new DailyFieldRoute(f.Workflow).FieldReady(timeout: 3, settle: 0); outcome = "handled"; }
                else outcome = await DailyTravel.RecoverPresentation(f.Workflow, before) ? "handled" : "none";
            }
            catch (StageHostException e) { outcome = e.Kind; }
            Check(mode switch {
                "arrived" => changed && outcome == "handled" && f.Box.Commands.Count == 1,
                "vanished" => changed && outcome == "handled" && f.Box.Commands.Count == 0,
                "identity" => outcome == "identity" && f.Box.Commands.Count == 0,
                "unknown" => outcome == "pending" && f.Box.Commands.Count == 1,
                _ => outcome == "adapter" && f.Box.Commands.Count == 0 && f.Time < 2
            }, "story transition recovery preserves dispatch boundary: " + entry + " / " + mode + " -> " + outcome);
        }
        using (var f = New("preflight"))
        {
            f.Frame["Scene"] = "Map3008_001"; f.Page("MenuUI");
            bool armed = false, changed = false; int reads = 0;
            f.Readings = () =>
            {
                if (f.Time >= .3 && !changed) armed = true;
                return [WorkflowCases.Reading("navigation.pack", (DailyTravel.CurrentPack + ".Id", 3007), (DailyTravel.CurrentPack + ".PackType", 9))];
            };
            f.OnRead = () =>
            {
                if (armed && !changed && ++reads == 3) { changed = true; f.Frame["Scene"] = "Empty"; }
            };
            f.OnDelay = () => { if (changed) { f.Frame["Scene"] = "Map3007_001"; f.Page("CafeteriaFieldDefaultUI"); } };
            bool arrived = await DailyTravel.Reuse(f.Workflow, packType: 9, surfaces: ["CafeteriaFieldDefaultUI"], waitForTarget: true, seconds: 5);
            Check(changed && arrived && f.Box.Commands.Count == 0, "unsent scene-change rejection re-observes arrival instead of interrupting the queue");
        }
        using (var f = New("overlay-disappeared"))
        {
            f.Frame["Scene"] = "Map3007_001"; f.Page("MenuUI");
            f.Driver.SubmissionGuard = () => f.Page("CafeteriaFieldDefaultUI");
            bool arrived = await DailyTravel.Reuse(f.Workflow, packType: 9, surfaces: ["CafeteriaFieldDefaultUI"], seconds: 4);
            Check(arrived && f.Box.Commands.Count == 0, "overlay disappearance before dispatch re-observes the arrived field");
        }
        using (var f = New("submitted-unknown"))
        {
            f.Frame["Scene"] = "Map3007_001"; f.Page("MenuUI");
            f.Box.ThrowCreate = true;
            bool rejected = false;
            try { await DailyTravel.Reuse(f.Workflow, packType: 9, surfaces: ["CafeteriaFieldDefaultUI"], seconds: 3); }
            catch (DailyStepException e) { rejected = e.Submitted; }
            Check(rejected && f.Box.Commands.Count == 1, "possibly submitted navigation is never replayed by arrival recovery");
        }
        using (var f = New("cancel"))
        {
            f.Frame["Scene"] = "Empty"; f.Page("MenuUI");
            f.OnDelay = () => f.Stopped = true;
            bool stopped = false;
            try { await DailyTravel.Reuse(f.Workflow, packType: 9, seconds: 3); }
            catch (StageHostException e) { stopped = e.Kind == "stopped"; }
            Check(stopped && f.Box.Commands.Count == 0, "arrival wait remains immediately cancellable");
        }
        using (var f = New("unknown-popup"))
        {
            f.Frame["Scene"] = "Map3007_001"; f.Page("CafeteriaFieldDefaultUI");
            f.Frame["Surfaces"]!.AsArray().Add(O(("Id", 2), ("Type", "UnknownPaymentPopupUI"), ("Popup", true), ("Order", 999), ("InputReady", true), ("Targets", new JsonArray())));
            bool blocked = false;
            try { await DailyTravel.Reuse(f.Workflow, packType: 9, surfaces: ["CafeteriaFieldDefaultUI"], seconds: 2); }
            catch (StageHostException e) { blocked = e.Kind == "adapter"; }
            Check(blocked && f.Box.Commands.Count == 0, "arrival recovery preserves unknown popups without confirmation");
        }
        foreach (string lobby in new[] { "PVPColosseumLobbyUI", "GameFieldDefaultUI", "CafeteriaFieldDefaultUI", "AnotherNativeLobbyUI" })
        {
            using var f = New("entry-" + lobby);
            f.Page("MenuUI", "_buttonPack"); f.Frame["Scene"] = "Map3008_001";
            f.OnCommand = c => { if (S(c["Kind"]) == "click" && N(c["TargetId"]) == 11) f.Page(lobby, "_buttonPackList"); else f.Page("PackListUI"); };
            await DailyTravel.PackList(f.Workflow, 5);
            Check(f.Box.Commands.Count == 2 && S(f.Box.Commands[0]["Kind"]) == "click" && N(f.Box.Commands[0]["TargetId"]) == 11 && N(f.Box.Commands[1]["TargetId"]) == 10, "return from menu then use actual cartridge capability: " + lobby);
        }
        using (var f = New("direct-lobby"))
        {
            f.Page("PVPColosseumLobbyUI", "_buttonPackList"); f.Frame["Scene"] = "Map3008_001";
            f.Readings = () => [WorkflowCases.Reading("navigation.pack", (DailyTravel.CurrentPack + ".Id", 3008), (DailyTravel.CurrentPack + ".PackType", 10))];
            Check(!await DailyTravel.Reuse(f.Workflow, packId: 3007, seconds: 2) && f.Time < 1 && f.Box.Commands.Count == 0, "different lobby cartridge identity resolves without field whitelist timeout");
        }
        using (var f = New("delayed-entry"))
        {
            f.Page("MenuUI"); f.Frame["Scene"] = "Empty";
            f.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("LoadingUI", id: 2));
            f.OnDelay = () => { if (f.Time >= 3 && S(f.Frame["Scene"]) == "Empty") { f.Frame["Scene"] = "Map3008_001"; f.Page("PVPColosseumLobbyUI", "_buttonPackList"); } };
            f.OnCommand = _ => f.Page("PackListUI");
            await DailyTravel.PackList(f.Workflow, 6);
            Check(f.Time >= 3.4 && f.Box.Commands.Count == 1, "cartridge entry waits through scene loading beyond old eight-poll limit");
        }
        foreach (string mode in new[] { "disabled", "duplicate", "unknown-popup", "battle" })
        {
            using var f = New("blocked-entry-" + mode);
            f.Page(mode == "battle" ? "BattleUI_PVP" : "PVPColosseumLobbyUI", "_buttonPackList");
            var row = f.Frame["Surfaces"]![0]!;
            if (mode == "disabled") row["Targets"]![0]!["Enabled"] = false;
            if (mode == "duplicate") row["Targets"]!.AsArray().Add(row["Targets"]![0]!.DeepClone());
            if (mode == "unknown-popup") f.Frame["Surfaces"]!.AsArray().Add(O(("Id", 2), ("Type", "UnknownPaymentPopupUI"), ("Popup", true), ("Order", 999), ("InputReady", true), ("Targets", new JsonArray())));
            bool blocked = false;
            try { await DailyTravel.PackList(f.Workflow, 1); } catch (StageHostException) { blocked = true; }
            Check(blocked && f.Box.Commands.Count == 0, "unsafe pack entry preserves scene: " + mode);
        }
        using (var f = New("list-not-ready"))
        {
            f.Page("PackListUI"); f.Frame["Surfaces"]![0]!["InputReady"] = false;
            f.OnDelay = () => { if (f.Time >= 1) f.Frame["Surfaces"]![0]!["InputReady"] = true; };
            await DailyTravel.PackList(f.Workflow, 3);
            Check(f.Time >= 1.4 && f.Box.Commands.Count == 0, "already visible pack list waits for interactive state");
        }
        using (var f = New("pack-preflight"))
        {
            f.Page("MenuUI");
            f.Driver.SubmissionGuard = () => { f.Page("PackListUI"); f.Driver.SubmissionGuard = null; };
            await DailyTravel.PackList(f.Workflow, 3);
            Check(f.Box.Commands.Count == 0, "pack list re-observes a disappeared menu before dispatch");
        }
        using (var f = New("pack-submitted-unknown"))
        {
            f.Page("MenuUI"); f.Box.ThrowCreate = true;
            bool blocked = false;
            try { await DailyTravel.PackList(f.Workflow, 3); } catch (DailyStepException e) { blocked = e.Submitted; }
            Check(blocked && f.Box.Commands.Count == 1, "pack navigation never replays an uncertain submitted command");
        }
    }
}
