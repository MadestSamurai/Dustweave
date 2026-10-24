using Dustweave;
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
        {
            var frame=CommandDriverCases.Frame();frame["Protocol"]=1;frame["BridgeVersion"]=DailyStageObservation.BridgeVersion;frame["Scene"]="Empty";
            var identity=new DailySnapshot {ProcessId=7,ProcessStartTicks=10,AccountKey=new string('a',64),PlayerKey=new string('b',64),State="identified",Loading=true,FrameUtcTicks=100000000,Guild=new(){ServerKey="server",CycleKey="today"}};
            var loading=new DailyStageObservation(identity.AccountKey,()=>frame,()=>identity,now:()=>100000000,timeout:TimeSpan.Zero);
            string reason="";
            Check((await loading.ReadFrameAsync()).Daily!.Loading,"read-only observation retains actual loading state without falsely reporting lost identity");
            identity.Loading=false;frame["Scene"]="Map";
            Check((await loading.ReadFrameAsync()).Context["cycle"]!.GetValue<string>()=="today","same game resumes directly after loading without discarding its cycle");
            identity.Scene="Empty";identity.MainMenuReady=true;frame["Scene"]="Empty";
            Check((await loading.ReadFrameAsync()).Frame["Scene"]!.GetValue<string>()=="Empty","confirmed native home menu starts daily observation without loading a map first");
            frame["Surfaces"]!.AsArray().Add(new JsonObject{["Type"]="LoadingUI"});
            Check(DailyHomeDecision.Inspect((await loading.ReadFrameAsync()).Frame,DailyNavigationPolicy.Load()).Kind=="waiting",
                "loading frame reaches navigation which waits without authorizing input");
            frame["Surfaces"]!.AsArray().RemoveAt(1);identity.MainMenuReady=false;
            foreach(string page in new[]{"MyRoomUI","MailUI","GuildUI","GachaUI","EventUI","FutureFeatureUI"}){
                frame["Surfaces"]![0]!["Type"]=page;
                Check(DailyNavigationDecision.Types((await loading.ReadFrameAsync()).Frame).Contains(page),"read-only Empty observation supports child page without MenuUI: "+page);
            }
            frame["Surfaces"]!.AsArray().Clear();
            Check(DailyNavigationDecision.Types((await loading.ReadFrameAsync()).Frame).Count==0 &&
                DailyHomeDecision.Inspect(frame,DailyNavigationPolicy.Load()).Kind=="waiting", "no visible UI is observable but cannot authorize a navigation action");
            frame["Surfaces"]!.AsArray().Add(new JsonObject{["Type"]="IntroUI"});
            Check(DailyHomeDecision.Inspect((await loading.ReadFrameAsync()).Frame,DailyNavigationPolicy.Load()).Kind=="blocked",
                "navigation rejects login page even before the common identity observer catches up");
            frame["Surfaces"]![0]!["Type"]="EntranceBackgroundCoverUI";frame["Scene"]="Map";
            Check(DailyHomeDecision.Inspect((await loading.ReadFrameAsync()).Frame,DailyNavigationPolicy.Load()).Kind=="waiting",
                "navigation waits for actual transition cover on every scene");
            frame["Surfaces"]![0]!["Type"]="MyRoomUI";
            identity.AccountKey=new string('c',64);reason="";try{await loading.ReadFrameAsync();}catch(StageHostException e){reason=e.Message;}
            Check(reason.Contains("识别已变化"),"loading recovery still rejects changed account identity");
            Check(DailyDiagnosticExport.Allowed("live/observation-failures/loading.json"),"observation failure includes both live and identity evidence in diagnostic export");
        }
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
