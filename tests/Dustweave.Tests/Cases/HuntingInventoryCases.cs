using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;

static class HuntingInventoryCases
{
    const string Item = "ὬὩὨὮὥὫὧὪὥὥὨ.ὯὫὪὡὭὤὪὮὫὬὣ", Count = "ὬὩὨὮὥὫὧὪὥὥὨ.ὮὢὥὯὥὧὤὭὨὨὪ";
    static JsonObject Owned(int id, object? count) => WorkflowCases.Reading("hunt.owned", (Item, id), (Count, count));
    public static async Task Run(string output, List<string> checks)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        int serial = 0;
        Fixture Make() => new(Path.Combine(output, "holy-stone-" + ++serial));
        using (var f = Make())
        {
            var result = await DailyHunting.Run(f.H.Workflow, "stones");
            Check(S(result["state"]) == "completed" && f.Expands == 1, "Folded inventory is expanded once and production hunting completes");
            Check(f.Dispatched == 1 && f.Element == "wind" && f.Free == 0, "Least owned attribute is selected before one free-only batch");
            var record = f.H.Business.Records(f.H.Context, "hunt.dispatch").Single();
            Check(S(record["state"]) == "completed" && N(record["result"]?["cost"]) == 20, "Recovered stock selection preserves native receipt and exact free torch debit");
            Check(f.Paid == 999 && f.H.Box.Commands.All(c => S(c["Kind"]) != "reward_refresh"), "Inventory recovery neither spends stored torches nor queries unrelated tasks");
        }
        using (var f = Make())
        {
            f.Folded = false; f.Page();
            Check(await DailyHunting.StoneElement(f.H.Workflow, "least") == "wind" && f.Expands == 0,
                "An already expanded inventory stays expanded without a toggle");
        }
        using (var f = Make())
        {
            f.DelayRows = true;
            Check(await DailyHunting.StoneElement(f.H.Workflow, "least") == "wind" && f.Expands == 1 && f.H.Time >= .45,
                "Delayed five-row population waits for inventory, not only the selected tab");
        }
        foreach (string element in DailyHunting.Elements)
        using (var f = Make())
        {
            f.NeverRows = true;
            f.H.Workflow.Settings.Hunt.StoneElement = element;
            await DailyHunting.Run(f.H.Workflow, "stones");
            Check(f.Expands == 0 && f.Dispatched == 1 && f.Element == element,
                "Explicit " + element + " runs independently of hidden/missing owned inventory");
        }
        using (var f = Make())
        {
            f.NeverRows = true;
            try { await DailyHunting.Run(f.H.Workflow, "stones"); throw new Exception("Missing inventory accepted"); }
            catch (StageHostException ex) when (ex.Kind == "adapter")
            {
                Check(ex.Message == "Stone inventory incomplete" && f.Expands == 1 && f.Dispatched == 0 && f.Free == 20,
                    "Unreadable inventory stops without torch consumption or repeated toggling");
            }
            var diagnostic = Directory.GetFiles(Path.Combine(f.H.Root, "live", "diagnostics", "failures"), "*.json")
                .Select(path => JsonNode.Parse(File.ReadAllText(path))!).Single(j => S(j["kind"]) == "hunting_inventory");
            Check(diagnostic["detail"]?["readings"] is JsonArray && B(diagnostic["detail"]?["expanded"]),
                "Failed inventory readings survive diagnostic timeline rotation");
            f.NeverRows = false;
            await DailyHunting.Run(f.H.Workflow, "stones");
            Check(f.Expands == 1 && f.Dispatched == 1, "Fresh retry recovers without old failure blocking or folding the inventory again");
        }
        using (var f = Make())
        {
            f.Free = 0;
            await DailyHunting.Run(f.H.Workflow, "stones");
            Check(f.Expands == 0 && f.Dispatched == 0, "No free torches skips inventory work entirely");
        }
        using (var f = Make())
        {
            f.H.OnDelay = () => f.H.Context["actor"]![3] = "other-account";
            f.DelayRows = true;
            try { await DailyHunting.Run(f.H.Workflow, "stones"); throw new Exception("Changed account accepted"); }
            catch (DailyStepException) { Check(f.Dispatched == 0, "Actor change during inventory wait prevents hunting"); }
        }
        var zeros = Enumerable.Range(111, 5).Select(id => Owned(id, 0)).ToArray();
        Check(DailyHunting.StoneStock(WorkflowCases.Evidence(zeros)).Values.All(n => n == 0), "Observed zero balances are valid inventory");
        foreach (string invalid in new[] { "missing-row", "null-count", "negative-count", "duplicate", "wrong-id", "read-error" })
        {
            var rows = zeros.Select(r => r.DeepClone().AsObject()).ToList();
            if (invalid == "missing-row") rows.RemoveAt(0);
            if (invalid == "null-count") rows[0] = Owned(111, null);
            if (invalid == "negative-count") rows[0] = Owned(111, -1);
            if (invalid == "duplicate") rows.Add(Owned(111, 0));
            if (invalid == "wrong-id") rows[0] = Owned(999, 0);
            if (invalid == "read-error") rows[0]["Error"] = "unavailable";
            try { DailyHunting.StoneStock(WorkflowCases.Evidence(rows.ToArray())); throw new Exception("Invalid stock accepted: " + invalid); }
            catch (InvalidDataException) { checks.Add("Stock comparison rejects " + invalid + " instead of treating unreadable data as zero"); }
        }
        Check(DailyUserText.Describe("Stone inventory incomplete").Contains("未消耗火把") && DailyUserText.Describe("Stone inventory incomplete").Contains("展开库存栏"),
            "Inventory failure explains spending status and a usable recovery action");
    }

    sealed class Fixture : IDisposable
    {
        public readonly WorkflowHarness H;
        public bool Folded = true, DelayRows, NeverRows;
        public int Expands, Dispatched, Free = 20, Paid = 999;
        public string Element = "fire";
        double expandedAt = -100;
        int count = 1;
        JsonObject Table() => O(("id", 100 + System.Array.IndexOf(DailyHunting.Elements, Element)), ("groupId", 10),
            ("packId", 100), ("typeGroupId", 3 + System.Array.IndexOf(DailyHunting.Elements, Element)), ("apPerTime", 1));
        public Fixture(string root)
        {
            H = new(root, [DailyHunting.Proof()]);
            H.Readings = () => new[] {
                WorkflowCases.Reading("hunt.selection", (DailyHunting.TablePath, Table())),
                WorkflowCases.Reading("hunt.native", ("ὤὫὣὡὫὤὦὯὢὡὬ", "ABLE")),
                WorkflowCases.Reading("daily.currency", ("FreeHuntingAp", 0), ("BonusHuntingAp", 33), ("FreeTorchLightAp", Free), ("TorchLightAp", Paid)),
                WorkflowCases.Reading("hunt.popup", (DailyHunting.TablePath, Table()), (DailyHunting.CountPath, count), ("_buttonFreeOnly.IsEnable", true),
                    ("_sliderAutoCount.ὩὧὩὦὫὮὨὢὦὠὯ", 20), ("_sliderAutoCount.ὧὬὩὬὢὦὩὥὥὩὢ", 10)),
                WorkflowCases.Reading("reward.presentation", ("ὣὤὥὦὯὦὩὤὨὠὪ", true), ("ὪὯὣὥὬὫὬὩὠὮὠ", true), ("ὮὬὧὦὣὠὠὤὮὦὧ", true), ("ὦὡὮὫὧὠὮὡὭὭὬ", false))
            }.Concat(Folded || NeverRows || DelayRows && H.Time - expandedAt < .45 ? [] : Enumerable.Range(111, 5).Select(id => Owned(id, id == 113 ? 0 : 100))).ToArray();
            H.OnCommand = c =>
            {
                H.Time += .01;
                string ui = S(c["UiToken"]);
                string field = DailyNavigationDecision.Rows(H.Frame).SelectMany(r => Rows(r["Targets"]))
                    .Where(t => N(t["Id"]) == N(c["TargetId"])).Select(t => S(t["Field"])).SingleOrDefault() ?? "";
                if (ui == DailyHunting.Ui && field == "_buttonFoldAndExpand")
                { Folded = !Folded; Expands++; expandedAt = H.Time; Page(); }
                else if (ui == DailyHunting.Ui && field == DailyHunting.SelectField)
                    H.Page("HuntDispatchPopupUI", "_sliderAutoCount._objPlusMaxButton", "_buttonHuntDispatch._button");
                else if (ui == DailyHunting.Ui && field == "_objBackButton") H.Page("MenuUI");
                else if (ui == DailyHunting.Ui && field.EndsWith("/Button - Element"))
                    Element = DailyHunting.Elements.Single(e => field.Contains("Item - Element - " + char.ToUpperInvariant(e[0]) + e[1..]));
                else if (ui == DailyHunting.Ui && field.EndsWith("4 - Crystal")) { }
                else if (ui == "HuntDispatchPopupUI" && field == "_sliderAutoCount._objPlusMaxButton") count = 20;
                else if (ui == "HuntDispatchPopupUI" && field == "_buttonHuntDispatch._button")
                {
                    H.Event("hunt.dispatch", "request");
                    Free -= count; Dispatched++;
                    H.Event("hunt.dispatch", "response", ("RewardInfoBundle", O(("items", new[] { 1 }))));
                    H.Page("RewardReceivePopupUI", "_objBackButton");
                }
                else if (ui == "RewardReceivePopupUI") Page();
                else throw new Exception("Unexpected hunting input: " + c.ToJsonString());
            };
            Page();
        }
        public void Page() => H.Page(DailyHunting.Ui, new[] { "$pointer/Layout - Button/Button - 4 - Crystal", "_buttonFoldAndExpand",
            Folded ? "_expandButtonCurrency" : "_foldButtonCurrency", DailyHunting.SelectField, "_objBackButton" }
            .Concat(DailyHunting.Elements.Select(e => "$pointer/SelectParent/Layout - Select/Tab - Element/UIScrollView - Element/UIViewport/UIContent/Item - Element - " + char.ToUpperInvariant(e[0]) + e[1..] + "/Button - Element")).ToArray());
        public void Dispose() => H.Dispose();
    }
}
