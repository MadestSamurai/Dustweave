using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;

static class MirrorStageCases
{
    private sealed class Scenario : IDisposable
    {
        public readonly WorkflowHarness F;
        public int Free = 40, Paid = 7, Multiplier = 40, Slider = 40, Repeats = 5;
        public bool FreeOnly, Skip, Hold, StopAfterMatch;
        public readonly List<int> Batches = [];
        public readonly List<int> BatchRepeats = [];
        public readonly List<JsonObject> Progress = [];
        public int Settled;
        public double SecondsPerBattle = 2;
        public string Period = "ScheduleSeason";
        public bool PeriodReady = true;
        public bool CloseAfterBatch, CloseInSettings;
        private int pending;
        private double settleAt;
        private const string Auto = "BattleAutoSettingPopupUI", Boost = "BattleAutoCurrencyAccelSettingPopupUI";
        public Scenario(string root)
        {
            F = new(root, [DailyMirror.Proof()]);
            F.Frame["Scene"] = "Map3001_001";
            F.Workflow.Settings.Mirror.Multiplier = 15;
            F.Workflow.WorkProgress = p => Progress.Add(p.DeepClone().AsObject());
            F.Readings = Readings;
            F.OnCommand = Command;
            F.OnDelay = () => { if (pending > 0 && !Hold && F.Time >= settleAt) Settle(); };
            Lobby();
        }
        private JsonObject[] Readings() =>
        [
            WorkflowCases.Reading("mirror.period", ("$self", O(("Ready", PeriodReady), ("State", Period)))),
            WorkflowCases.Reading("mirror.presentation.PVPClassUpUI", ("ὣὤὥὦὯὦὩὤὨὠὪ", true), ("ὪὯὣὥὬὫὬὩὠὮὠ", true), ("ὮὬὧὦὣὠὠὤὮὦὧ", true), ("ὫὥὧὬὭὫὡὠὠὪὠ", false), ("ὦὡὮὫὧὠὮὡὭὭὬ", false)),
            WorkflowCases.Reading("mirror.currency", ("PvpTicket", Free), ("PvpTicketStack", Paid)),
            WorkflowCases.Reading("mirror.auto", ("ὣὤὥὪὥὡὥὢὧὮὠ", Multiplier), ("ὯὠὭὣὮὠὯὬὤὯὠ", Repeats), ("_buttonFreeOnly.IsEnable", FreeOnly), ("_buttonSkip.IsEnable", Skip), ("_sliderAutoCount._objPlusMaxButton.name", "Button - +100")),
            WorkflowCases.Reading("mirror.boost", ("_sliderCount.ὮὢὥὯὥὧὤὭὨὨὪ", Slider), ("_sliderCount._objPlus10Button.name", "+10"), ("_sliderCount._objMinus10Button.name", "-10"), ("_sliderCount._objPlusButton.name", "+1"), ("_sliderCount._objMinusButton.name", "-1")),
            WorkflowCases.Reading("mirror.input", ("ὨὬὬὯὫὨὦὧὨὩὭ", true), ("ὤὬὮὠὨὮὣὯὩὩὡ", true)),
            WorkflowCases.Reading("mirror.ready", ("ὣὤὥὦὯὦὩὤὨὠὪ", true), ("ὪὯὣὥὬὫὬὩὠὮὠ", true))
        ];
        private void Lobby() => F.Page("BattleUI_PVP", "Panel/Button - AutoSetting");
        private void AutoPage() => F.Page(Auto, "_btnSettingBoost", "Panel/FreeOnly/Button - Toggle", "Panel/BattleSkip/Button - Toggle", "Panel/Button - -100", "Panel/Button - +100", "_currencyButtonOK");
        public void Settle()
        {
            if (pending == 0) throw new Exception("Unexpected second settlement");
            Free -= Multiplier;
            Settled++;
            F.Event("mirror.end", "response", ("win", true));
            pending--;
            if (pending == 0 && CloseAfterBatch) Period = "ScheduleOffSeason";
            if (pending > 0) Match();
            Lobby();
            F.Time += .01;
        }
        private void Match()
        {
            F.Event("mirror.matching", "request"); F.Event("mirror.matching", "response"); F.Event("mirror.end", "request");
            settleAt = F.Time + SecondsPerBattle;
        }
        private void Command(JsonObject command)
        {
            var page = F.Frame["Surfaces"]![0]!.AsObject();
            string ui = S(page["Type"]), kind = S(command["Kind"]);
            string field = Rows(page["Targets"]).Where(t => N(t["Id"]) == N(command["TargetId"])).Select(t => S(t["Field"])).SingleOrDefault() ?? "";
            F.Time += .01;
            if (ui == "PVPClassUpUI" && field == "_objCancelButton") { Lobby(); return; }
            if (kind == "back" && ui == "BattleUI_PVP") { F.Page("GameFieldDefaultUI", "_buttonMenu"); return; }
            if (ui == "GameFieldDefaultUI" && field == "_buttonMenu") { F.Page("MenuUI"); return; }
            if (ui == "BattleUI_PVP" && field.EndsWith("/Button - AutoSetting", StringComparison.Ordinal)) { AutoPage(); if (CloseInSettings) Period = "ScheduleOffSeason"; return; }
            if (ui == Auto)
            {
                if (kind == "back") { Lobby(); return; }
                if (field == "_btnSettingBoost") { Slider = Multiplier; F.Page(Boost, "Panel/+10", "Panel/-10", "Panel/+1", "Panel/-1", "_btnOk"); return; }
                if (field.Contains("FreeOnly", StringComparison.Ordinal)) { FreeOnly = !FreeOnly; return; }
                if (field.Contains("BattleSkip", StringComparison.Ordinal)) { Skip = !Skip; return; }
                if (field.EndsWith("-100", StringComparison.Ordinal)) { Repeats = 1; return; }
                if (field.EndsWith("+100", StringComparison.Ordinal)) { Repeats = (Free + (FreeOnly ? 0 : Paid)) / Multiplier; return; }
                if (field == "_currencyButtonOK")
                {
                    if (!FreeOnly || !Skip || Repeats < 1 || Multiplier * Repeats > Free || pending > 0) throw new Exception("Unsafe Mirror configuration");
                    Batches.Add(Multiplier); BatchRepeats.Add(Repeats); pending = Repeats;
                    Match();
                    Lobby();
                    if (StopAfterMatch) F.Stopped = true;
                    return;
                }
            }
            if (ui == Boost)
            {
                if (field == "_btnOk") { Multiplier = Slider; AutoPage(); return; }
                Slider = Math.Clamp(Slider + (field.Split('/').Last() switch { "+10" => 10, "-10" => -10, "+1" => 1, "-1" => -1, _ => throw new Exception("Unexpected slider command") }), 1, 40);
                return;
            }
            throw new Exception("Unexpected Mirror command: " + ui + "/" + kind + "/" + field);
        }
        public void Dispose() => F.Dispose();
    }
    public static async Task Run(string root, List<string> cases)
    {
        await MirrorArrivalCases.Run(Path.Combine(root, "mirror-arrival"), cases);
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        void Reject(Action action, string name) { try { action(); } catch (InvalidDataException) { cases.Add(name); return; } throw new Exception(name); }
        var command = new BD2Daily.Live.Command { Kind = "native_click", SurfaceId = 1, TargetId = 2 };
        var native = new BD2Daily.Live.Frame { Scene = "Map3001_001", Surfaces = [new() { Id = 1, Type = "BattleAutoSettingPopupUI", Targets = [new() { Id = 2, Field = "_currencyButtonOK" }] }] };
        Check(BD2Daily.Live.LiveProtocol.RequiresMirrorPeriod(command, native), "Mirror auto confirmation is checked before native dispatch");
        native.Scene = "Map0009_001";
        Check(!BD2Daily.Live.LiveProtocol.RequiresMirrorPeriod(command, native), "Mirror season guard does not intercept hunting's shared auto popup");
        native.Scene = "Map3001_001"; command.Kind = "back";
        Check(!BD2Daily.Live.LiveProtocol.RequiresMirrorPeriod(command, native), "closed Mirror settings remain dismissible");
        foreach (string period in new[] { "ScheduleOffSeason", "ScheduleCalculate", "ScheduleComingSoon", "ScheduleTraining" })
        using (var s = new Scenario(Path.Combine(root, "mirror-closed-" + period)) { Period = period })
        {
            var result = await DailyMirror.Run(s.F.Workflow);
            Check(S(result["state"]) == "skipped" && S(result["reason"]) == "outside_mirror_play_period" && s.Batches.Count == 0 && s.Free == 40 && s.F.Time < 5,
                "Mirror unavailable period skips quickly without spending tickets: " + period);
        }
        foreach (bool closed in new[] { false, true })
        using (var s = new Scenario(Path.Combine(root, "mirror-historical-" + closed)))
        {
            if (closed) s.Period = "ScheduleOffSeason";
            var old = s.F.Business.Create(s.F.Context, "mirror.end", s.F.Evidence(), O(("multiplier", 1), ("free_only", true)), O(("ui", "BattleAutoSettingPopupUI")));
            old["state"] = "unknown"; old["cycle"] = "older-day";
            old["before"]!["Frame"]!["ProcessId"] = N(s.F.Frame["ProcessId"]) + 10;
            old["before"]!["Frame"]!["ProcessStartTicks"] = N(s.F.Frame["ProcessStartTicks"]) - 1;
            old["events"] = new JsonArray(); old["error"] = "stopped in old game process";
            s.F.Business.Save(old);
            var stable = new DailyStageFrame(s.F.Frame.DeepClone().AsObject(), s.F.Context.DeepClone().AsObject());
            Check(DailyMirror.CanRetireHistorical(old, stable), "old Mirror process and cycle can be retired in a verified lobby");
            foreach (string guard in new[] { "account", "loading", "active", "result", "not-ready", "unknown-popup" })
            {
                var candidate = old.DeepClone().AsObject();
                var frame = stable.Frame.DeepClone().AsObject();
                switch (guard) {
                    case "same-cycle": candidate["cycle"] = s.F.Context["cycle"]!.DeepClone(); break;
                    case "same-process": candidate["before"]!["Frame"]!["ProcessId"] = s.F.Frame["ProcessId"]!.DeepClone(); candidate["before"]!["Frame"]!["ProcessStartTicks"] = s.F.Frame["ProcessStartTicks"]!.DeepClone(); candidate["before"]!["Frame"]!["Instance"] = "old-component"; break;
                    case "account": frame["AccountKey"] = "other"; break;
                    case "loading": frame["Scene"] = ""; break;
                    case "active": frame["Surfaces"]![0]!["Targets"] = new JsonArray(); break;
                    case "not-ready": frame["Surfaces"]![0]!["InputReady"] = false; break;
                    case "result": frame["Surfaces"]!.AsArray().Add(O(("Type", "PVPAutoHistoryPopupUI"))); break;
                    case "unknown-popup": frame["Surfaces"]!.AsArray().Add(O(("Type", "MessagePopupUI"), ("Popup", true), ("Order", 99))); break;
                }
                Check(!DailyMirror.CanRetireHistorical(candidate, new(frame, stable.Context)), "historical Mirror recovery refuses " + guard);
            }
            old["cycle"] = Copy(s.F.Context["cycle"]);
            old["scope"]!["repetitions"] = 40;
            s.F.Business.Save(old);
            Check(DailyMirror.CanRetireHistorical(old, stable), "same-day restarted Mirror replans from its remaining tickets");
            var sameProcess = old.DeepClone().AsObject(); sameProcess["before"]!["Frame"] = stable.Frame.DeepClone();
            Check(!DailyMirror.CanRetireHistorical(sameProcess, stable), "same-process auto battle lobby alone is not proof the native batch ended");
            s.F.Page("GameFieldDefaultUI");
            Check(DailyMirror.CanRetireHistorical(sameProcess, new(s.F.Frame, s.F.Context)), "leaving the battle permits same-process current-state recovery");
            s.F.Page("BattleUI_PVP", "Panel/Button - AutoSetting");
            var result = await DailyMirror.Run(s.F.Workflow);
            var retired = s.F.Business.Records(s.F.Context).Single(r => S(r["id"]) == S(old["id"]));
            Check(S(retired["state"]) == "superseded" && retired["result"] == null && S(retired["error"]) == S(old["error"]) &&
                retired["reconciliation"]?["battle_completed"]?.GetValue<bool>() == false && JsonNode.DeepEquals(retired["events"], old["events"]),
                "old Mirror evidence remains historical and never becomes a fabricated win");
            Check(closed ? S(result["state"]) == "skipped" && s.Batches.Count == 0 && s.Free == 40
                : s.Free == 0 && s.Paid == 7 && s.Batches.SequenceEqual(new[] {15,10}),
                "historical Mirror journal permits fresh free-ticket planning or closed-period skip: " + closed);
        }
        using (var s = new Scenario(Path.Combine(root,"mirror-interrupted-18-of-40")) { Free=22 })
        {
            s.F.Workflow.Settings.Mirror.Multiplier=1;
            var old=s.F.Business.Create(s.F.Context,"mirror.end",s.F.Evidence(),O(("multiplier",1),("repetitions",40),("free_only",true)),new());
            old["state"]="unknown";old["before"]!["Frame"]!["ProcessId"]=900;
            var receipts=new JsonArray();
            for(int i=0;i<18;i++)foreach(var pair in new[]{("mirror.matching","request"),("mirror.matching","response"),("mirror.end","request"),("mirror.end","response")})
            {
                var e=WorkflowCases.Event(pair.Item1,pair.Item2,receipts.Count+1);e["Frame"]=old["before"]!["Frame"]!.DeepClone();e["AtUtcTicks"]=s.F.Ticks;receipts.Add(e);
            }
            var last=WorkflowCases.Event("mirror.matching","request",73);last["Frame"]=old["before"]!["Frame"]!.DeepClone();last["AtUtcTicks"]=s.F.Ticks;receipts.Add(last);
            old["events"]=receipts;s.F.Business.Save(old);
            await DailyMirror.Run(s.F.Workflow);
            var retained=s.F.Business.Records(s.F.Context).Single(o=>S(o["id"])==S(old["id"]));
            Check(s.Batches.SequenceEqual([1])&&s.BatchRepeats.SequenceEqual([22])&&s.Free==0&&s.Paid==7,
                "18 of 40 interrupted battles resume exactly the current 22 free attempts in one batch after same-day restart");
            Check(retained["events"]!.AsArray().Count==73&&retained["result"]==null&&S(retained["state"])=="superseded",
                "partial Mirror receipts remain diagnostic without claiming all forty battles completed");
        }
        using (var s = new Scenario(Path.Combine(root, "mirror-period-not-loaded")) { PeriodReady = false })
        {
            bool failed = false;
            try { await DailyMirror.Run(s.F.Workflow); } catch (StageHostException e) { failed = e.Message == "mirror_period_unavailable"; }
            Check(failed && s.Batches.Count == 0, "missing Mirror schedule is not fabricated into rest or battle availability");
        }
        using (var s = new Scenario(Path.Combine(root, "mirror-period-boundary")) { CloseInSettings = true })
        {
            var result = await DailyMirror.Run(s.F.Workflow);
            Check(S(result["state"]) == "skipped" && s.Batches.Count == 0 && s.Free == 40,
                "Mirror period closing during auto configuration prevents a new battle and exits settings");
        }
        using (var s = new Scenario(Path.Combine(root, "mirror-period-after-batch")) { CloseAfterBatch = true })
        {
            var result = await DailyMirror.Run(s.F.Workflow);
            Check(S(result["state"]) == "partial" && s.Settled == 2 && s.Free == 10 && N(result["free_spent"]) == 30 && s.Batches.Count == 1,
                "Mirror settles a submitted batch before deferring its remainder after period close");
        }
        using (var s = new Scenario(Path.Combine(root, "mirror-full")))
        {
            var result = await DailyMirror.Run(s.F.Workflow);
            Check(s.Batches.SequenceEqual(new[] {15,10}) && s.BatchRepeats.SequenceEqual(new[] {2,1}) && s.Free == 0 && s.Paid == 7 && N(result["free_spent"]) == 40, "Mirror drains 40 free tickets with native 15x2 then 10x1 without paid tickets");
            var progress = s.Progress.Where(p => S(p["unit"]) == "tickets").Select(p => I(p["completed"])).ToArray();
            Check(progress.Contains(15) && progress.Contains(30) && progress[^1] == 40
                && progress.Zip(progress.Skip(1)).All(p => p.First <= p.Second),
                "native batch progress advances after each reply across batches without another game query or a backwards jump");
            Check(s.F.Time >= 6 && s.F.Business.Records(s.F.Context).Count(r => S(r["state"]) == "completed") == 2 && s.Settled == 3, "Mirror waits for all three native results across exactly two durable batches");
            Check(DailyNavigationDecision.Types(s.F.Frame).SetEquals(["MenuUI"]), "Mirror returns after the final confirmed settlement");
            var ops = s.F.Business.Records(s.F.Context).OrderBy(r => N(r["at"])).ToArray();
            var batch = ops[0];
            var before = batch["before"]!.AsObject(); var after = batch["after"]!.AsObject(); var events = batch["events"]!.AsArray();
            Reject(() => DailyMirror.Verify(before, Array(events.Take(4)), after, 15, 2), "Mirror does not accept a single result as proof of two battles even with the final balance");
            Reject(() => DailyMirror.Verify(before, events, after, 15, 1), "Mirror rejects changed native repetitions");
            var rejected = events.DeepClone().AsArray(); rejected[^1]!["Accepted"] = false;
            Reject(() => DailyMirror.Verify(before, rejected, after, 15, 2), "Mirror rejects a failed response within a continuous batch");
            var legacy = ops[1].DeepClone().AsObject(); legacy["scope"]!.AsObject().Remove("repetitions");
            Check(N(DailyMirror.Proof().Verify(legacy, legacy["events"]!.AsArray(), legacy["after"]!.AsObject())["cost"]) == 10,
                "Mirror legacy single-battle journals retain their original settlement semantics");
            await DailyMirror.Run(s.F.Workflow);
            Check(s.Batches.Count == 2, "Mirror rerun uses current zero free balance without another match");
        }
        using (var s = new Scenario(Path.Combine(root, "mirror-four-x-ten")))
        {
            var store = new DailyPreferenceStore(s.F.Root);
            string account = new('a', 64);
            var preferences = store.Read(account); preferences.Mirror.Multiplier = 4; store.Save(account, preferences);
            s.F.Workflow.Settings.Mirror.Multiplier = store.Read(account).Mirror.Multiplier;
            await DailyMirror.Run(s.F.Workflow);
            Check(s.Batches.SequenceEqual(new[] {4}) && s.BatchRepeats.SequenceEqual(new[] {10}) && s.Settled == 10 && s.Free == 0 && s.Paid == 7,
                "saved fourfold preference starts exactly one native 4x10 batch");
        }
        using (var s = new Scenario(Path.Combine(root, "mirror-one-x-forty")) { SecondsPerBattle = 4 })
        {
            s.F.Workflow.Settings.Mirror.Multiplier = 1;
            var delayed = s.F.OnDelay;
            s.F.OnDelay = () => { s.F.Time += 4; delayed?.Invoke(); };
            var result = await DailyMirror.Run(s.F.Workflow);
            Check(s.Batches.SequenceEqual(new[] {1}) && s.BatchRepeats.SequenceEqual(new[] {40}) && s.Settled == 40 && s.Free == 0 && s.Paid == 7,
                "Mirror 1x40 starts native continuous battle exactly once");
            Check(s.F.Time > 150 && s.F.Business.Records(s.F.Context).Single()["state"]!.GetValue<string>() == "completed" && N(result["free_spent"]) == 40,
                "Mirror multi-battle settlement survives beyond the old single-match timeout");
        }
        using (var s = new Scenario(Path.Combine(root, "mirror-nonstandard-free")) { Free = 43 })
        {
            s.F.Workflow.Settings.Mirror.Multiplier = 7;
            await DailyMirror.Run(s.F.Workflow);
            Check(s.Batches.SequenceEqual(new[] {7,1}) && s.BatchRepeats.SequenceEqual(new[] {6,1}) && s.Free == 0 && s.Paid == 7,
                "Mirror derives native count and remainder from live free tickets rather than assuming forty");
        }
        using (var s = new Scenario(Path.Combine(root, "mirror-rank-transition")))
        {
            s.F.Page("PVPClassUpUI", "_objCancelButton");
            var publish = s.F.Box.AfterWrite;
            bool injected = false;
            s.F.Box.AfterWrite = (channel, name, bytes) =>
            {
                publish?.Invoke(channel, name, bytes);
                if (name != "observation-request.json" || injected) return;
                injected = true;
                var e = JsonNode.Parse(s.F.Box.Values["live:evidence.json"])!.AsObject();
                e["Frame"]!["AccountKey"] = ""; e["Frame"]!["PlayerKey"] = "";
                e["Frame"]!["Error"] = "Waiting for fresh account identity";
                s.F.Box.Values["live:evidence.json"] = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(e);
            };
            await DailyMirror.Run(s.F.Workflow);
            Check(injected && s.Batches.SequenceEqual(new[] {15,10}) && s.Free == 0 && s.Paid == 7, "Mirror resumes a rank-window scene identity race and drains only free tickets");
            Check(s.F.Box.Commands.Count(c => S(c["UiToken"]) == "PVPClassUpUI") == 1 && Directory.Exists(Path.Combine(s.F.Root, "live", "evidence-observations")), "Mirror rank recovery keeps transient diagnostics without battle replay");
        }
        using (var s = new Scenario(Path.Combine(root, "mirror-resume")) { Hold = true, StopAfterMatch = true })
        {
            string error = "";
            try { await DailyMirror.Run(s.F.Workflow); } catch (StageHostException e) { error = e.Kind; }
            Check(error == "pending" && s.Batches.Count == 1 && s.F.Business.Records(s.F.Context).Single()["state"]!.GetValue<string>() == "unknown", "Mirror stopped after matching preserves a pending journal instead of matching again");
            s.F.Stopped = false;
            s.Settle();
            var reconcile = await s.F.Business.ReconcileAsync(s.F.Context);
            Check(S(s.F.Business.Records(s.F.Context).Single()["state"]) == "unknown" && B(reconcile["unresolved"]![0]!["recover_in_stage"]),
                "First result never completes a multi-battle batch; original queue may observe it without replay");
            int commands = s.F.Box.Commands.Count;
            error = "";
            var delayed = s.F.OnDelay;
            s.F.OnDelay = () => s.F.Time += 300;
            try { await DailyMirror.Run(s.F.Workflow); } catch (StageHostException e) { error = e.Kind; }
            s.F.OnDelay = delayed;
            Check(error == "pending" && s.F.Box.Commands.Count == commands, "Mirror unresolved first match blocks all repeat input");
            s.Hold = false; s.StopAfterMatch = false;
            await DailyMirror.Run(s.F.Workflow);
            Check(s.Batches.SequenceEqual(new[] {15,10}) && s.BatchRepeats.SequenceEqual(new[] {2,1}) && s.Free == 0 && s.Paid == 7, "Mirror resumes the original native batch and starts only the remainder afterward");
            Check(s.F.Business.Records(s.F.Context).All(r => S(r["state"]) == "completed"), "Mirror recovered transaction retains completed proof alongside later batches");
        }
    }
}
