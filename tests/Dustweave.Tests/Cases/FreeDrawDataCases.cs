using Dustweave;
using System.Text.Json.Nodes;
static class FreeDrawDataCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool value, string name)
        {
            if (!value)
                throw new Exception(name);
            cases.Add(name);
        }
        void Reject(Action action, string name)
        {
            bool bad = false;
            try
            {
                action();
            }
            catch (InvalidDataException) { bad = true; }
            Check(bad, name);
        }
        foreach (bool changeAtFinalRead in new[] { false, true })
        {
            string ownerRoot = Path.Combine(output, changeAtFinalRead ? "owned-final-preflight" : "owned-first-preflight");
            var box = new CommandDriverCases.Mailbox();
            var original = CommandDriverCases.Frame();
            original["Surfaces"]![0]!["Type"] = "MessagePopupUI";
            original["Surfaces"]![0]!["Targets"]![0]!["Field"] = "_buttonOK";
            var current = original.DeepClone().AsObject();
            int reads = 0;
            double time = 0;
            if (!changeAtFinalRead)
                current["UiToken"] = "replacement";
            using var driver = new DailyCommandDriver(ownerRoot, box, () => { if (changeAtFinalRead && ++reads == 4) current["UiToken"] = "replacement"; return Task.FromResult(new DailyStageFrame(current.DeepClone().AsObject(), CommandDriverCases.Context())); }, () => false, () => 100000000, () => time, t => { time += t.TotalSeconds; return Task.CompletedTask; });
            driver.Bind(CommandDriverCases.Context());
            driver.Acquire("live");
            bool rejected = false;
            try
            {
                await driver.SendObservedAsync(new()
                {
                    ["ui"] = "MessagePopupUI",
                    ["field"] = "_buttonOK"
                }, original);
            }
            catch (DailyStepException e) { rejected = e.Kind == "rejected"; }
            Check(rejected && box.Commands.Count == 0, changeAtFinalRead ? "owned confirmation rechecks its popup token in final preflight" : "owned confirmation cannot click a replacement popup with the same OK field");
        }
        using (var f = new FreeDrawStageCases.Fixture(Path.Combine(output, "preview-final-ownership")))
        {
            var op = await f.PreviewRecord();
            f.Current["UiToken"] = "replacement";
            bool stopped = false;
            try
            {
                await f.Business.CommitAsync(op, f.Context);
            }
            catch (StageHostException e) { stopped = e.Kind == "pending"; }
            Check(stopped && f.Confirmations == 0 && f.Records().Single()["state"]!.GetValue<string>() == "preview_ready", "managed business rechecks preview ownership before persisting a consuming dispatch");
        }
        string root = Path.Combine(output, "free-draw-data"), assembly = new('a', 64), database = new('b', 64);
        Directory.CreateDirectory(root);
        var tables = new JsonArray(new JsonObject { ["id"] = 50, ["freeCountDay"] = 1, ["gachaCount"] = 1 }, new JsonObject { ["id"] = 51, ["freeCountDay"] = 0, ["gachaCount"] = 10 });
        var groups = new JsonArray(new JsonObject { ["id"] = 5, ["oneTimeGachaId"] = 50 });
        var manifest = new JsonObject { ["assemblySha256"] = assembly, ["databaseSha256"] = database, ["tables"] = new JsonObject { ["GachaTable"] = 2, ["GachaGroupTable"] = 1 } };
        void Save()
        {
            DailyJson.Write(Path.Combine(root, "manifest.json"), manifest);
            DailyJson.Write(Path.Combine(root, "GachaTable.json"), tables);
            DailyJson.Write(Path.Combine(root, "GachaGroupTable.json"), groups);
        }
        Save();
        var rules = DailyFreeDrawData.ReadExport(root, assembly, database);
        Check(rules.Require(50).Group == 5 && rules.Projection["source"]!["database_sha256"]!.GetValue<string>() == database, "managed free draw data accepts only a complete export tied to the current assembly/database");
        Reject(() => rules.Require(51), "managed exported draw rules exclude paid and ten-draw definitions");
        Reject(() => DailyFreeDrawData.ReadExport(root, new string('c', 64), database), "managed free draw data rejects an export from a different assembly");
        Reject(() => DailyFreeDrawData.ReadExport(root, assembly, new string('c', 64)), "managed free draw data rejects an export from an older database");
        manifest["tables"]!["GachaTable"] = 3;
        Save();
        Reject(() => DailyFreeDrawData.ReadExport(root, assembly, database), "managed free draw export must include its entire advertised table");
        manifest["tables"]!["GachaTable"] = 2;
        groups.Add(new JsonObject { ["id"] = 6, ["oneTimeGachaId"] = 50 });
        manifest["tables"]!["GachaGroupTable"] = 2;
        Save();
        Reject(() => DailyFreeDrawData.ReadExport(root, assembly, database), "managed free draw definitions reject ambiguous group ownership");
        groups.RemoveAt(1);
        manifest["tables"]!["GachaGroupTable"] = 1;
        tables[0]!["freeCountDay"] = 0;
        Save();
        Reject(() => DailyFreeDrawData.ReadExport(root, assembly, database), "unrecognized free draw definitions stop before any native batch");
        using (var f = new FreeDrawStageCases.Fixture(Path.Combine(output, "free-draw-preflight-failure")))
        {
            int preparation = 0;
            var stage = new DailyFreeDrawStage(f.Driver, f.Business, () => false, () => f.Seconds, f.Advance, FreeDrawStageCases.Fixture.Rules(), prepare: () => { preparation++; throw new StageHostException("adapter", "incomplete data"); });
            string kind = "";
            try
            {
                await stage.ExecuteAsync(f.Context, (o, s, a) => throw new Exception("No Python"));
            }
            catch (StageHostException e) { kind = e.Kind; }
            Check(kind == "adapter" && preparation == 1 && f.Box.Commands.Count == 0 && f.Records().Count == 0, "failed managed free draw data preparation submits no menu, preview or consuming input");
            var op = await f.PreviewRecord();
            op["state"] = "unknown_preview";
            f.Business.Save(op);
            kind = "";
            try
            {
                await stage.ExecuteAsync(f.Context, (o, s, a) => throw new Exception("No Python"));
            }
            catch (StageHostException e) { kind = e.Kind; }
            Check(kind == "pending" && preparation == 1 && f.Box.Commands.Count == 0, "pending free draw guard runs before new rule preparation or gameplay");
        }
    }
}


