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
        public int Settled;
        public double SecondsPerBattle = 2;
        private int pending;
        private double settleAt;
        private const string Auto = "BattleAutoSettingPopupUI", Boost = "BattleAutoCurrencyAccelSettingPopupUI";
        public Scenario(string root)
        {
            F = new(root, [DailyMirror.Proof()]);
            F.Frame["Scene"] = "Map3001_001";
            F.Workflow.Settings.Mirror.Multiplier = 15;
            F.Readings = Readings;
            F.OnCommand = Command;
            F.OnDelay = () => { if (pending > 0 && !Hold && F.Time >= settleAt) Settle(); };
            Lobby();
        }
        private JsonObject[] Readings() =>
        [
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
            if (ui == "BattleUI_PVP" && field.EndsWith("/Button - AutoSetting", StringComparison.Ordinal)) { AutoPage(); return; }
            if (ui == Auto)
            {
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
        using (var s = new Scenario(Path.Combine(root, "mirror-full")))
        {
            var result = await DailyMirror.Run(s.F.Workflow);
            Check(s.Batches.SequenceEqual(new[] {15,10}) && s.BatchRepeats.SequenceEqual(new[] {2,1}) && s.Free == 0 && s.Paid == 7 && N(result["free_spent"]) == 40, "Mirror drains 40 free tickets with native 15x2 then 10x1 without paid tickets");
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
