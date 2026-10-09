using Dustweave;
using Dustweave.Desktop;
internal static class StartupCases
{
    public static async Task Run(string root, List<string> cases)
    {
        void Check(bool v, string m)
        {
            if (!v)
                throw new Exception(m);
        }
        void Case(string name, Action f)
        {
            f();
            cases.Add("startup: " + name);
        }
        async Task Async(string name, Func<Task> f)
        {
            await f();
            cases.Add("startup: " + name);
        }
        async Task Reject(Task t)
        {
            try
            {
                await t;
            }
            catch (TimeoutException) { return; }
            catch (InvalidOperationException) { return; }
            throw new Exception("Expected rejected startup");
        }
        string NewRoot() => Path.Combine(root, "startup-" + Guid.NewGuid().ToString("N"));
        Case("packaged CLI startup recovery resolves GUI package root", () =>
        {
            var dir = NewRoot();
            Directory.CreateDirectory(Path.Combine(dir, "connection"));
            File.WriteAllText(Path.Combine(dir, "connection", "Dustweave.Connection.exe"), "");
            File.WriteAllText(Path.Combine(dir, "Dustweave.exe"), "");
            var method = typeof(DailyGameHost).GetMethod("ResolvePackageDirectory", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            Check((string)method.Invoke(null, new object[] { Path.Combine(dir, "connection") })! == dir, "CLI root incorrect");
            Check((string)method.Invoke(null, new object[] { dir })! == dir, "GUI root incorrect");
        });
        (DemoEnvironment env, DailyCoordinator run, string path) Fixture(int timeout = 1000)
        {
            var path = NewRoot();
            var env = new DemoEnvironment(path) { TitleVisible = true };
            return (env, new(env, env, path, new()
            {
                PollInterval = TimeSpan.FromMilliseconds(2),
                LoginTimeout = TimeSpan.FromMilliseconds(timeout)
            }), path);
        }
        (DailySnapshot s, StartupPermit p) Input()
        {
            var env = new DemoEnvironment(NewRoot()) { TitleVisible = true };
            var s = env.ReadSnapshot()!;
            return (s, new()
            {
                Owner = "test",
                AccountKey = s.AccountKey,
                InstanceId = s.InstanceId,
                ProcessId = s.ProcessId,
                ProcessStartTicks = s.ProcessStartTicks,
                ExpiresUtcTicks = DateTime.UtcNow.AddSeconds(10).Ticks
            });
        }
        Case("ready matching title requests native click", () => { var f = Input(); Check(StartupPolicy.Decide(f.s, f.p, DateTime.UtcNow.Ticks) == "click_start", "no start"); });
        foreach (var condition in new[] { "old-frame", "wrong-runtime", "wrong-process", "reused-pid", "other-instance", "wrong-account", "missing-sdk", "no-owner", "expired", "unbounded", "unsupported", "no-title", "not-ready", "disabled", "modal", "already-clicked", "read-error" })
            Case("refuses " + condition, () =>
            {
                var f = Input();
                switch (condition)
                {
                    case "old-frame":
                        f.s.FrameUtcTicks -= TimeSpan.FromSeconds(10).Ticks;
                        break;
                    case "wrong-runtime":
                        f.s.Runtime = "old";
                        break;
                    case "wrong-process":
                        f.p.ProcessId++;
                        break;
                    case "reused-pid":
                        f.p.ProcessStartTicks++;
                        break;
                    case "other-instance":
                        f.p.InstanceId = "old";
                        break;
                    case "wrong-account":
                        f.p.AccountKey = DailyIdentity.MemberKey("999");
                        break;
                    case "missing-sdk":
                        f.s.AccountKey = "";
                        break;
                    case "no-owner":
                        f.p.Owner = "";
                        break;
                    case "expired":
                        f.p.ExpiresUtcTicks = 0;
                        break;
                    case "unbounded":
                        f.p.ExpiresUtcTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
                        break;
                    case "unsupported":
                        f.s.Startup.Supported = false;
                        break;
                    case "no-title":
                        f.s.Startup.Visible = false;
                        break;
                    case "not-ready":
                        f.s.Startup.Ready = false;
                        break;
                    case "disabled":
                        f.s.Startup.ClickEnabled = false;
                        break;
                    case "modal":
                        f.s.Startup.BlockReason = "login modal";
                        break;
                    case "already-clicked":
                        f.s.Startup.Attempted = true;
                        break;
                    case "read-error":
                        f.s.ErrorCode = "error";
                        break;
                }
                Check(StartupPolicy.Decide(f.s, f.p, DateTime.UtcNow.Ticks) != "click_start", condition);
            });
        Case("download can precede SDK identity", () => { var f = Input(); f.s.AccountKey = ""; f.s.Startup.Ready = false; f.s.Startup.DownloadVisible = true; f.s.Startup.DownloadReady = true; f.s.Startup.DownloadInstanceId = 99; Check(StartupPolicy.Decide(f.s, f.p, DateTime.UtcNow.Ticks) == "click_download", "download gated on player login"); });
        foreach (var condition in new[] { "disabled", "already-clicked", "modal", "wrong-account", "no-owner", "expired", "no-instance" })
            Case("download refuses " + condition, () =>
            {
                var f = Input();
                f.s.Startup.Ready = false;
                f.s.Startup.DownloadVisible = true;
                f.s.Startup.DownloadReady = true;
                f.s.Startup.DownloadInstanceId = 99;
                switch (condition)
                {
                    case "disabled":
                        f.s.Startup.DownloadReady = false;
                        break;
                    case "already-clicked":
                        f.s.Startup.DownloadAttempted = true;
                        break;
                    case "modal":
                        f.s.Startup.BlockReason = "unknown popup";
                        break;
                    case "wrong-account":
                        f.p.AccountKey = "other";
                        break;
                    case "no-owner":
                        f.p.Owner = "";
                        break;
                    case "expired":
                        f.p.ExpiresUtcTicks = 0;
                        break;
                    case "no-instance":
                        f.s.Startup.DownloadInstanceId = 0;
                        break;
                }
                Check(StartupPolicy.Decide(f.s, f.p, DateTime.UtcNow.Ticks) != "click_download", condition);
            });
        Case("a populated player DTO on title is not login completion", () => { var f = Input(); f.s.State = "identified"; var instance = new GameInstance(f.s.ProcessId, f.s.ProcessStartTicks, "fake"); var g = new DailyIdentityGuard(); Check(!g.Observe(f.s, instance, f.s.AccountKey, "", DateTimeOffset.UtcNow), "title accepted"); f.s.Sequence++; f.s.FrameUtcTicks++; Check(!g.Observe(f.s, instance, f.s.AccountKey, "", DateTimeOffset.UtcNow), "advancing title accepted"); });
        await Async("transient login credentials recover without restarting or clicking early", async () =>
        {
            var f = Fixture(); bool observedWaiting = false;
            f.run.Progress += p =>
            {
                if (p.State == "waiting_login") f.env.IncompleteCatalogReads = 3;
                if (p.State == "waiting_credentials")
                {
                    observedWaiting = true;
                    Check(f.env.TitleClicks == 0, "input granted before credentials recovered");
                    Check(DailyJson.TryRead<StartupPermit>(Path.Combine(f.path, "startup-permit.json"))!.Owner == "", "old permit retained");
                }
            };
            await f.run.ConnectCurrentAsync();
            Check(observedWaiting && f.env.TitleClicks == 1, "did not resume the same check");
            Check(new DailyProfiles(f.path).Read().Single().LastVerifiedUtc != null, "recovered identity not verified");
            Check(!f.env.Calls.Any(c => c == "close" || c.StartsWith("launch:") || c.StartsWith("save:")), "recovery changed account or process");
        });
        await Async("persistent missing credentials time out without claiming expired tokens", async () =>
        {
            var f = Fixture(60);
            f.run.Progress += p => { if (p.State == "waiting_login") f.env.IncompleteCatalogReads = int.MaxValue; };
            string error = "";
            try { await f.run.ConnectCurrentAsync(); } catch (TimeoutException ex) { error = ex.Message; }
            Check(error.Contains("不能判定"), "missing local state was not reported precisely");
            Check(f.env.TitleClicks == 0 && f.env.StartupRecoveryCalls == 0, "retried game input with unavailable credentials");
            Check(new DailyProfiles(f.path).Read().Count == 0, "missing credentials marked as success");
        });
        await Async("cancel while waiting for credentials remains responsive", async () =>
        {
            var f = Fixture();
            f.run.Progress += p =>
            {
                if (p.State == "waiting_login") f.env.IncompleteCatalogReads = int.MaxValue;
                if (p.State == "waiting_credentials") f.run.Stop();
            };
            await f.run.ConnectCurrentAsync();
            Check(f.env.TitleClicks == 0, "cancelled check clicked title");
        });
        await Async("credential recovery cannot switch into another account", async () =>
        {
            var f = Fixture();
            f.run.Progress += p =>
            {
                if (p.State == "waiting_login") f.env.IncompleteCatalogReads = 1;
                if (p.State == "waiting_credentials") f.env.ForcedKey = f.env.Accounts[1].AccountKey;
            };
            await Reject(f.run.ConnectCurrentAsync());
            Check(f.env.TitleClicks == 0, "recovery entered a different account");
            Check(new DailyProfiles(f.path).Read().Count == 0, "wrong account saved");
        });
        await Async("agreement wait survives login timeout then resumes without accepting terms", async () =>
        {
            var f = Fixture(200);
            f.env.TitleBlock = "请先处理启动页弹窗：AgreementPopupUI";
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.run.Progress += p => { if (p.Message.Contains(DailyLoginReadiness.AgreementMessage)) waiting.TrySetResult(); };
            var pending = f.run.ConnectCurrentAsync();
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Task.Delay(350);
            Check(!pending.IsCompleted, "human decision consumed authentication timeout");
            Check(f.env.StartupRecoveryCalls == 0 && f.env.TitleClicks == 0, "attempted to act on service agreement");
            f.env.TitleBlock = "";
            await pending;
            Check(f.env.TitleClicks == 1 && new DailyProfiles(f.path).Read().Count == 1, "did not resume after user confirmation");
        });
        await Async("agreement wait remains cancellable", async () =>
        {
            var f = Fixture(); f.env.TitleBlock = "请先处理启动页弹窗：AgreementPopupUI";
            f.run.Progress += p => { if (p.Message.Contains(DailyLoginReadiness.AgreementMessage)) f.run.Stop(); };
            await f.run.ConnectCurrentAsync();
            Check(f.env.TitleClicks == 0 && f.env.StartupRecoveryCalls == 0, "cancelled agreement caused input");
        });        await Async("download recovery hands ownership back before identity proof", async () => { var f = Fixture(); f.env.TitleBlock = "resource network error"; f.env.StartupRecoverySucceeds = true; await f.run.ConnectCurrentAsync(); Check(f.env.StartupRecoveryCalls == 1 && f.env.TitleClicks == 1, "recovery repeated or title not advanced"); Check(DailyJson.TryRead<StartupPermit>(Path.Combine(f.path, "startup-permit.json"))!.Owner == "", "recovery kept permit"); });
        await Async("unrecognized modal probes only once and never clicks", async () => { var f = Fixture(60); f.env.TitleBlock = "unknown"; await Reject(f.run.ConnectCurrentAsync()); Check(f.env.StartupRecoveryCalls == 1 && f.env.TitleClicks == 0, "unknown dialog retried"); });
        await Async("connect advances from title then validates player", async () => { var f = Fixture(); await f.run.ConnectCurrentAsync(); Check(f.env.TitleClicks == 1, "not exactly one click"); Check(new DailyProfiles(f.path).Read().Single().LastVerifiedUtc != null, "missing game identity proof"); Check(DailyJson.TryRead<StartupPermit>(Path.Combine(f.path, "startup-permit.json"))!.Owner == "", "kept startup permission"); });
        await Async("title that never leaves times out without repeated clicks", async () =>
        {
            var f = Fixture(70);
            f.env.HoldTitle = true;
            // Drive observer frames after the permit is granted instead of relying on a
            // second scheduler tick within 70 ms on a busy build machine.
            f.run.Progress += p => { if (p.State == "waiting_start") for (int i = 0; i < 10; i++) f.env.ReadSnapshot(); };
            await Reject(f.run.ConnectCurrentAsync());
            Check(f.env.TitleClicks == 1, "expected exactly one title click, got " + f.env.TitleClicks);
            Check(new DailyProfiles(f.path).Read().Count == 0, "premature success");
            Check(DailyJson.TryRead<StartupPermit>(Path.Combine(f.path, "startup-permit.json"))!.Owner == "", "timeout permission survived");
        });
        await Async("stop prevents later readiness from clicking", async () => { var f = Fixture(); f.env.TitleBlock = "modal"; var pending = f.run.ConnectCurrentAsync(); await Task.Delay(15); f.run.Stop(); await pending; f.env.TitleBlock = ""; f.env.ReadSnapshot(); Check(f.env.TitleClicks == 0, "clicked after stop"); });
        await Async("wrong SDK account does not enter or switch onward", async () => { var f = Fixture(); f.env.ForcedKey = DailyIdentity.MemberKey("999"); await Reject(f.run.InspectAsync(f.env.Accounts)); Check(f.env.TitleClicks == 0 && !f.env.Calls.Any(c => c.StartsWith("launch:")), "wrong account side effect"); });
        await Async("opening a coordinator does not grant startup", async () => { var f = Fixture(); await Task.Delay(5); f.env.ReadSnapshot(); Check(f.env.TitleClicks == 0 && !File.Exists(Path.Combine(f.path, "startup-permit.json")), "auto started"); });
        await Async("stop and startup renewal remain serialized", async () => { for (int i = 0; i < 20; i++) { var f = Fixture(); f.env.TitleBlock = "modal"; var pending = f.run.ConnectCurrentAsync(); await Task.Delay(5); f.run.Stop(); await pending; Check(DailyJson.TryRead<StartupPermit>(Path.Combine(f.path, "startup-permit.json"))!.Owner == "", "renewed after stop"); } });
    }
}
