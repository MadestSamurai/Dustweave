using BD2Daily;
using System.Text.Json.Nodes;
static class StageProtocolCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool value, string name)
        {
            if (!value)
                throw new Exception(name);
            cases.Add(name);
        }
        Check(new BD2Daily.Live.Frame().BridgeVersion == DailyStageObservation.BridgeVersion, "native frame version matches desktop acceptance contract");
        using var f = new FreeDrawStageCases.Fixture(Path.Combine(output, "managed-stage-boundary"));
        f.Current["BridgeVersion"] = DailyStageObservation.BridgeVersion;
        var calls = new List<int>();
        int index = 0;
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        string? failure = null;
        var stage = new ManagedHostFixture.Stage(async context => { int current = ++index; calls.Add(current); if (current == 1) { entered.SetResult(); await release.Task; } if (failure != null) throw new StageHostException(failure, "日常 애莉 繁體 😀"); return new JsonObject { ["state"] = "completed", ["label"] = "日常 애莉 繁體 😀", ["large"] = new string('界', 80000) }; });
        using var fixture = new ManagedHostFixture(f.Root, () => Task.FromResult(new DailyStageFrame(f.Current, f.Context)), f.Driver, f.Business, stages: new Dictionary<string, IDailyManagedStage> { ["mail"] = stage }, proofs: [DailyFreeDrawProof.Definition()]);
        var host = fixture.Host;
        await host.CallAsync("begin", arguments: new()
        {
            ["context"] = f.Context.DeepClone()
        });
        var first = host.CallAsync("execute", "mail");
        await entered.Task;
        var second = host.CallAsync("execute", "mail");
        Check(calls.Count == 1 && !second.IsCompleted, "managed host serializes stage execution in one controller");
        release.SetResult();
        var results = await Task.WhenAll(first, second);
        Check(calls.SequenceEqual(new[] { 1, 2 }), "queued managed calls execute once in order");
        Check(results.All(r => r["label"]!.GetValue<string>() == "日常 애莉 繁體 😀" && r["large"]!.GetValue<string>().Length == 80000), "managed business preserves large multilingual results without a text protocol");
        foreach (string kind in new[] { "pending", "stopped", "identity", "adapter" })
        {
            failure = kind;
            string actual = "";
            try
            {
                await host.CallAsync("execute", "mail");
            }
            catch (StageHostException e) { actual = e.Kind; }
            Check(actual == kind, "managed exception retains classification: " + kind);
        }
        await host.DisposeAsync();
        string closed = "";
        try
        {
            await host.CallAsync("execute", "mail");
        }
        catch (StageHostException e) { closed = e.Kind; }
        Check(closed == "transport", "disposed stage host rejects new operations");
    }
}
