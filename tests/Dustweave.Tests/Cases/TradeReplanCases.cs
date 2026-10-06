using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;
static class TradeReplanCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        using var f = new WorkflowHarness(Path.Combine(output, "trade-current-state"), DailyWorkflowRegistry.Proofs());
        var before = WorkflowCases.Evidence();
        before["Frame"] = f.Frame.DeepClone(); before["Taps"] = new JsonArray("trade.buy", "dispatch.start");
        foreach (string role in new[] { "trade.buy", "dispatch.start" })
        {
            var old = f.Business.Create(f.Context, role, before, O(("trade_session", "old"), ("action", "purchase"), ("_proof", O(("kind", role == "dispatch.start" ? "bargain" : "buy")))), O(("ui", "ShopUI")));
            old["state"] = "unknown"; f.Business.Save(old);
        }
        var reconciled = await f.Business.ReconcileAsync(f.Context);
        Check(reconciled["unresolved"]!.AsArray().Count == 0 && reconciled["trade_diagnostics"]!.AsArray().Count == 2 && f.Box.Commands.Count == 0,
            "old unknown trades are diagnostic only, never replayed or declared completed");
        Check(f.Business.Records(f.Context).All(op => S(op["state"]) == "unknown"), "original uncertain outcomes preserved honestly");
        var next = f.Business.Create(f.Context, "trade.buy", before, O(("trade_session", "fresh"), ("action", "purchase")), O(("ui", "ShopUI")));
        Check(S(next["state"]) == "prepared" && S(next["scope"]!["trade_session"]) == "fresh", "fresh current-state plan can create its own purchase without recovering old receipt");
        f.Business.RequireResolved(f.Context, "dispatch.start");
        cases.Add("old bargain receipt cannot block an unrelated talent stage");

        string directory = Path.Combine(output, "trade-stale-plan");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "execution.json"), "{obsolete or interrupted json");
        File.WriteAllText(Path.Combine(directory, "purchases.json"), "old split");
        DailyTradeJournal.ArchivePlan(directory);
        var archives = Directory.GetDirectories(Path.Combine(directory, "history"));
        Check(archives.Length == 1 && File.ReadAllText(Path.Combine(archives[0], "execution.json")) == "{obsolete or interrupted json",
            "even unparseable old plan can be archived without blocking current-state planning");
        Check(File.ReadAllText(Path.Combine(archives[0], "purchases.json")) == "old split", "diagnostic archive preserves original plan bytes");
    }
}
