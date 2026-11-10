using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
using static WorkflowCases;

static class PassRecoveryCases
{
    const string Selected = "ὢὡὧὮὫὫὬὥὪὪὢ", Groups = "ὨὤὭὯὣὥὡὯὯὭὬ";
    static JsonObject Pass(int id = 501, bool claimable = false, bool claimed = false, bool broken = false, long[]? active = null, bool advanced = false)
    {
        var group = O(("id", 700), ("missionGroupId", new[] {70}), ("eventMissionType", 3));
        var table = O(("id", id), ("expEventMissionGroupId", broken ? 999 : 700), ("newbiePassStep", id == 501 ? 6 : 7));
        var mission = O(("id", 701), ("groupType", 2), ("groupId", 70), ("conditionValue", 1), ("conditionType", 217));
        var cache = O(("EventId", "700"), ("Id", 701), ("GroupId", 70), ("Value", claimable || claimed ? 1 : 0), ("IsComplete", claimed));
        var rewards = advanced ? new JsonArray(O(("Key", 501), ("Value.Count", 1), ("Value", new JsonArray(O(("id", 1), ("passId", 501), ("basic", true)))))) : new JsonArray();
        active ??= [501];
        var e = Evidence(
            Reading("pass.active", ("ὠὤὭὯὯὪὯὧὮὪὬ.Count", active.Length), ("ὦὩὪὣὣὤὣὯὩὠὢ", 6), ("$items", Array(active.Select(x => O(("ὥὬὭὪὪὦὬὩὦὧὠ", x)))))),
            Reading("pass.scope", (Selected, id), (Groups+".Count", 1), (Groups+".Keys", new[] {0}), (Groups+".Values", new[] {new[] {701}}), ("ὥὠὣὪὮὭὧὣὩὩὧ", 0), (DailyPasses.Enabled, claimable && !claimed)),
            Reading("pass.definition", ("$self", O(("PassId", id), ("Title", "Synthetic guide pass"), ("Table", table.ToJsonString()), ("Group", group.ToJsonString()), ("Missions", new[] {mission.ToJsonString()})))),
            Reading("pass.cache", ("Count", 1), ("$items", new JsonArray(cache))),
            Reading("pass.rewards", ("Count", rewards.Count), ("$items", rewards)));
        e["Error"] = "";
        return e;
    }
    public static async Task Run(string root, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        void Reject(Action action, string name) { try { action(); } catch (InvalidDataException) { cases.Add(name); return; } throw new Exception(name); }
        var initial = Pass(claimable: true);
        Check(N(DailyPasses.Report(initial, JsonValue.Create("today")!)["claimable"]) == 1, "guide pass without UsePass uses native table/group relationship");
        var unnamed = DailyPasses.Report(initial, JsonValue.Create("today")!);
        unnamed["missions"]![0]!["title_id"] = 77;
        var named = DailyPasses.WithTitles(unnamed, new() { [77] = O(("textCn", "完成任务")) });
        Check(S(named["missions"]![0]!["title"]) == "完成任务" && S(unnamed["missions"]![0]!["title"]) == "701"
            && N(named["missions"]![0]!["progress"]) == 1, "post-claim pass report restores names without changing verified progress or the transaction proof");
        Reject(() => DailyPasses.Report(Pass(broken: true), JsonValue.Create("today")!), "guide pass still rejects a mismatched mission group");
        var op = O(("before", initial), ("cycle", "today"));
        var events = new JsonArray(Event("missions.clear", "request", 1), Event("missions.clear", "response", 2), Event("pass.reward", "request", 3), Event("pass.reward", "response", 4, ("NewbiePassStep", 7)));
        var next = Pass(502, claimed: true, active: [502], advanced: true);
        Check(B(DailyPasses.Verify(op, events, next)["newbie_advanced"]), "confirmed guide progression may replace the current pass and catalogue");
        foreach (string change in new[] {"no-progress-reply", "missing-reply", "unclaimed-cache", "changed-actor"})
        {
            var ev = events.DeepClone().AsArray(); var after = next.DeepClone().AsObject();
            if (change == "no-progress-reply") ev[3]!["Values"] = new JsonArray();
            if (change == "missing-reply") ev.RemoveAt(3);
            if (change == "unclaimed-cache") after = Pass(502, active: [502], advanced: true);
            if (change == "changed-actor") after["Frame"]!["AccountKey"] = "other";
            Reject(() => DailyPasses.Verify(op, ev, after), "pass progression preserves evidence guard: " + change);
        }
        // Unknown first page must not hide a healthy later page or authorize a claim.
        foreach (bool claim in new[] {false, true})
        {
            string assets = Path.Combine(root, "pass-assets-" + claim);
            Directory.CreateDirectory(Path.Combine(assets, "data", "daily", "missions"));
            File.WriteAllText(Path.Combine(assets, "data", "daily", "missions", "LocalTextTable.json"), "[]");
            using var h = new WorkflowHarness(Path.Combine(root, "pass-isolation-"+claim), DailyMissions.Proofs().ToArray(), ["missions.query", "missions.event_query"], assetDirectory: assets);
            int selected = 501;
            h.Page("PassUI", "$pointer/Guide/Button - Tab - Mission", "_objBackButton");
            h.Frame["Surfaces"]![0]!["Targets"]![0]!["Route"] = "pointer";
            h.Readings = () => Rows(Pass(selected, broken: selected == 501, active: [501, 502])["Readings"]);
            h.OnCommand = c =>
            {
                if (S(c["Kind"]) == "reward_refresh")
                {
                    foreach (string role in new[] {"missions.query", "missions.event_query"}) { h.Event(role, "request"); h.Event(role, "response"); }
                }
                if (S(c["Kind"]) == "pass_select") selected = I(c["Value"]);
                if (S(c["Kind"]) == "click" && N(c["TargetId"]) == 11) h.Page("MenuUI");
            };
            var result = await DailyPasses.Run(h.Workflow, claim);
            Check(S(result["state"]) == "partial" && Rows(result["issues"]).Length == 1 && Rows(result["reports"]).Single()["selected_pass_id"]!.GetValue<long>() == 502, "incompatible pass isolated with healthy later page in claim mode " + claim);
            Check(h.Time < 5 && !h.Box.Commands.Any(c => S(c["Reason"]).Contains("领取全部")), "stable pass schema error fails promptly without claims in mode " + claim);
            Check(DailyNavigationDecision.Types(h.Frame).Contains("MenuUI"), "isolated pass returns to usable menu in mode " + claim);
        }
        using (var h = new WorkflowHarness(Path.Combine(root, "pass-page-race"), DailyMissions.Proofs().ToArray(), ["missions.query", "missions.event_query"], assetDirectory: Path.Combine(root, "pass-assets-False")))
        {
            int selected = 501; bool arm = false, captured = false, changed = false; int newPageClicks = 0;
            void Page(string name) { h.Page("PassUI", "$pointer/" + name + "/Button - Tab - Mission", "_objBackButton"); h.Frame["Surfaces"]![0]!["Targets"]![0]!["Route"] = "pointer"; }
            Page("OldGuide");
            h.Readings = () => Rows(Pass(selected, active: [501, 502])["Readings"]);
            var publish = h.Box.AfterWrite;
            h.Box.AfterWrite = (channel, file, bytes) => { publish?.Invoke(channel, file, bytes); if (arm && file == "observation-request.json") captured = true; };
            h.OnRead = () => { if (captured && !changed) { changed = true; Page("Seasonal"); } };
            h.OnCommand = c => {
                if (S(c["Kind"]) == "reward_refresh") foreach (string role in new[] {"missions.query", "missions.event_query"}) { h.Event(role, "request"); h.Event(role, "response"); }
                if (S(c["Kind"]) == "pass_select") { selected = I(c["Value"]); arm = selected == 502; }
                if (S(c["Kind"]) == "pointer" && selected == 502) { if (!changed) throw new Exception("Sent an obsolete pass tab"); newPageClicks++; }
                if (S(c["Kind"]) == "click" && N(c["TargetId"]) == 11) h.Page("MenuUI");
            };
            var result = await DailyPasses.Run(h.Workflow, false);
            Check(changed && newPageClicks == 1 && Rows(result["reports"]).Select(r => N(r["selected_pass_id"])).ToHashSet().SetEquals([501L, 502L]),
                "pass switch discards obsolete mission target and reads each pass once without replaying a claim");
        }
        // Reproduce a switched, fully loaded pass with one enabled AllRecv
        // pointer but InputReady=false. The virtual clock also covers the second
        // and third preparation attempts without real waits or game access.
        foreach (string mode in new[] {"render", "second-attempt", "third-attempt", "mission-tab", "eligibility", "late-eligibility", "already-claimed", "permanent", "cancel", "actor-change"})
        {
            using var h = new WorkflowHarness(Path.Combine(root, "pass-ready-" + mode), DailyMissions.Proofs().ToArray(), ["missions.query", "missions.event_query"], assetDirectory: Path.Combine(root, "pass-assets-True"));
            int selected = 145; bool armed = false, restored = false;
            double started = 0, wait = mode switch { "second-attempt" => 13, "third-attempt" => 26, _ => .6 };
            var claimed = new HashSet<int>();
            void Page()
            {
                h.Page("PassUI", "$pointer/Season/Button - Tab - Mission", "_objBackButton", "$pointer/Season/Button - AllRecv");
                foreach (int index in new[] {0, 2}) h.Frame["Surfaces"]![0]!["Targets"]![index]!["Route"] = "pointer";
            }
            Page();
            h.Readings = () =>
            {
                var e = Pass(selected, claimable: !(mode == "late-eligibility" && selected == 146 && armed && !restored), claimed: claimed.Contains(selected), active: [145, 146]);
                if (mode == "eligibility" && selected == 146 && armed && !restored)
                    foreach (var reading in Rows(e["Readings"]).Where(r => S(r["Id"]) == "pass.scope"))
                        foreach (var value in Rows(reading["Values"]).Where(v => S(v["Path"]) == DailyPasses.Enabled)) value["Json"] = "false";
                return Rows(e["Readings"]);
            };
            h.OnCommand = c =>
            {
                string kind = S(c["Kind"]);
                if (kind == "reward_refresh") foreach (string role in new[] {"missions.query", "missions.event_query"}) { h.Event(role, "request"); h.Event(role, "response"); }
                if (kind == "pass_select")
                {
                    selected = I(c["Value"]); Page();
                    if (mode == "mission-tab" && selected == 146 && !armed) { armed = true; started = h.Time; h.Frame["Surfaces"]![0]!["InputReady"] = false; }
                }
                if (kind == "pointer" && N(c["TargetId"]) == 10 && selected == 146 && !armed)
                {
                    armed = true; started = h.Time;
                    if (mode is "render" or "late-eligibility" or "already-claimed" or "cancel" or "actor-change") h.Frame["Surfaces"]![0]!["InputReady"] = false;
                    if (mode is "second-attempt" or "permanent") h.Frame["Surfaces"]![0]!["Targets"]!.AsArray().RemoveAt(2);
                    if (mode == "third-attempt") h.Frame["Surfaces"]![0]!["Targets"]!.AsArray().Add(h.Frame["Surfaces"]![0]!["Targets"]![2]!.DeepClone());
                }
                if (kind == "pointer" && N(c["TargetId"]) == 12)
                {
                    if (selected == 146 && !restored) throw new Exception("Claim dispatched before current pass input settled: " + mode);
                    if (!claimed.Add(selected)) throw new Exception("Duplicate claim: " + mode);
                    foreach (string role in new[] {"missions.clear", "pass.reward"}) { h.Event(role, "request"); h.Event(role, "response"); }
                }
                if (kind == "click" && N(c["TargetId"]) == 11) h.Page("MenuUI");
            };
            h.OnDelay = () =>
            {
                if (!armed || restored || h.Time - started < wait || mode == "permanent") return;
                if (mode == "cancel") { h.Stopped = true; return; }
                if (mode == "actor-change") return;
                restored = true;
                if (mode == "already-claimed") claimed.Add(146);
                Page();
            };
            h.OnRead = () => { if (mode == "actor-change" && armed && h.Time - started >= wait) throw new StageHostException("identity", "synthetic changed session"); };
            JsonObject? result = null; string failure = "";
            try { result = await DailyPasses.Run(h.Workflow); }
            catch (StageHostException error) { failure = error.Kind; }
            int claims = h.Box.Commands.Count(c => S(c["Kind"]) == "pointer" && N(c["TargetId"]) == 12);
            if (mode is "cancel" or "actor-change")
                Check(result == null && claims == 1 && failure == (mode == "cancel" ? "stopped" : "identity") && h.Time < 5, "pass preparation promptly preserves " + mode);
            else if (mode == "permanent")
            {
                Check(S(result?["state"]) == "partial" && Rows(result?["issues"]).Length == 1 && claims == 1 && h.Time is >= 36 and < 40, "unavailable claim is bounded to three attempts and retains prior success");
                restored = true; Page();
                var resumed = await DailyPasses.Run(h.Workflow);
                Check(S(resumed["state"]) == "completed" && claimed.SetEquals([145, 146]), "retrying the stage after restored conditions claims only the missed pass");
            }
            else
                Check(S(result?["state"]) == "completed" && Rows(result?["issues"]).Length == 0 && claimed.SetEquals([145, 146]) && claims == (mode == "already-claimed" ? 1 : 2) && h.Time >= wait && h.Time < wait + 5, "pass preparation recovers from " + mode);
            if (result != null)
            {
                Check(DailyNavigationDecision.Types(h.Frame).Contains("MenuUI"), "pass preparation returns to menu after " + mode);
                int count = h.Box.Commands.Count;
                restored = true; Page();
                if (mode == "permanent") claimed.Add(146);
                var again = await DailyPasses.Run(h.Workflow);
                Check(S(again["state"]) == "completed" && !h.Box.Commands.Skip(count).Any(c => S(c["Kind"]) == "pointer" && N(c["TargetId"]) == 12), "repeated pass recovery does not reclaim completed rewards after " + mode);
            }
        }
        foreach (string mode in new[] {"progression", "unknown-claim", "stopped", "identity"})
        {
            using var h = new WorkflowHarness(Path.Combine(root, "pass-guard-"+mode), DailyMissions.Proofs().ToArray(), ["missions.query", "missions.event_query"], assetDirectory: Path.Combine(root, "pass-assets-True"));
            bool advanced = false, initialized = false;
            h.Page("PassUI", "$pointer/Guide/Button - Tab - Mission", "_objBackButton", "$pointer/Guide/Button - AllRecv");
            foreach (int index in new[] {0, 2}) h.Frame["Surfaces"]![0]!["Targets"]![index]!["Route"] = "pointer";
            h.Readings = () => Rows((advanced ? Pass(502, claimed: true, active: [502], advanced: true) : Pass(claimable: true))["Readings"]);
            h.OnRead = () => { if (initialized && mode is "stopped" or "identity") throw new StageHostException(mode, "synthetic " + mode); };
            h.OnCommand = c =>
            {
                if (S(c["Kind"]) == "reward_refresh")
                    foreach (string role in new[] {"missions.query", "missions.event_query"}) { h.Event(role, "request"); h.Event(role, "response"); }
                if (S(c["Kind"]) == "pointer" && N(c["TargetId"]) == 10) initialized = true;
                if (S(c["Kind"]) == "click" && N(c["TargetId"]) == 11) h.Page("MenuUI");
                if (S(c["Kind"]) == "pointer" && N(c["TargetId"]) == 12 && mode == "progression")
                {
                    h.Event("missions.clear", "request"); h.Event("missions.clear", "response");
                    h.Event("pass.reward", "request"); h.Event("pass.reward", "response", ("NewbiePassStep", 7));
                    advanced = true;
                }
            };
            JsonObject? result = null; string failure = "";
            try { result = await DailyPasses.Run(h.Workflow); }
            catch (StageHostException error) { failure = error.Kind; }
            catch (DailyStepException error) when (mode is "stopped" or "identity" && error.Kind == "pending" && error.Message.EndsWith("synthetic " + mode, StringComparison.Ordinal)) { failure = mode; }
            if (mode == "progression")
            {
                Check(failure == "" && S(result?["state"]) == "completed" && Rows(result?["operations"]).Length == 2, "pass runner follows server advancement to newly active page");
                Check(Rows(result?["reports"]).Length == 1 && N(Rows(result?["reports"])[0]["selected_pass_id"]) == 502, "newbie progression does not duplicate next-page missions");
            }
            else
                Check(result == null && failure == (mode == "unknown-claim" ? "pending" : mode), "pass isolation preserves workflow guard: " + mode);
            int submitted = h.Box.Commands.Count(c => S(c["Kind"]) == "pointer" && N(c["TargetId"]) == 12);
            Check(submitted == (mode is "progression" or "unknown-claim" ? 1 : 0), "pass claims are never replayed after " + mode);
            if (mode == "unknown-claim")
            {
                h.Dispose(); // Simulate process exit before opening the same journal.
                using var resumed = new WorkflowHarness(h.Root, DailyMissions.Proofs().ToArray(), ["missions.query", "missions.event_query"], assetDirectory: Path.Combine(root, "pass-assets-True"));
                resumed.Page("PassUI", "$pointer/Guide/Button - Tab - Mission", "_objBackButton", "$pointer/Guide/Button - AllRecv");
                foreach (int index in new[] {0, 2}) resumed.Frame["Surfaces"]![0]!["Targets"]![index]!["Route"] = "pointer";
                resumed.Readings = () => Rows(Pass(claimed: true)["Readings"]);
                resumed.OnCommand = c =>
                {
                    if (S(c["Kind"]) == "reward_refresh") foreach (string role in new[] {"missions.query", "missions.event_query"}) { resumed.Event(role, "request"); resumed.Event(role, "response"); }
                    if (S(c["Kind"]) == "click" && N(c["TargetId"]) == 11) resumed.Page("MenuUI");
                };
                Check(S((await DailyPasses.Run(resumed.Workflow))["state"]) == "completed" && !resumed.Box.Commands.Any(c => N(c["TargetId"]) == 12), "restart with an uncertain journal accepts native claimed state without replaying the saved claim");
            }
        }
        var row = O(("instance", 10), ("skill", O(("classType", 1), ("resetType", 2), ("groupId", 114), ("id", 1))), ("disabled", false));
        var pending = new[] {O(("Group", 114))};
        var ready = O(("Instance", 10), ("Group", 114), ("Reason", ""), ("Cooldown", 0), ("Gate", ""));
        Check(DailyWeeklySteal.Choose([row], [ready], pending) != null, "ready steal talent remains usable");
        foreach (string gate in new[] {"talent_wait:timeline", "talent_wait:attempt_latch", "talent_wait:animation", "talent_reject:character_unavailable"})
        {
            ready["Gate"] = gate;
            Check(DailyWeeklySteal.Choose([row], [ready], pending) == null, "steal respects native gate " + gate);
            Check(DailyWeeklySteal.WaitingForTalent([ready], pending) == gate.StartsWith("talent_wait:"), "steal waits only for transient gate " + gate);
        }
    }
}
