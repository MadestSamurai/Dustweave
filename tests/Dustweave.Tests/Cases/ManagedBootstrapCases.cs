using Dustweave;
using System.Text.Json.Nodes;
static class ManagedBootstrapCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool value, string name)
        {
            if (!value)
                throw new Exception(name);
            cases.Add(name);
        }
        async Task Reject(Func<Task> action, string kind, string name)
        {
            string actual = "";
            try
            {
                await action();
            }
            catch (StageHostException e) { actual = e.Kind; }
            Check(actual == kind, name + " (" + actual + ")");
        }
        int n = 0;
        FreeDrawStageCases.Fixture Create() => new(Path.Combine(output, "managed-bootstrap-" + (++n)));
        DailyManagedBootstrap Boot(FreeDrawStageCases.Fixture f) => new(f.Root, f.Driver, () => Task.FromResult(new DailyStageFrame(f.Current.DeepClone().AsObject(), f.Context.DeepClone().AsObject())), () => f.Stopped);
        foreach (string failure in new[] { "preflight", "explicit-rejection", "uncertain", "identity", "stopped" })
        using (var f = Create())
        {
            int next = 0;
            var failing = new ManagedHostFixture.Stage(_ =>
            {
                if (failure == "identity") throw new StageHostException("identity", "identity changed");
                if (failure == "stopped") f.Stopped = true;
                throw new DailyStepException(failure == "uncertain" ? "pending" : "rejected", "test native input boundary", failure is "explicit-rejection" or "uncertain");
            });
            var following = new ManagedHostFixture.Stage(_ => { next++; return Task.FromResult(new JsonObject { ["state"] = "completed" }); });
            using var fixture = new ManagedHostFixture(f.Root, () => Task.FromResult(new DailyStageFrame(f.Current, f.Context)), f.Driver, f.Business, bootstrap: Boot(f), stages: new Dictionary<string, IDailyManagedStage> { ["free_draws"] = failing, ["mail"] = following }, proofs: [DailyFreeDrawProof.Definition()], stopped: () => f.Stopped);
            var result = await new DailyQueueEngine(fixture.Host, () => f.Stopped, () => new DailyPreferences()).RunAsync(new(f.Root, (string)f.Context["actor"]![3]!, Path.Combine(f.Root, "queue.json"), Tasks: ["free_draws", "mail"]));
            bool rejected = failure is "preflight" or "explicit-rejection";
            Check(result["state"]!.GetValue<string>() == (rejected ? "partial" : "paused") && next == (rejected ? 1 : 0),
                "production host isolates definite rejection but preserves uncertain/identity/operator stop: " + failure);
            Check(f.Box.Commands.Count == 0, "failure classification never replays native actions: " + failure);
        }
        using (var f = Create())
        {
            f.Current["BridgeVersion"] = DailyStageObservation.BridgeVersion;
            using var fixture = new ManagedHostFixture(f.Root, () => Task.FromResult(new DailyStageFrame(f.Current, f.Context)), f.Driver, f.Business, bootstrap: Boot(f), stages: new Dictionary<string, IDailyManagedStage> { ["free_draws"] = f.Stage }, proofs: [DailyFreeDrawProof.Definition()], stopped: () => f.Stopped);
            var host = fixture.Host;
            var settings = new DailyPreferences();
            settings.Stages.FreeDraws = true;
            var engine = new DailyQueueEngine(host, () => f.Stopped, () => settings);
            var result = await engine.RunAsync(new(f.Root, (string)f.Context["actor"]![3]!, Path.Combine(f.Root, "queue.json"), Tasks: ["free_draws"]));
            Check(result["state"]!.GetValue<string>() == "completed" && f.Confirmations == 2, "full managed queue observes, begins, reconciles and finishes both free draws");
            Check(result["items"]![0]!["result"]!["engine"]!.GetValue<string>() == "dotnet-free-draws-v1", "managed queue reports the actual business adapter");
            await Reject(() => host.CallAsync("begin", arguments: new() { ["context"] = f.Context.DeepClone() }), "protocol", "queue cannot repeat begin");
            await Reject(() => host.CallAsync("execute", "equipment"), "protocol", "unregistered business has no process fallback");
        }
        using (var f = Create())
        {
            f.Box.Values["live:command.json"] = System.Text.Encoding.UTF8.GetBytes("{}");
            await Reject(() => Boot(f).BeginAsync(f.Context), "pending", "outstanding native command is preserved");
            Check(f.Box.Values.ContainsKey("live:command.json") && f.Box.Values.ContainsKey("live:pause") && f.Box.Commands.Count == 0, "outstanding command is neither deleted nor replayed");
        }
        using (var f = Create())
        {
            f.Current["BridgeVersion"] = DailyStageObservation.BridgeVersion;
            f.Box.Values["live:pause"] = [];
            var boot = Boot(f);
            var result = await boot.BeginAsync(f.Context);
            Check(result["state"]!.GetValue<string>() == "ready" && !f.Box.Values.ContainsKey("live:pause") && f.Driver.HasControl, "one driver lease resumes the bound queue");
        }
        using (var f = Create())
        {
            f.Current["BridgeVersion"] = DailyStageObservation.BridgeVersion;
            using var fixture = new ManagedHostFixture(f.Root, () => Task.FromResult(new DailyStageFrame(f.Current, f.Context)), f.Driver, f.Business, bootstrap: Boot(f), proofs: [DailyFreeDrawProof.Definition()]);
            await fixture.Host.CallAsync("begin", arguments: new()
            {
                ["context"] = f.Context.DeepClone()
            });
            var op = new JsonObject { ["id"] = Guid.NewGuid().ToString("N"), ["role"] = "unknown.consume", ["stage"] = "equipment", ["state"] = "unknown", ["account"] = f.Context["actor"]![3]!.DeepClone(), ["player"] = f.Context["actor"]![4]!.DeepClone(), ["server"] = f.Context["server"]!.DeepClone(), ["cycle"] = "yesterday" };
            string path = Path.Combine(f.Root, "live", "business", op["id"]!.GetValue<string>() + ".json");
            DailyJson.Write(path, op);
            var recovered = await fixture.Host.CallAsync("reconcile");
            Check(recovered["unresolved"]!.AsArray().Count == 1 && f.Box.Commands.Count == 0, "unknown old consumptions block without replay across resets");
            op["account"] = new string('c', 64);
            DailyJson.Write(path, op);
            recovered = await fixture.Host.CallAsync("reconcile");
            Check(recovered["unresolved"]!.AsArray().Count == 0, "another account's pending record remains untouched and does not block");
        }
        using (var f = Create())
        {
            f.Current["BridgeVersion"] = DailyStageObservation.BridgeVersion - 1;
            f.Box.Values["live:pause"] = [];
            await Reject(() => Boot(f).BeginAsync(f.Context), "identity", "old bridge cannot clear pause");
            Check(f.Box.Values.ContainsKey("live:pause") && f.Box.Commands.Count == 0, "old bridge receives no input");
        }
        using (var f = Create())
        {
            f.Current["BridgeVersion"] = DailyStageObservation.BridgeVersion;
            var wrong = f.Context.DeepClone().AsObject();
            wrong["cycle"] = "tomorrow";
            await Reject(() => Boot(f).BeginAsync(wrong), "identity", "changed cycle cannot begin");
            Check(f.Box.Commands.Count == 0, "changed cycle receives no input");
        }
        using (var f = Create())
        {
            f.Stopped = true;
            await Reject(() => Boot(f).BeginAsync(f.Context), "stopped", "stopped bootstrap never resumes");
        }
        using (var f = Create())
        {
            f.Driver.EnsureBound(f.Context);
            var wrong = f.Context.DeepClone().AsObject();
            wrong["server"] = "other";
            await Reject(() => { f.Driver.EnsureBound(wrong); return Task.CompletedTask; }, "identity", "driver cannot be rebound to another server");
        }
    }
}
