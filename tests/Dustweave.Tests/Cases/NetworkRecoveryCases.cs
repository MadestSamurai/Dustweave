using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.Json.Nodes;
using BD2Daily.Live;
using Dustweave;
using static Dustweave.DailyData;

internal static class NetworkRecoveryCases
{
    internal sealed class Scenario(string mode = "ok")
    {
        public long Now = 100000000;
        public int Acks, Starts, Claims, Permits, Reads;
        public long AckAt, StartAt;
        public StartupPermit? Permit;
        public bool Used, Stop;
        public JsonObject Context = CommandDriverCases.Context();
        public Task Delay(TimeSpan value) { Now += value.Ticks; return Task.CompletedTask; }
        public Task Ack()
        {
            Acks++; AckAt = Now;
            if (mode == "lost-receipt") throw new DailyStepException("pending", "lost", true);
            return Task.CompletedTask;
        }
        public void Grant(StartupPermit? permit)
        {
            Permit = permit;
            if (permit == null) return;
            Permits++;
            if (Starts == 0) { Starts++; StartAt = Now; }
        }
        public void Claim() { Claims++; if (Used) throw new StageHostException("network", "already-used"); Used = true; }
        public Task<DailyStageFrame> Read()
        {
            Reads++;
            if (mode == "read-gap" && Acks > 0 && Reads is >= 4 and <= 7) throw new IOException("missing observation");
            if (mode == "stop" && Starts > 0) Stop = true;
            var f = CommandDriverCases.Frame(); f["Protocol"] = 1; f["BridgeVersion"] = DailyBridgeVersion.Current;
            f["AtUtcTicks"] = Now; f["Error"] = "";
            var d = new DailySnapshot { ProcessId = 7, ProcessStartTicks = 10, InstanceId = "daily-instance", Sequence = Reads,
                FrameUtcTicks = Now, AccountKey = new string('a', 64), PlayerKey = new string('b', 64), State = "identified",
                Guild = new GuildObservation { ServerKey = "test", CycleKey = "today" }, Startup = new() { Supported = true } };
            if (Acks == 0 || mode == "stuck-popup" || Now - AckAt < 7000000 || mode == "second-error" && Starts > 0)
            {
                f["Surfaces"] = new JsonArray(O(("Id", mode == "second-error" && Starts > 0 ? 12 : 11), ("Type", "MessagePopupUI"),
                    ("NativeContext", DailyNetworkRecovery.RestartContext), ("Popup", true),
                    ("InputReady", mode != "popup-unavailable" && (mode != "popup-loading" || Reads >= 6)), ("Order", 10),
                    ("Targets", new JsonArray(O(("Id", 22), ("Field", "_buttonOK"), ("Enabled", true), ("Route", "ui"))))));
            }
            else if (Starts == 0 || Now - StartAt < 7000000 || mode == "new-title" || mode == "no-sdk")
            {
                f["Surfaces"] = new JsonArray(); f["Scene"] = "ReGame"; d.State = "waiting_start";
                d.Startup = new() { Supported = true, Visible = true, Ready = true, ClickEnabled = true,
                    TitleInstanceId = mode == "new-title" && Starts > 0 ? 92 : 91, Attempted = Starts > 0 };
                if (mode == "no-sdk") { d.AccountKey = ""; f["AccountKey"] = ""; }
                if (mode == "terms") d.Startup.BlockReason = "请先处理启动页弹窗：IntroMessagePopupUI";
                if (mode == "download") d.Startup.DownloadVisible = true;
            }
            if (Acks > 0)
            {
                if (mode == "login-notice" && Starts > 0 && Now - StartAt >= 7000000)
                    f["Surfaces"]!.AsArray().Add(O(("Id", 32), ("Type", "NewsPopupEventUI"), ("Popup", true), ("Order", 10)));
                if (mode == "account") d.AccountKey = new string('c', 64);
                if (mode == "player") d.PlayerKey = new string('c', 64);
                if (mode == "cycle") d.Guild.CycleKey = "tomorrow";
                if (mode == "process") d.ProcessId++;
                if (mode == "instance") f["Instance"] = "different";
                if (mode == "lease") d.LeaseOwner = "other";
                if (mode == "stale" && Reads < 8) f["AtUtcTicks"] = Now - 90000000;
            }
            return Task.FromResult(new DailyStageFrame(f, Context, d));
        }
        public Task<bool> Run() => DailyNetworkRecovery.RecoverAsync(Context, Read, Ack, Grant, Claim, () => Stop, _ => { }, () => Now, Delay);
    }
    public static async Task Run(string root, List<string> cases)
    {
        void Check(bool v, string name) { if (!v) throw new Exception(name); cases.Add("network: " + name); }
        foreach (string mode in new[] { "ok", "lost-receipt", "read-gap", "stale", "login-notice", "popup-loading" })
        {
            var s = new Scenario(mode);
            Check(await s.Run(), mode + " resumes after verified login");
            Check(s.Acks == 1 && s.Starts == 1 && s.Claims == 1 && s.Permit == null, mode + " one acknowledgement, one login, permit revoked");
            Check(s.StartAt - s.AckAt >= 30000000, mode + " weak-network grace before login");
        }
        foreach (string mode in new[] { "second-error", "stuck-popup", "stop", "terms", "download", "account", "player", "cycle", "process", "instance", "lease", "new-title", "no-sdk" })
        {
            var s = new Scenario(mode); bool failed = false;
            try { await s.Run(); } catch (StageHostException e) { failed = e.Kind is "network" or "stopped"; }
            Check(failed && s.Acks == 1 && s.Starts <= 1 && s.Permit == null, mode + " stops without another login");
            Check(s.Now <= 100000000 + TimeSpan.FromMinutes(3).Ticks + 3500000, mode + " wait is bounded");
        }
        {
            var s = new Scenario("popup-unavailable"); bool failed = false;
            try { await s.Run(); } catch (StageHostException e) { failed = e.Kind == "network"; }
            Check(failed && s.Acks == 0 && s.Claims == 0 && s.Starts == 0 && s.Now < 160000000,
                "unavailable popup has a short bounded wait without clicking or consuming login");
        }
        {
            var s = new Scenario { Stop = true }; bool failed = false;
            try { await s.Run(); } catch (StageHostException) { failed = true; }
            Check(failed && s.Acks == 0 && s.Claims == 0 && s.Permits == 0, "stop before acknowledgement leaves login untouched");
        }
        {
            var s = new Scenario { Used = true }; bool failed = false;
            try { await s.Run(); } catch (StageHostException) { failed = true; }
            Check(failed && s.Acks == 0 && s.Starts == 0, "persistent consumed attempt blocks host-restart retry");
        }
        var basic = new Scenario(); var observed = await basic.Read();
        var popup = DailyNetworkRecovery.Popup(observed.Frame)!;
        var action = O(("ui", "MessagePopupUI"), ("operation", "network_timeout_ack"), ("startup", true));
        var command = DailyCommandDriver.BuildCommand(observed.Frame, popup, 0, action, basic.Now, Guid.NewGuid().ToString("N"), "test");
        var options = new JsonSerializerOptions { IncludeFields = true };
        Frame NativeFrame(JsonObject f) => JsonSerializer.Deserialize<Frame>(f.ToJsonString(), options)!;
        Command NativeCommand(JsonObject c) => JsonSerializer.Deserialize<Command>(c.ToJsonString(), options)!;
        Check(LivePolicy.Gate(NativeCommand(command), NativeFrame(observed.Frame), basic.Now, false) == "", "managed startup scope reaches native gate");
        foreach (string mode in new[] { "gameplay", "wrong-account", "target", "items", "old-bridge", "unrelated", "cancel", "disabled", "hidden", "foreground", "stale", "ownership" })
        {
            var c = NativeCommand(command); var f = NativeFrame(observed.Frame); var p = f.Surfaces[0];
            switch (mode)
            {
                case "gameplay": c.Scope = "gameplay"; break;
                case "wrong-account": c.AccountKey = "other"; break;
                case "target": c.TargetId = 22; break;
                case "items": c.Items = [1]; break;
                case "old-bridge": f.BridgeVersion = 145; break;
                case "unrelated": p.NativeContext = BackgroundErrorPolicy.RequestContext; break;
                case "cancel": p.Targets = [..p.Targets, new() { Field = "_buttonCancel", Enabled = true }]; break;
                case "disabled": p.Targets[0].Enabled = false; break;
                case "hidden": p.InputReady = false; break;
                case "foreground": f.Surfaces = [..f.Surfaces, new() { Id = 99, Type = "Other", Popup = true, Order = 20 }]; break;
                case "stale": f.AtUtcTicks -= 90000000; break;
            }
            Check(LivePolicy.Gate(c, f, basic.Now, mode == "ownership") != "", "native guard rejects " + mode);
        }
        {
            var box = new CommandDriverCases.Mailbox();
            using var driver = new DailyCommandDriver(Path.Combine(root, "network-driver"), box, () => Task.FromResult(observed), () => false, () => basic.Now);
            driver.Bind(basic.Context);
            await driver.AcknowledgeNetworkTimeoutAsync();
            Check(box.Commands.Count == 1 && LivePolicy.Gate(NativeCommand(box.Commands[0]), NativeFrame(observed.Frame), basic.Now, false) == "",
                "real managed driver sends one native-compatible command");
            bool refused = false;
            try { await driver.SubmitAsync(action); } catch (DailyStepException) { refused = true; }
            Check(refused && box.Commands.Count == 1, "ordinary workflow cannot request a login restart");
        }
        {
            var watch = new DailyStageObservation(new string('a', 64), () => observed.Frame, () => observed.Daily, now: () => basic.Now);
            bool interrupted = false;
            try { await watch.ReadFrameAsync(); } catch (StageHostException e) { interrupted = e.Kind == "network" && e.HoldQueue; }
            Check(interrupted, "network popup unwinds queue before unrelated navigation");
        }
        {
            var d = new DailySnapshot { ProcessId = 7, ProcessStartTicks = 10, InstanceId = "daily", Sequence = 1, FrameUtcTicks = basic.Now,
                AccountKey = new string('a',64), Startup = new() { Supported = true, Visible = true, Ready = true, ClickEnabled = true, TitleInstanceId = 91 } };
            var p = new StartupPermit { Owner = "one", ProcessId = 7, ProcessStartTicks = 10, InstanceId = "daily", AccountKey = d.AccountKey,
                ExpiresUtcTicks = basic.Now + 50000000, ResumeOnly = true, TitleInstanceId = 91 };
            Check(StartupPolicy.Decide(d, p, basic.Now) == "click_start", "recovery permit enters its observed title");
            d.Startup.TitleInstanceId++;
            Check(StartupPolicy.Decide(d, p, basic.Now) != "click_start", "title replacement between polls cannot cause another login");
            d.Startup.TitleInstanceId = 91; d.Startup.DownloadVisible = true; d.Startup.DownloadReady = true; d.Startup.DownloadInstanceId = 1;
            Check(StartupPolicy.Decide(d, p, basic.Now) != "click_download", "recovery cannot approve new resource downloads");
        }
        foreach (bool secondFailure in new[] { false, true })
        {
            int executions = 0, recoveries = 0; string id = Guid.NewGuid().ToString("N");
            var record = O(("id", id), ("state", "paused"), ("context", basic.Context), ("error_kind", "network"),
                ("items", new JsonArray(O(("task", "mail"), ("state", "completed")), O(("task", "trade"), ("state", "recovery_required")))));
            var result = await DailyNetworkRecovery.ResumeQueueAsync(new(root, new string('a',64), "test-output", Tasks: ["mail", "trade"]), r =>
            {
                if (++executions == 2)
                {
                    Check(r.Resume == id && r.Tasks == null && r.RetryOf == null && !r.SyncCollection, "resume retains original queue ID and selection");
                    record["state"] = secondFailure ? "paused" : "completed";
                }
                return Task.FromResult(record);
            }, _ => { recoveries++; return Task.FromResult(true); }, () => false);
            Check(executions == 2 && recoveries == 1 && S(result["state"]) == (secondFailure ? "paused" : "completed"), "queue resumes at most once: " + secondFailure);
            Check(S(result["items"]![0]!["state"]) == "completed" && S(result["items"]![1]!["state"]) == "recovery_required", "completed and uncertain operations are not reset: " + secondFailure);
        }
        {
            // Actual consuming-operation journal: the server applied it, but the
            // next observation disconnected. Reconciliation must not submit again.
            bool serverApplied = false;
            var proof = new DailyBusinessProof("network.fixture", "event_battle", ["network.fixture"], (_, _, after) =>
                B(R(after, "network.fixture", "applied")) ? O(("confirmed", true)) : throw new InvalidDataException("not confirmed"));
            using var f = new WorkflowHarness(Path.Combine(root, "network-business"), [proof]);
            f.Readings = () => [WorkflowCases.Reading("network.fixture", ("applied", serverApplied))];
            f.Page("MenuUI", "_buttonMail");
            var op = f.Business.Create(f.Context, proof.Role, f.Evidence(), new(), O(("ui", "MenuUI"), ("field", "_buttonMail")));
            f.OnCommand = _ => { serverApplied = true; throw DailyNetworkRecovery.Interrupted(); };
            bool interrupted = false;
            try { await f.Business.CommitAsync(op, f.Context); } catch (StageHostException e) { interrupted = e.Kind == "network" && e.HoldQueue; }
            Check(interrupted && S(op["state"]) == "unknown" && f.Box.Commands.Count == 1, "network interruption preserves unknown dispatched operation and recovery signal");
            f.OnCommand = null;
            var reconciled = await f.Business.ReconcileAsync(f.Context);
            Check(reconciled["unresolved"]!.AsArray().Count == 0 && reconciled["completed"]!.AsArray().Count == 1 && f.Box.Commands.Count == 1,
                "restored authoritative progress settles original operation without another input");
        }
        foreach (string mode in new[] { "direct", "guarded", "false", "extra", "wrong-origin", "multicast" })
        {
            var (callback, app) = Callback(mode);
            Check(BackgroundErrorPolicy.RestartOnly(callback, app) == (mode is "direct" or "guarded"), "structural restart callback: " + mode);
        }
        {
            string storage = Path.Combine(root, "network-budget");
            string journal = Path.Combine(storage, "live", "diagnostics", "network-recovery", DailyIdentity.Hash(basic.Context["actor"]!.ToJsonString()) + ".json");
            DailyJson.Write(journal, O(("used", true), ("state", "paused")));
            observed.Frame["AtUtcTicks"] = DateTime.UtcNow.Ticks;
            observed.Daily!.FrameUtcTicks = DateTime.UtcNow.Ticks;
            DailyNetworkRecovery.BeginManualRun(storage, observed);
            Check(B(DailyJson.TryRead<JsonObject>(journal)!["used"]), "manual click while error remains cannot reset login budget");
            observed.Frame["Surfaces"] = new JsonArray();
            DailyNetworkRecovery.BeginManualRun(storage, observed);
            Check(!B(DailyJson.TryRead<JsonObject>(journal)!["used"]), "manual recovery to a healthy game permits a later explicit run");
            var archive = await DailyDiagnosticExport.CreateAsync(storage, Path.Combine(root, "network-diagnostics.zip"), "", "fixture");
            using var zip = System.IO.Compression.ZipFile.OpenRead(archive.Path);
            Check(zip.Entries.Any(e => e.FullName.Contains("/network-recovery/")), "recovery reason and used attempt are included in diagnostic export");
        }
        foreach (string language in new[] { "Request timed out", "连线请求超时", "連線請求超時" })
            Check(BackgroundErrorPolicy.RestartTimeout([language + "\nCLINET_NET_TIMEOUT\nNONE"]), "native timeout code independent of language: " + language);
        foreach (string code in new[] { "SESSION_DST", "RPE_NEON_API_ACCESS_TOKEN_EXPIRE", "CLIENT_LOGIC_ERROR", "WAIT_TIMEOUT", "CLINET_NET_TIMEOUT_UNRELATED" })
            Check(!BackgroundErrorPolicy.RestartTimeout(["error\n" + code]), "authentication/unrelated error never restarted: " + code);
    }
    private static (Delegate, Type) Callback(string mode)
    {
        var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("NetworkFixture" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run).DefineDynamicModule("main");
        var app = module.DefineType("AppManager", TypeAttributes.Public);
        var getter = app.DefineMethod("RenamedSingleton", MethodAttributes.Public | MethodAttributes.Static, app, Type.EmptyTypes);
        var code = getter.GetILGenerator(); code.Emit(OpCodes.Ldnull); code.Emit(OpCodes.Ret);
        var restart = app.DefineMethod("AppReStart", MethodAttributes.Public, typeof(void), Type.EmptyTypes); restart.GetILGenerator().Emit(OpCodes.Ret);
        Type appType = app.CreateType()!;
        var network = module.DefineType(mode == "wrong-origin" ? "Other.NetworkManager" : "BDNetwork.NetworkManager", TypeAttributes.Public);
        var closure = network.DefineNestedType("RenamedClosure", TypeAttributes.NestedPublic);
        closure.DefineDefaultConstructor(MethodAttributes.Public);
        var flag = closure.DefineField("RenamedFlag", typeof(bool), FieldAttributes.Public);
        var method = closure.DefineMethod("RenamedCallback", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        var il = method.GetILGenerator();
        if (mode is "guarded" or "false")
        {
            var target = il.DefineLabel(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, flag); il.Emit(OpCodes.Brtrue_S, target); il.Emit(OpCodes.Ret); il.MarkLabel(target);
        }
        if (mode == "extra") il.Emit(OpCodes.Nop);
        il.Emit(OpCodes.Call, appType.GetMethod("RenamedSingleton")!); il.Emit(OpCodes.Callvirt, appType.GetMethod("AppReStart")!); il.Emit(OpCodes.Ret);
        network.CreateType(); Type type = closure.CreateType()!; object targetObject = Activator.CreateInstance(type)!;
        type.GetField("RenamedFlag")!.SetValue(targetObject, mode != "false");
        var callback = type.GetMethod("RenamedCallback")!.CreateDelegate<Action>(targetObject);
        return (mode == "multicast" ? Delegate.Combine(callback, callback) : callback, appType);
    }
}
