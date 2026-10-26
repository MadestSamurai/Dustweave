using Dustweave;
using System.Text.Json.Nodes;
using System.Text.Json;
using static Dustweave.DailyData;
static class ManagedInputsCases
{
    private static async Task RewardCleanupState(string root, List<string> cases)
    {
        foreach (string mode in new[] { "ready", "opening", "unready", "missing", "covered", "state-pending", "changing", "reward-open", "reward-closed", "reward-complete" })
        {
            using var h = new WorkflowHarness(Path.Combine(root, "reward-cleanup-" + mode), []);
            const string destination = "MailUI";
            h.Page(destination);
            if (mode is "opening" or "unready") h.Frame["Surfaces"]![0]!["InputReady"] = false;
            if (mode == "missing") h.Page("MenuUI");
            if (mode == "covered") h.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("UnknownPopupUI", true, "_close", 2));
            bool hasReward = mode.StartsWith("reward-", StringComparison.Ordinal);
            if (hasReward) h.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("RewardReceivePopupUI", true, "", 2));
            h.Readings = () => [WorkflowCases.Reading("reward.presentation",
                ("ὣὤὥὦὯὦὩὤὨὠὪ", mode != "reward-open"), ("ὪὯὣὥὬὫὬὩὠὮὠ", true),
                ("ὮὬὧὦὣὠὠὤὮὦὧ", true), ("ὦὡὮὫὧὠὮὡὭὭὬ", false))];
            h.OnDelay = () => {
                if (mode == "opening" && h.Time >= .3) h.Frame["Surfaces"]![0]!["InputReady"] = true;
                if (mode == "changing") h.Frame["UiToken"] = "refresh-" + h.Time;
            };
            int closes = 0;
            h.OnCommand = c => {
                if (!hasReward || S(c["Kind"]) != "back" || N(c["SurfaceId"]) != 2)
                    throw new Exception("Reward cleanup dispatched an unrelated operation");
                closes++;
                h.Page(destination);
                if (mode == "reward-closed") h.Frame["Surfaces"]![0]!["InputReady"] = false;
            };
            string error = "";
            try { await h.Driver.DismissRewardAsync([destination], true, () => Task.FromResult(mode != "state-pending")); }
            catch (StageHostException e) { error = e.Message; }
            string expected = mode switch {
                "ready" or "opening" or "reward-complete" => "",
                "reward-open" => "Reward presentation is still open",
                "reward-closed" => "Reward presentation closed but destination is not ready",
                "state-pending" => "Reward destination state has not refreshed",
                "changing" => "Reward destination is still changing",
                _ => "Reward destination is not ready"
            };
            if (error != expected || closes != (mode is "reward-closed" or "reward-complete" ? 1 : 0))
                throw new Exception($"Reward cleanup state {mode}: expected '{expected}', got '{error}', closes={closes}");
            if (expected.Length > 0 && (h.Time < 40 || h.Time > 41)) throw new Exception("Reward cleanup changed its existing timeout");
            cases.Add("reward cleanup distinguishes presentation, destination and refreshed state without replay: " + mode);
        }
    }
    private static async Task CostumeTicketNotice(string root, List<string> cases)
    {
        const string ui = "CostumeSelectNoticePopupUI";
        foreach (string mode in new[] { "mail", "event", "resume", "opening", "disabled", "unknown", "stopped", "identity", "never-ready", "stuck" })
        {
            using var h = new WorkflowHarness(Path.Combine(root, "costume-ticket-" + mode), []);
            string destination = mode == "event" ? "EventUI" : "MailUI";
            h.Page(destination);
            var popup = HomeNavigationCases.Surface(ui, true, "_cancelBtn", 2);
            popup["Targets"]!.AsArray().Add(O(("Id", 103), ("Field", "_okBtn"), ("Enabled", true), ("Route", "ui")));
            if (mode is "opening" or "never-ready" or "stopped" or "identity") popup["InputReady"] = false;
            if (mode == "disabled") popup["Targets"]![0]!["Enabled"] = false;
            h.Frame["Surfaces"]!.AsArray().Add(popup);
            if (mode == "unknown") h.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("MessagePopupUI", true, "_buttonOK", 99));
            h.Frame["UiToken"] = "ticket-notice";
            h.OnDelay = () => {
                if (h.Time < .3) return;
                if (mode == "opening") popup["InputReady"] = true;
                if (mode == "disabled") popup["Targets"]![0]!["Enabled"] = true;
                if (mode == "stopped") h.Stopped = true;
                if (mode == "identity") h.Frame["AccountKey"] = "changed";
            };
            int cancellations = 0, exits = 0;
            h.OnCommand = c => {
                if (S(c["Kind"]) == "click" && N(c["SurfaceId"]) == 2 && N(c["TargetId"]) == 102)
                {
                    cancellations++;
                    if (mode != "stuck") h.Page(destination);
                    else h.Time += 45;
                }
                else if (mode == "resume" && S(c["Kind"]) == "back" && !DailyNavigationDecision.Types(h.Frame).Contains(ui))
                { exits++; h.Page("MenuUI"); }
                else throw new Exception("Unexpected ticket use or repeated business operation: " + c);
            };
            Exception? failure = null;
            try
            {
                if (mode == "resume") await h.Workflow.Step(O(("ui", destination), ("back", true), ("expect", "MenuUI"), ("timeout", 3)));
                else await h.Driver.DismissRewardAsync(destination, true);
            }
            catch (Exception e) { failure = e; }
            bool success = mode is "mail" or "event" or "resume" or "opening" or "disabled";
            if (success ? failure != null || cancellations != 1 || exits != (mode == "resume" ? 1 : 0)
                : failure == null || cancellations != (mode == "stuck" ? 1 : 0) || exits != 0)
                throw new Exception("Costume ticket shared recovery failed: " + mode, failure);
            if (h.Time > 75) throw new Exception("Ticket cancellation exceeded its bounded deadline");
            cases.Add("costume ticket preserves inventory during reward cleanup and resumed input: " + mode);
        }
        foreach (string mode in new[] { "confirm", "back", "native", "operation", "pointer" })
        {
            using var h = new WorkflowHarness(Path.Combine(root, "costume-ticket-reject-" + mode), []);
            h.Page(ui, "_cancelBtn", "_okBtn");
            var action = O(("ui", ui), ("field", mode == "confirm" ? "_okBtn" : "_cancelBtn"));
            if (mode is "back" or "native") action[mode] = true;
            if (mode == "operation") action["operation"] = "story_advance";
            if (mode == "pointer") h.Frame["Surfaces"]![0]!["Targets"]![0]!["Route"] = "pointer";
            bool rejected = false;
            try { await h.Driver.SendObservedAsync(action); }
            catch (DailyStepException e) when (e.Kind == "rejected") { rejected = true; }
            if (!rejected || h.Box.Commands.Count != 0) throw new Exception("Unsafe ticket operation dispatched: " + mode);
            cases.Add("costume ticket submission only allows native cancellation: " + mode);
        }
    }
    private static async Task TacticsSeasonNotice(string root, List<string> cases)
    {
        const string lobby = "TarosTacticsBingoUI", notice = "TarosTacticsSeasonFinishUI";
        foreach (string mode in new[] { "arrival", "resume", "opening", "disabled", "unknown", "stopped", "identity", "never-ready", "stuck" })
        {
            using var h = new WorkflowHarness(Path.Combine(root, "tactics-season-" + mode), []);
            var popup = HomeNavigationCases.Surface(notice, true, "_btnBack", 2);
            void ShowLobby()
            {
                h.Page(lobby);
                h.Frame["Surfaces"]!.AsArray().Add(popup);
                if (mode == "unknown") h.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("MessagePopupUI", true, "_buttonOK", 9));
                h.Frame["UiToken"] = "season-notice";
            }
            if (mode is "opening" or "never-ready" or "stopped" or "identity") popup["InputReady"] = false;
            if (mode == "disabled") popup["Targets"]![0]!["Enabled"] = false;
            if (mode == "arrival") h.Page("MenuUI", "_btnTarosTactics"); else ShowLobby();
            h.OnDelay = () => {
                if (h.Time < .3) return;
                if (mode == "opening") popup["InputReady"] = true;
                if (mode == "disabled") popup["Targets"]![0]!["Enabled"] = true;
                if (mode == "stopped") h.Stopped = true;
                if (mode == "identity") h.Frame["AccountKey"] = "changed";
            };
            int acknowledgements = 0, selections = 0;
            h.OnCommand = c => {
                if (S(c["Kind"]) == "tactics_select")
                {
                    if (DailyNavigationDecision.Types(h.Frame).Contains(notice)) throw new Exception("Selection went through season notice");
                    selections++; h.Page("TarosTacticsBingoBattleInfoPopupUI");
                }
                else if (N(c["SurfaceId"]) == 2 && N(c["TargetId"]) == 102)
                {
                    acknowledgements++;
                    if (mode != "stuck") h.Page(lobby);
                    else h.Time += 25; // Original acknowledgement must not be replayed.
                }
                else if (mode == "arrival" && S(c["Kind"]) == "click") ShowLobby();
                else throw new Exception("Unexpected tactics navigation: " + c);
            };
            Exception? failure = null;
            try
            {
                if (mode == "arrival")
                {
                    await h.Workflow.Step("MenuUI", "_btnTarosTactics", expect: lobby);
                    if (DailyNavigationDecision.Types(h.Frame).Contains(notice)) throw new Exception("Arrival returned behind the season notice");
                }
                await h.Workflow.Step(O(("ui", lobby), ("operation", "tactics_select"), ("value", 1),
                    ("items", new JsonArray(1732, 9)), ("expect", "TarosTacticsBingoBattleInfoPopupUI"), ("timeout", 2)));
            }
            catch (Exception e) { failure = e; }
            bool success = mode is "arrival" or "resume" or "opening" or "disabled";
            if (success ? failure != null || selections != 1 || acknowledgements != 1
                : failure == null || selections != 0 || acknowledgements != (mode == "stuck" ? 1 : 0))
                throw new Exception("Tactics season handling failed: " + mode, failure);
            cases.Add("Tactics new-season presentation uses shared driver without replay: " + mode);
        }
    }
    private static async Task OpeningTalentNotice(string root, List<string> cases)
    {
        foreach (string mode in new[] { "opens", "disabled-button", "never-ready", "unknown", "changes", "account", "stopped" })
        {
            using var h = new WorkflowHarness(Path.Combine(root, "opening-talent-" + mode), []);
            h.Frame["Scene"] = "Map0008_001";
            h.Page("GameFieldDefaultUI");
            var popup = O(("Id", 2), ("Type", "MessagePopupUI"), ("Popup", true), ("Order", 10000),
                ("InputReady", mode == "disabled-button"), ("NativeContext", mode == "unknown" ? "" : "talent_inactive_ack_only"),
                ("Targets", new JsonArray(O(("Id", 20), ("Field", "_buttonOK"), ("Route", "ui"), ("Enabled", mode != "disabled-button")))));
            h.Frame["Surfaces"]!.AsArray().Add(popup);
            h.Frame["UiToken"] = "opening-talent";
            h.OnDelay = () => {
                if (h.Time < .3) return;
                if (mode is "opens" or "disabled-button") { popup["InputReady"] = true; popup["Targets"]![0]!["Enabled"] = true; }
                if (mode == "changes") popup["NativeContext"] = "";
                if (mode == "account") h.Frame["AccountKey"] = "changed";
                if (mode == "stopped") h.Stopped = true;
            };
            h.OnCommand = c => {
                if (S(c["Kind"]) == "talent_error_ack")
                {
                    if (!B(popup["InputReady"]) || !B(popup["Targets"]![0]!["Enabled"])) throw new Exception("Input sent behind opening talent notice");
                    h.Page("GameFieldDefaultUI");
                }
                else if (S(c["Kind"]) != "mainline_cancel_nav") throw new Exception("Unexpected retry or talent consumption");
            };
            Exception? failure = null;
            try { await h.Driver.SendObservedAsync(O(("ui", "GameFieldDefaultUI"), ("operation", "mainline_cancel_nav"), ("timeout", 2))); }
            catch (Exception e) { failure = e; }
            bool success = mode is "opens" or "disabled-button";
            if (success ? failure != null || h.Time < .3 || !h.Box.Commands.Select(c => S(c["Kind"])).SequenceEqual(new[] { "talent_error_ack", "mainline_cancel_nav" })
                : failure == null || h.Box.Commands.Count != 0)
                throw new Exception("Opening talent notice recovery failed: " + mode, failure);
            if (mode == "never-ready" && (h.Time < 2 || h.Time > 3)) throw new Exception("Talent readiness extended the original timeout");
            cases.Add("opening talent notice stays in current step without consuming or replaying business input: " + mode);
        }
    }
    private static async Task MailRecovery(string root, List<string> cases)
    {
        foreach(bool remaining in new[]{false,true})
        {
            using var f=new WorkflowHarness(Path.Combine(root,"mail-current-state-"+remaining),[],["mail.collect"]);
            f.Page("MailUI","_objAllReceiveButton");
            string tab="normal";bool claimable=remaining;int claims=0, ticketCancellations=0;
            f.Readings=()=>[
                WorkflowCases.Reading("mail.ui",("ὭὯὫὫὮὡὠὮὤὬὨ",tab)),
                WorkflowCases.Reading("mail.normal",("Count",claimable?1:0),("$items",claimable?new JsonArray(O(("InvenIndex",42))):new JsonArray())),
                WorkflowCases.Reading("mail.cash",("Count",0),("$items",new JsonArray())),
                WorkflowCases.Reading("mail.cash_total",("$self",0))];
            var oldPaths=new List<string>();
            foreach(string oldTab in new[]{"normal","cash"})
            {
                string id=Guid.NewGuid().ToString("N");
                string path=Path.Combine(f.Root,"live","business",id+".json");oldPaths.Add(path);
                DailyJson.Write(path,O(("id",id),("role","mail.collect"),("state","unknown"),("at",f.Ticks),
                    ("account",f.Context["actor"]![3]),("player",f.Context["actor"]![4]),("server",f.Context["server"]),
                    ("cycle","yesterday"),("scope",O(("tab",oldTab))),("before",f.Evidence()),("events",new JsonArray())));
            }
            f.OnCommand=c=>{
                if(S(c["Kind"])=="mail_tab")tab=N(c["Value"])==0?"normal":"cash";
                else if(S(c["Kind"])=="back")f.Page("MenuUI");
                else if(S(c["Kind"])=="click"){
                    if (N(c["SurfaceId"]) == 2 && N(c["TargetId"]) == 102)
                    {
                        ticketCancellations++; f.Page("MailUI", "_objAllReceiveButton"); return;
                    }
                    if(!claimable||tab!="normal")throw new Exception("Unexpected repeated mail claim");
                    claims++;f.Event("mail.collect","request");claimable=false;f.Event("mail.collect","response",("RewardInfoBundle",new JsonObject()));
                    f.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("CostumeSelectNoticePopupUI", true, "_cancelBtn", 2));
                    f.Frame["UiToken"] = "claimed-ticket-notice";
                }else throw new Exception("Unexpected mail action: "+c);
            };
            var stage=new DailyMailStage(f.Root,f.Driver,()=>false,()=>f.Time,t=>{f.Time+=t.TotalSeconds;return Task.CompletedTask;});
            var result=await stage.ExecuteAsync(f.Context,(_,_,_)=>throw new Exception("Unexpected mail relay"));
            if(claims!=(remaining?1:0)||ticketCancellations!=claims||S(result["state"])!=(remaining?"completed":"skipped")||
                oldPaths.Any(p=>S(DailyJson.TryRead<JsonObject>(p)!["state"])!="superseded"))throw new Exception("Old mail receipt blocked fresh mailbox state");
            cases.Add("both interrupted mailbox tabs recover from native remaining IDs, claims="+claims);
        }
    }
    private static async Task ProtectedMail(string root, List<string> cases)
    {
        JsonObject Mail(long id, params int[] types) => O(("InvenIndex", id), ("RewardExpireTime", 1),
            ("ItemType", types), ("ItemId", types.Select(_ => 0).ToArray()), ("ItemCount", types.Select(_ => 1).ToArray()));
        foreach (string mode in new[] { "mixed", "only-protected", "off", "missing", "mismatch", "paged", "page-stuck", "repeat", "interrupted", "rejected", "identity" })
        {
            using var f = new WorkflowHarness(Path.Combine(root, "protected-mail-" + mode), [], ["mail.collect"]);
            string tab = "normal";
            var normal = new List<JsonObject> { Mail(10, 15), Mail(11, 4, 24) };
            var cash = new List<JsonObject> { Mail(30, 24), Mail(31, 4) };
            if (mode != "only-protected") normal.AddRange(Enumerable.Range(100, 23).Select(i => Mail(i, 4)));
            else cash.RemoveAt(1);
            if (mode == "missing") normal[0].Remove("ItemType");
            if (mode == "mismatch") normal[0]["ItemId"] = new JsonArray();
            bool pageLoaded = mode is not ("paged" or "page-stuck");
            f.Page("MailUI", "_objAllReceiveButton");
            f.Readings = () => [
                WorkflowCases.Reading("mail.ui", ("ὭὯὫὫὮὡὠὮὤὬὨ", tab)),
                WorkflowCases.Reading("mail.normal", ("Count", normal.Count), ("$items", Array(normal))),
                WorkflowCases.Reading("mail.cash", ("Count", cash.Count), ("$items", Array(cash))),
                WorkflowCases.Reading("mail.normal_total", ("$self", normal.Count + (pageLoaded ? 0 : 1))),
                WorkflowCases.Reading("mail.cash_total", ("$self", cash.Count))];
            int claims = 0, pages = 0;
            f.OnCommand = c => {
                string kind = S(c["Kind"]);
                if (kind == "mail_tab") { tab = N(c["Value"]) == 0 ? "normal" : "cash"; return; }
                if (kind == "back") { f.Page("MenuUI", "_buttonMail"); return; }
                if (kind == "mail_page")
                {
                    pages++;
                    if (mode == "paged") { pageLoaded = true; normal.Add(Mail(200, 4)); }
                    return;
                }
                if (kind == "click" && DailyNavigationDecision.Types(f.Frame).Contains("MenuUI")) { f.Page("MailUI", "_objAllReceiveButton"); return; }
                var list = tab == "normal" ? normal : cash;
                long[] ids;
                if (mode == "off")
                {
                    if (kind != "click") throw new Exception("Disabled preservation changed Claim All behavior");
                    ids = list.Select(r => N(r["InvenIndex"])).ToArray();
                }
                else
                {
                    if (kind != "mail_collect_filtered") throw new Exception("Filtered mailbox used Claim All");
                    ids = c["Items"]!.AsArray().Select(N).ToArray();
                    if (ids.Length is < 1 or > 10 || list.Where(r => ids.Contains(N(r["InvenIndex"]))).Any(r => r["ItemType"]!.AsArray().Any(t => N(t) is 15 or 24)))
                        throw new Exception("Protected or oversized mailbox batch");
                }
                claims += ids.Length;
                if (mode == "identity") f.Frame["AccountKey"] = "changed";
                if (mode == "rejected") { f.Box.Error = "mail_selection_rejected"; return; }
                for (int n = 0; n < (ids.Length + 9) / 10; n++) f.Event("mail.collect", "request");
                list.RemoveAll(r => ids.Contains(N(r["InvenIndex"])));
                for (int n = 0; n < (ids.Length + 9) / 10; n++) f.Event("mail.collect", "response", ("RewardInfoBundle", new JsonObject()));
                if (mode == "interrupted" && claims == 10) f.Stopped = true;
            };
            var stage = new DailyMailStage(f.Root, f.Driver, () => f.Stopped, () => f.Time,
                t => { f.Time += t.TotalSeconds; return Task.CompletedTask; }, () => mode != "off");
            Exception? failure = null;
            JsonObject? result = null;
            try
            {
                var dependency = new DailyEventMailDependency(true);
                await dependency.Collect(async () => result = await stage.ExecuteAsync(f.Context, (_, _, _) => throw new Exception("Unexpected relay")));
                if (dependency.Required) throw new Exception("Preservation blocked event token dependency");
                if (mode == "repeat")
                {
                    int before = claims;
                    await stage.ExecuteAsync(f.Context, (_, _, _) => throw new Exception("Unexpected relay"));
                    if (claims != before) throw new Exception("Repeat claimed retained mail");
                }
            }
            catch (Exception e) { failure = e; }
            if (mode == "interrupted")
            {
                if (failure == null || claims != 10) throw new Exception("Interrupted-mail fixture did not stop after first receipt");
                f.Stopped = false;
                int inputs = f.Box.Commands.Count;
                var reconciled = await new DailyManagedReconciliation(f.Root, f.Driver, ["mail.collect"], () => false).RunAsync(f.Context);
                if (reconciled["completed"]!.AsArray().Count != 1 || reconciled["unresolved"]!.AsArray().Count != 0 || inputs != f.Box.Commands.Count)
                    throw new Exception("Selected mail receipt could not reconcile while protected mail remains");
                result = await stage.ExecuteAsync(f.Context, (_, _, _) => throw new Exception("Unexpected relay"));
                failure = null;
            }
            bool fails = mode is "missing" or "mismatch" or "page-stuck" or "rejected" or "identity";
            if (fails != (failure != null)) throw new Exception("Mailbox scenario " + mode, failure);
            if (mode is "missing" or "mismatch" or "page-stuck" && claims != 0) throw new Exception("Unreadable mail was claimed");
            if (mode == "rejected" && claims != 10) throw new Exception("Rejected batch repeated");
            if (!fails)
            {
                if (mode == "off" ? normal.Count + cash.Count != 0 : normal.Count != 2 || cash.Count != 1 || N(result?["preserved_mail_count"]) != 3)
                    throw new Exception("Mailbox remaining attachments differ: " + mode);
                if (mode == "only-protected" && (claims != 0 || S(result?["reason"]) != "mailbox_blood_cocktail_preserved")) throw new Exception("Protected mail reported as empty");
                if (mode == "paged" && (pages != 1 || claims != 25)) throw new Exception("Later-page reward missed");
            }
            cases.Add("mail preservation handles " + mode + " without claiming cocktail attachments");
        }
    }
    public static async Task Run(string output, List<string> cases)
    {
        await RewardCleanupState(output, cases);
        await CostumeTicketNotice(output, cases);
        await MailRecovery(output, cases);
        await ProtectedMail(output, cases);
        await OpeningTalentNotice(output, cases);
        await TacticsSeasonNotice(output, cases);
        void Check(bool value, string name)
        {
            if (!value)
                throw new Exception(name);
            cases.Add(name);
        }
        async Task Reject(Func<Task> action, string kind, string label)
        {
            string found = "";
            try
            {
                await action();
            }
            catch (StageHostException e) { found = e.Kind; }
            Check(found == kind, label + " (" + found + ")");
        }
        foreach (string mode in new[] { "restarted", "expired", "in-flight", "wrong-owner" })
        {
            using var f = new WorkflowHarness(Path.Combine(output, "room-rerun-" + mode), [], ["room.info"], (frame, ctx) => {
                string player = DailyIdentity.PlayerKey(frame["AccountKey"]!.GetValue<string>(), 42);
                frame["PlayerKey"] = player; ctx["actor"]![4] = player;
                frame["Scene"] = "Empty";
            }, observeIdentity: true);
            f.Page("MenuUI", "_buttonMyRoom");
            f.Readings = () => [WorkflowCases.Reading("room.cache", ("OwnerIndex", mode == "wrong-owner" ? 43 : 42))];
            string id = Guid.NewGuid().ToString("N"), oldPath = Path.Combine(f.Root, "live", "business", id + ".json");
            var old = new JsonObject {
                ["id"] = id, ["role"] = "room.info", ["stage"] = "room", ["state"] = "unknown", ["at"] = f.Ticks - (mode == "expired" ? 60 : 1) * TimeSpan.TicksPerSecond,
                ["account"] = f.Context["actor"]![3]!.DeepClone(), ["player"] = f.Context["actor"]![4]!.DeepClone(),
                ["server"] = f.Context["server"]!.DeepClone(), ["cycle"] = f.Context["cycle"]!.DeepClone(), ["before"] = f.Evidence(), ["events"] = new JsonArray()
            };
            if (mode is "restarted" or "wrong-owner") old["before"]!["Frame"]!["ProcessStartTicks"] = 1;
            DailyJson.Write(oldPath, old);
            var report = await new DailyManagedReconciliation(f.Root, f.Driver, ["room.info"], () => false).RunAsync(f.Context);
            Check(report["unresolved"]!.AsArray().Count == 1 && report["unresolved"]![0]!["recover_in_stage"]!.GetValue<bool>(), "room query uncertainty defers to fresh stage evidence: " + mode);
            f.OnCommand = _ => {
                if (DailyNavigationDecision.Types(f.Frame).Contains("MenuUI")) {
                    f.Event("room.info", "request"); f.Page("MyRoomUI", "_objBackButton");
                    f.Event("room.info", "response", ("RoomInfo.OwnerIndex", 42));
                } else f.Page("MenuUI", "_buttonMyRoom");
            };
            var room = new DailyRoomStage(f.Root, f.Driver, () => false, () => f.Time, t => { f.Time += t.TotalSeconds; return Task.CompletedTask; });
            string outcome = "";
            try { outcome = (await room.ExecuteAsync(f.Context, (_, _, _) => throw new Exception("Unexpected relay")))["state"]!.GetValue<string>(); }
            catch (StageHostException e) { outcome = e.Kind; }
            Check(mode == "in-flight" ? outcome == "pending" && f.Box.Commands.Count == 0 : mode == "wrong-owner" ? outcome == "pending" && f.Box.Commands.Count == 1 : outcome == "completed" && f.Box.Commands.Count == 2,
                "room restart recovery keeps independent native owner proof: " + mode + " -> " + outcome);
            var retained = DailyJson.TryRead<JsonObject>(oldPath)!;
            Check(retained["state"]!.GetValue<string>() == (mode == "in-flight" ? "unknown" : "superseded") &&
                (mode == "in-flight" || retained["reconciliation"]!["original_outcome"]!.GetValue<string>() == "unknown"), "old room request never becomes fabricated success: " + mode);
        }
        JsonObject Page(string name, string field = "_objBackButton")
        {
            var f = CommandDriverCases.Frame();
            f["BridgeVersion"] = 104;
            f["Surfaces"]![0]!["Type"] = name;
            f["Surfaces"]![0]!["InputReady"] = true;
            f["Surfaces"]![0]!["Targets"]![0]!["Field"] = field;
            return f;
        }
        var policy = DailyNavigationPolicy.Load();
        JsonObject context = CommandDriverCases.Context(), current = Page("MailUI");
        double time = 0;
        string path = Path.Combine(output, "managed-inputs");
        var box = new CommandDriverCases.Mailbox();
        Func<Task<DailyStageFrame>> read = () => Task.FromResult(new DailyStageFrame(current.DeepClone().AsObject(), context.DeepClone().AsObject()));
        using (var driver = new DailyCommandDriver(path, box, read, () => false, () => 100000000, () => time, t => { time += t.TotalSeconds; return Task.CompletedTask; }))
        {
            driver.Bind(context);
            driver.Acquire("live");
            box.AfterCommand = _ => current = Page("MenuUI", "_buttonMail");
            var close = DailyNavigationDecision.CloseAction(current, policy)!;
            var result = await driver.NavigationAsync(close);
            Check(result["state"]!.GetValue<string>() == "observed_expected_ui" && box.Commands.Count == 1, "managed navigation closes a known page and observes its absence");
            await Reject(() => driver.NavigationAsync(close), "navigation_changed", "stale close action is refused without another input");
            Check(box.Commands.Count == 1, "changed navigation sends zero replacement commands");
            current = Page("MailUI");
            box.State = "rejected";
            box.Dispatched = false;
            await Reject(() => driver.NavigationAsync(DailyNavigationDecision.CloseAction(current, policy)!), "navigation_changed", "managed non-dispatch navigation rejection permits bounded outer replan");
            box.State = "unknown";
            box.Dispatched = true;
            await Reject(() => driver.NavigationAsync(DailyNavigationDecision.CloseAction(current, policy)!), "pending", "managed uncertain navigation cannot be replanned");
            box.State = "observed_after_dispatch";
            box.Dispatched = true;
            await Reject(() => driver.NavigationAsync(new() { ["ui"] = "MenuUI", ["field"] = "_buttonMail", ["allow_guild"] = true }), "protocol", "navigation SDK rejects extra spending authorization");
            int before = box.Commands.Count;
            current = Page("MailUI");
            box.AfterCommand = null;
            await Reject(() => driver.NavigationAsync(DailyNavigationDecision.CloseAction(current, policy)!), "pending", "missing expected UI retains the original dispatched receipt");
            Check(box.Commands.Count == before + 1, "expected-UI timeout has no automatic repeat");
        }
        time = 0;
        current = Page("MessagePopupUI", "_buttonOK");
        current["Surfaces"]![0]!["NativeContext"] = "talent_inactive_ack_only";
        current["Surfaces"]![0]!["Popup"] = true;
        box = new();
        string talentPath = Path.Combine(output, "managed-talent-inputs");
        using (var driver = new DailyCommandDriver(talentPath, box, read, () => false, () => 100000000, () => time, t => { time += t.TotalSeconds; return Task.CompletedTask; }))
        {
            driver.Bind(context);
            driver.Acquire("live");
            box.AfterCommand = _ => current = Page("MenuUI", "_buttonMail");
            for (int i = 0; i < 4; i++)
            {
                current = Page("MessagePopupUI", "_buttonOK");
                current["Surfaces"]![0]!["NativeContext"] = "talent_inactive_ack_only";
                current["Surfaces"]![0]!["Popup"] = true;
                await driver.NavigationAsync(DailyNavigationDecision.TalentAction(current, policy)!);
            }
            Check(box.Commands.Count == 4 && Directory.GetFiles(Path.Combine(talentPath, "live", "popup-recovery"), "*.json").All(p => DailyJson.TryRead<JsonObject>(p)!["business_replayed"]!.GetValue<bool>() == false), "managed talent acknowledgements retain no-business-replay diagnostics");
            current = Page("MessagePopupUI", "_buttonOK");
            current["Surfaces"]![0]!["NativeContext"] = "talent_inactive_ack_only";
            current["Surfaces"]![0]!["Popup"] = true;
            await Reject(() => driver.NavigationAsync(DailyNavigationDecision.TalentAction(current, policy)!), "adapter", "managed talent recovery retains original four-per-minute bound");
            Check(box.Commands.Count == 4, "repeated talent popup does not send a fifth acknowledgement");
        }
        foreach (string scenario in new[] { "resume-in-delay", "resume-in-read", "still-missing", "changed-account", "stopped", "unknown-receipt" })
        {
            double elapsed = 0;
            bool submitted = false, pause = false;
            var frame = Page("MenuUI", "_buttonMail");
            var actor = CommandDriverCases.Context();
            var delayedBox = new CommandDriverCases.Mailbox { Error = "" };
            if (scenario == "unknown-receipt") delayedBox.State = "unknown";
            delayedBox.AfterCommand = _ => submitted = true;
            using var delayed = new DailyCommandDriver(Path.Combine(output, "observed-resume-" + scenario), delayedBox,
                () =>
                {
                    if (submitted && scenario == "resume-in-read") { elapsed = 120; frame = Page("MailUI"); }
                    frame["AtUtcTicks"] = 100000000 + (long)(elapsed * TimeSpan.TicksPerSecond);
                    return Task.FromResult(new DailyStageFrame(frame.DeepClone().AsObject(), actor.DeepClone().AsObject()));
                }, () => pause, () => 100000000 + (long)(elapsed * TimeSpan.TicksPerSecond), () => elapsed, t =>
                {
                    elapsed += submitted ? 120 : t.TotalSeconds;
                    if (submitted && scenario == "resume-in-delay") frame = Page("MailUI");
                    if (scenario == "changed-account") actor["actor"]![3] = new string('d', 64);
                    if (scenario == "stopped") pause = true;
                    return Task.CompletedTask;
                });
            delayed.Bind(CommandDriverCases.Context());
            JsonObject? result = null;
            string failure = "";
            try { result = await delayed.SendObservedAsync(CommandDriverCases.Action()); }
            catch (DailyStepException e) { failure = e.Kind; }
            Check(scenario.StartsWith("resume-", StringComparison.Ordinal)
                ? result?["state"]?.GetValue<string>() == "observed_expected_ui"
                : failure == "pending", "UI input reobserves after delay without masking " + scenario);
            Check(delayedBox.Commands.Count == 1, "UI input " + scenario + " never replays dispatched command");
        }
        current = Page("MenuUI", "_buttonGuild");
        time = 0;
        box = new();
        string guildPath = Path.Combine(output, "managed-guild-inputs");
        string guildKey = new('c', 64);
        DailySnapshot daily = new()
        {
            Runtime = DailyIdentity.RuntimeName,
            State = "identified",
            ProcessId = 7,
            ProcessStartTicks = 10,
            AccountKey = new('a', 64),
            PlayerKey = new('b', 64),
            FrameUtcTicks = 100000000,
            Guild = new()
            {
                GuildKey = guildKey,
                ServerKey = "test",
                CycleKey = "today",
                Supported = true,
                InGuild = true,
                MenuVisible = true,
                ServerTicks = 100000000,
                ResetTicks = 100000000 + 100 * TimeSpan.TicksPerSecond
            }
        };
        Func<Task<DailyStageFrame>> guildRead = () => Task.FromResult(new DailyStageFrame(current.DeepClone().AsObject(), context.DeepClone().AsObject(), daily));
        using (var driver = new DailyCommandDriver(guildPath, box, guildRead, () => false, () => 100000000, () => time, t => { time += t.TotalSeconds; return Task.CompletedTask; }))
        {
            driver.Bind(context);
            driver.Acquire("live");
            box.AfterCommand = _ => current = Page("GuildUI");
            var entry = DailyGuildStage.LoadRecipe()["rules"]!.AsArray().Single(r => r!["id"]!.GetValue<string>() == "enter-guild-once")!["action"]!.DeepClone().AsObject();
            var done = await driver.GuildAsync(entry, guildKey);
            Check(box.Commands.Count == 1 && box.Commands[0]["Reason"]!.GetValue<string>().StartsWith("guild-once|today|"), "managed guild input preserves once-per-cycle native reason");
            current = Page("MenuUI", "_buttonGuild");
            await Reject(() => driver.GuildAsync(entry, guildKey), "guild_rejected", "existing dispatched guild receipt prevents a second entry");
            Check(box.Commands.Count == 1, "guild duplicate guard reaches no native input");
            await Reject(() => driver.GuildAsync(entry, new string('d', 64)), "identity", "guild identity change prevents managed input");
        }
        foreach (bool restarted in new[] { false, true })
        {
            long baseline = DateTime.UtcNow.Ticks; time = 0;
            long Now() => baseline + (long)(time * TimeSpan.TicksPerSecond);
            current = Page("MenuUI", "_buttonGuild");
            daily.Guild.MenuVisible = true; daily.Guild.GuildVisible = false;
            daily.Guild.ServerKey = new string('e', 64); context["server"] = daily.Guild.ServerKey;
            daily.Guild.ServerTicks = baseline; daily.Guild.ResetTicks = baseline + TimeSpan.FromHours(2).Ticks;
            string restartRoot = Path.Combine(output, "guild-full-refresh-" + restarted);
            var store = new GuildStore(restartRoot);
            var original = GuildStore.Intent(Guid.NewGuid().ToString("N"), daily);
            original.StartedUtcTicks = baseline - TimeSpan.FromSeconds(60).Ticks;
            original.State = restarted ? "waiting_response" : "unknown";
            original.RequestCount = 1; original.MayHaveDispatched = true;
            if (restarted) { original.ProcessStartTicks--; original.ReentryCount = 2; }
            store.Save(original);
            string managedId = Guid.NewGuid().ToString("N"), managedPath = Path.Combine(restartRoot, "live", "managed-guild", managedId + ".json");
            var previousContext = context.DeepClone().AsObject(); if (restarted) previousContext["actor"]![1] = original.ProcessStartTicks;
            DailyJson.Write(managedPath, new JsonObject { ["schema"]=1, ["id"]=managedId, ["context"]=previousContext, ["guild_key"]=guildKey, ["entry_status"]="unknown", ["started"]=original.StartedUtcTicks - TimeSpan.TicksPerSecond });
            box = new() { Error = "" };
            box.Values["live:receipts~old.json"] = JsonSerializer.SerializeToUtf8Bytes(new JsonObject {
                ["MayHaveDispatched"]=true, ["State"]="unknown", ["Command"]=new JsonObject {
                    ["AccountKey"]=daily.AccountKey, ["PlayerKey"]=daily.PlayerKey, ["Reason"]="guild-once|today|old",
                    ["ObservedUtcTicks"]=original.StartedUtcTicks - TimeSpan.TicksPerSecond, ["ExpiresUtcTicks"]=original.StartedUtcTicks + 10 * TimeSpan.TicksPerSecond
                }
            });
            Task<DailyStageFrame> ReadGuild() { current["AtUtcTicks"]=Now(); current["ProcessStartTicks"]=daily.ProcessStartTicks; daily.FrameUtcTicks=Now(); return Task.FromResult(new DailyStageFrame(current.DeepClone().AsObject(), context.DeepClone().AsObject(), daily)); }
            Task Delay(TimeSpan t) { time += t.TotalSeconds; return Task.CompletedTask; }
            using var driver = new DailyCommandDriver(restartRoot, box, ReadGuild, () => false, Now, () => time, Delay);
            driver.Bind(context);
            box.AfterCommand = command => {
                if (DailyNavigationDecision.Types(current).Contains("MenuUI")) {
                    var fresh = GuildStore.Intent(Guid.NewGuid().ToString("N"), daily);
                    fresh.StartedUtcTicks=Now(); fresh.State="checked_no_grant"; fresh.RequestCount=1;
                    fresh.ResponseSeen=fresh.CallbackMatched=fresh.MemberMatched=fresh.CacheMatched=fresh.MayHaveDispatched=true;
                    store.Save(fresh); current=Page("GuildUI"); daily.Guild.MenuVisible=false; daily.Guild.GuildVisible=true;
                } else { current=Page("MenuUI", "_buttonGuild"); daily.Guild.MenuVisible=true; daily.Guild.GuildVisible=false; }
            };
            var stage = new DailyGuildStage(restartRoot, ReadGuild, () => false, now:Now, clock:() => time, delay:Delay);
            var complete = await stage.ExecuteAsync(context, (_, _, arguments) => driver.GuildAsync(arguments!["action"]!.AsObject(), arguments["guild_key"]!.GetValue<string>()));
            Check(complete["state"]!.GetValue<string>() == "completed" && box.Commands.Count==2, "full guild refresh traverses workflow, managed input and original-receipt guard: restart=" + restarted);
            Check(store.Receipt(original.Id)!.State == original.State && DailyJson.TryRead<JsonObject>(managedPath)!["entry_status"]!.GetValue<string>()=="superseded", "guild old response is preserved separately from new completion: restart=" + restarted);
        }
        // Reproduce entry -> successful native response -> attendance UI loading.
        // Both the current and legacy observer shape must finish without a second entry.
        foreach (string mode in new[] { "loading", "legacy-loading", "loading-timeout", "identity-during-loading", "resume" })
        {
            long baseline = DateTime.UtcNow.Ticks; time = 0;
            long Now() => baseline + (long)(time * TimeSpan.TicksPerSecond);
            daily.State = "identified"; daily.Loading = false; daily.Guild.Supported = daily.Guild.InGuild = true;
            daily.Guild.AttendancePopup = false; daily.Guild.MenuVisible = true; daily.Guild.GuildVisible = false;
            daily.Guild.ServerTicks = baseline; daily.Guild.ResetTicks = baseline + TimeSpan.FromHours(2).Ticks;
            string testRoot = Path.Combine(output, "guild-attendance-" + mode);
            var store = new GuildStore(testRoot);
            GuildReceipt? granted = null;
            int entries = 0, closes = 0, backs = 0;
            double enteredAt = -1;
            current = Page("MenuUI", "_buttonGuild");
            void Grant()
            {
                granted = GuildStore.Intent(Guid.NewGuid().ToString("N"), daily);
                granted.StartedUtcTicks = Now(); granted.State = "completed"; granted.RequestCount = 1;
                granted.AttendanceGranted = granted.ResponseSeen = granted.CallbackMatched = granted.MemberMatched = granted.CacheMatched = granted.MayHaveDispatched = true;
                store.Save(granted);
            }
            void Popup()
            {
                current = Page("GuildUI");
                current["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("GuildJoinAttendancePopupUI", true, "_buttonBackground", 99));
                current["UiToken"] = "attendance";
                daily.Guild.AttendancePopup = daily.Guild.GuildVisible = true; daily.Guild.MenuVisible = false;
            }
            if (mode == "resume")
            {
                Grant(); Popup();
                string oldId = Guid.NewGuid().ToString("N");
                DailyJson.Write(Path.Combine(testRoot, "live", "managed-guild", oldId + ".json"), new JsonObject {
                    ["schema"] = 1, ["id"] = oldId, ["context"] = context.DeepClone(), ["guild_key"] = guildKey,
                    ["entry_status"] = "unknown", ["started"] = Now() - TimeSpan.TicksPerSecond });
            }
            Task<DailyStageFrame> ReadAttendance()
            {
                if (enteredAt >= 0 && closes == 0)
                {
                    daily.Loading = mode == "loading-timeout" || time - enteredAt < 1;
                    daily.Guild.Supported = !(daily.Loading && mode == "legacy-loading");
                    if (!daily.Loading) Popup();
                }
                daily.FrameUtcTicks = Now(); current["AtUtcTicks"] = Now(); current["ProcessStartTicks"] = daily.ProcessStartTicks;
                var observedContext = context.DeepClone().AsObject();
                if (mode == "identity-during-loading" && daily.Loading) observedContext["server"] = "changed";
                return Task.FromResult(new DailyStageFrame(current.DeepClone().AsObject(), observedContext, daily));
            }
            Task DelayAttendance(TimeSpan t) { time += t.TotalSeconds; return Task.CompletedTask; }
            var stage = new DailyGuildStage(testRoot, ReadAttendance, () => false, now: Now, clock: () => time, delay: DelayAttendance);
            string fault = ""; JsonObject? finished = null;
            try
            {
                finished = await stage.ExecuteAsync(context, (_, _, args) => {
                    var action = args!["action"]!.AsObject();
                    if (daily.Loading) throw new Exception("Guild input during loading");
                    if (action["allow_guild"]?.GetValue<bool>() == true) { entries++; Grant(); enteredAt = time; current = Page("GuildUI"); }
                    else if (action["ui"]!.GetValue<string>() == "GuildJoinAttendancePopupUI") { closes++; daily.Guild.AttendancePopup = false; current = Page("GuildUI"); }
                    else { backs++; daily.Guild.GuildVisible = false; daily.Guild.MenuVisible = true; current = Page("MenuUI", "_buttonGuild"); }
                    return Task.FromResult(new JsonObject { ["id"] = Guid.NewGuid().ToString("N") });
                });
            }
            catch (StageHostException e) { fault = e.Kind; }
            bool succeeds = mode is "loading" or "legacy-loading" or "resume";
            Check(succeeds ? finished?["state"]?.GetValue<string>() == "completed" && closes == 1 && backs == 1
                : fault == (mode == "identity-during-loading" ? "identity" : "pending") && closes == 0 && backs == 0,
                "guild attendance transition handles " + mode);
            Check(entries == (mode == "resume" ? 0 : 1) && granted != null && GuildPolicy.Outcome(granted) == "completed",
                "guild attendance " + mode + " preserves granted reward and never resubmits entry");
        }
    }
}
