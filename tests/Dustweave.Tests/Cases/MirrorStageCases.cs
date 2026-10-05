using BD2Daily;
using System.Text.Json.Nodes;
using static BD2Daily.DailyData;

static class MirrorStageCases
{
    private sealed class Scenario : IDisposable
    {
        public readonly WorkflowHarness F;
        public int Free = 40, Paid = 7, Multiplier = 40, Slider = 40, Repeats = 5;
        public bool FreeOnly, Skip, Hold, StopAfterMatch;
        public readonly List<int> Batches = [];
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
            WorkflowCases.Reading("mirror.auto", ("ὣὤὥὪὥὡὥὢὧὮὠ", Multiplier), ("ὯὠὭὣὮὠὯὬὤὯὠ", Repeats), ("_buttonFreeOnly.IsEnable", FreeOnly), ("_buttonSkip.IsEnable", Skip)),
            WorkflowCases.Reading("mirror.boost", ("_sliderCount.ὮὢὥὯὥὧὤὭὨὨὪ", Slider), ("_sliderCount._objPlus10Button.name", "+10"), ("_sliderCount._objMinus10Button.name", "-10"), ("_sliderCount._objPlusButton.name", "+1"), ("_sliderCount._objMinusButton.name", "-1")),
            WorkflowCases.Reading("mirror.input", ("ὨὬὬὯὫὨὦὧὨὩὭ", true), ("ὤὬὮὠὨὮὣὯὩὩὡ", true)),
            WorkflowCases.Reading("mirror.ready", ("ὣὤὥὦὯὦὩὤὨὠὪ", true), ("ὪὯὣὥὬὫὬὩὠὮὠ", true))
        ];
        private void Lobby() => F.Page("BattleUI_PVP", "Panel/Button - AutoSetting");
        private void AutoPage() => F.Page(Auto, "_btnSettingBoost", "Panel/FreeOnly/Button - Toggle", "Panel/BattleSkip/Button - Toggle", "Panel/Button - -100", "_currencyButtonOK");
        public void Settle()
        {
            if (pending == 0) throw new Exception("Unexpected second settlement");
            Free -= pending;
            F.Event("mirror.end", "response", ("win", true));
            pending = 0;
            Lobby();
            F.Time += .01;
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
                if (field == "_currencyButtonOK")
                {
                    if (!FreeOnly || !Skip || Repeats != 1 || Multiplier > Free || pending > 0) throw new Exception("Unsafe Mirror configuration");
                    Batches.Add(Multiplier); pending = Multiplier; settleAt = F.Time + 2;
                    F.Event("mirror.matching", "request"); F.Event("mirror.matching", "response"); F.Event("mirror.end", "request");
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
        using (var s = new Scenario(Path.Combine(root, "mirror-full")))
        {
            var result = await DailyMirror.Run(s.F.Workflow);
            Check(s.Batches.SequenceEqual(new[] {15,15,10}) && s.Free == 0 && s.Paid == 7 && N(result["free_spent"]) == 40, "Mirror production workflow drains 40 free tickets as 15+15+10 without paid tickets");
            Check(s.F.Time >= 6 && s.F.Business.Records(s.F.Context).Count(r => S(r["state"]) == "completed") == 3, "Mirror delayed native results complete exactly three durable transactions");
            Check(DailyNavigationDecision.Types(s.F.Frame).SetEquals(["MenuUI"]), "Mirror returns after the final confirmed settlement");
            await DailyMirror.Run(s.F.Workflow);
            Check(s.Batches.Count == 3, "Mirror rerun uses current zero free balance without another match");
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
            Check(injected && s.Batches.SequenceEqual(new[] {15,15,10}) && s.Free == 0 && s.Paid == 7, "Mirror resumes a rank-window scene identity race and drains only free tickets");
            Check(s.F.Box.Commands.Count(c => S(c["UiToken"]) == "PVPClassUpUI") == 1 && Directory.Exists(Path.Combine(s.F.Root, "live", "evidence-observations")), "Mirror rank recovery keeps transient diagnostics without battle replay");
        }
        using (var s = new Scenario(Path.Combine(root, "mirror-resume")) { Hold = true, StopAfterMatch = true })
        {
            string error = "";
            try { await DailyMirror.Run(s.F.Workflow); } catch (StageHostException e) { error = e.Kind; }
            Check(error == "pending" && s.Batches.Count == 1 && s.F.Business.Records(s.F.Context).Single()["state"]!.GetValue<string>() == "unknown", "Mirror stopped after matching preserves a pending journal instead of matching again");
            s.F.Stopped = false;
            int commands = s.F.Box.Commands.Count;
            error = "";
            try { await DailyMirror.Run(s.F.Workflow); } catch (StageHostException e) { error = e.Kind; }
            Check(error == "pending" && s.F.Box.Commands.Count == commands, "Mirror unresolved first match blocks all repeat input");
            s.Settle(); s.Hold = false; s.StopAfterMatch = false;
            await DailyMirror.Run(s.F.Workflow);
            Check(s.Batches.SequenceEqual(new[] {15,15,10}) && s.Free == 0 && s.Paid == 7, "Mirror resumes original settlement and consumes only the remaining 25 free tickets");
            Check(s.F.Business.Records(s.F.Context).All(r => S(r["state"]) == "completed"), "Mirror recovered transaction retains completed proof alongside later batches");
        }
    }
}
