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
