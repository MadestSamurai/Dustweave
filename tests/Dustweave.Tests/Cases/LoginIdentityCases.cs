using Dustweave;
using Dustweave.Desktop;
using System.Text.Json;
internal static class LoginIdentityCases
{
    internal static async Task Run(string root, List<string> cases)
    {
        void Check(bool ok,string name){if(!ok)throw new Exception(name);cases.Add("login identity: "+name);}
        Check(!DailyDesktopLaunch.NeedsRelay([]),"ordinary GUI launch works without Explorer COM");
        Check(!DailyDesktopLaunch.NeedsRelay(["--inspect-account",new string('a',64)]),"login-check window does not depend on Explorer COM");
        Check(!DailyDesktopLaunch.NeedsRelay([DailyDesktopLaunch.ChildSwitch]),"desktop child does not loop");
        Check(DailyDesktopLaunch.Normalize(DailyDesktopLaunch.ChildArguments([])).Length==0,"normal window never acquires an automatic task");
        Check(DailyDesktopLaunch.Normalize(DailyDesktopLaunch.ChildArguments(["--inspect-account","key"])).SequenceEqual(new[]{"--inspect-account","key"}),"explicit target survives relay");
        foreach(var mode in new[]{"--smoke","--utility","--check-suite","--daily-connect-elevated"}) Check(!DailyDesktopLaunch.NeedsRelay([mode,"fixture"]),"background entry remains unchanged "+mode);
        Check(DailyDesktopLaunch.Quote("a b")=="\"a b\"","desktop argument quoting preserves spaces");
        Check(!DailyDesktopLaunch.NeedsRelay(["--run-selected", "fixture"]), "selected queue keeps its process context");
        Check(DailyDesktopLaunch.NeedsRelay(["--launch-desktop"]), "explicit developer desktop relay remains available");
        Check(!DailyDesktopLaunch.NeedsRelay(["--scheduled", "fixture"]), "scheduled queue is not relayed");
        string diagnosticRoot = Path.Combine(root, "early-startup");
        string state = StartupDiagnostics.Save(diagnosticRoot, "managed-entry", null);
        Check(File.Exists(state), "records startup before window creation");
        string failure = StartupDiagnostics.Save(diagnosticRoot, "application-resources", new InvalidOperationException("fixture resource load failed"));
        using (var report = JsonDocument.Parse(File.ReadAllText(failure)))
        {
            Check(report.RootElement.GetProperty("stage").GetString() == "application-resources", "early failure retains failing stage");
            Check(report.RootElement.GetProperty("error").GetString()!.Contains("fixture resource load failed"), "early failure retains diagnostic");
            Check(!report.RootElement.TryGetProperty("args", out _) && !report.RootElement.TryGetProperty("environment", out _), "startup report excludes arguments and environment");
        }
        string blockedRoot = Path.Combine(root, "startup-root-is-file");
        File.WriteAllText(blockedRoot, "fixture");
        Check(StartupDiagnostics.Save(blockedRoot, "managed-entry", null) == "", "unwritable diagnostic location never prevents startup");
        Check(StartupDiagnostics.Save(blockedRoot, "main-window", new IOException("fixture")) == "", "failed diagnostic write never hides original failure");
        foreach(bool title in new[]{true,false})
        {
            string path=Path.Combine(root,"login-store-"+title);var env=new DemoEnvironment(path){TitleVisible=title};
            var target=env.Accounts[0];var actual=env.Accounts[1];env.ForcedKey=actual.AccountKey;
            var coordinator=new DailyCoordinator(env,env,path,new DailyOptions{PollInterval=TimeSpan.FromMilliseconds(1),LoginTimeout=TimeSpan.FromSeconds(1)});
            string error="";try{await coordinator.ConnectCurrentAsync();}catch(InvalidOperationException ex){error=ex.Message;}
            Check(error.Contains(target.Name)&&error.Contains(actual.Name),"mismatch names both accounts at "+title);
            Check(!env.Calls.Any(c=>c is "startup.click" or "close"||c.StartsWith("launch:")||c.StartsWith("save:")),"mismatch performs no account switch or game action at "+title);
            Check(new DailyProfiles(path).Read().Count==0,"mismatch never binds wrong player at "+title);
            using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(path,"login-identity-error.json")));
            Check(doc.RootElement.GetProperty("classification").GetString()=="tool_game_session_disagreement","store disagreement diagnostic at "+title);
            Check(doc.RootElement.GetProperty("actual").GetProperty("AccountKey").GetString()==actual.AccountKey,"actual hash recorded at "+title);
            Check(!doc.RootElement.TryGetProperty("token",out _),"diagnostic stores no authentication material at "+title);
        }
        {
            string path=Path.Combine(root,"wrong-target-diagnostic");var env=new DemoEnvironment(path);var target=env.Accounts[1];var snapshot=env.ReadSnapshot()!;
            var ex=DailyLoginFailure.Mismatch(path,"fixture",target,snapshot,env.Read(),false);
            using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(path,"login-identity-error.json")));
            Check(doc.RootElement.GetProperty("classification").GetString()=="different_logged_in_account","wrong selection distinct from different stores");
            Check(!ex.Message.Contains("资源管理器"),"ordinary wrong selection not blamed on launch environment");
        }
        foreach (bool memberAvailable in new[] { true, false })
        {
            string path = Path.Combine(root, "incomplete-local-login-" + memberAvailable);
            var env = new DemoEnvironment(path) { IncompleteCatalogReads = int.MaxValue };
            string actual = env.CurrentKey;
            if (!memberAvailable) { env.ForcedKey = actual; env.CurrentKey = ""; }
            var coordinator = new DailyCoordinator(env, env, path, new DailyOptions { PollInterval = TimeSpan.FromMilliseconds(2), LoginTimeout = TimeSpan.FromSeconds(1) });
            await coordinator.ConnectCurrentAsync();
            Check(new DailyProfiles(path).Read().Single().AccountKey == actual, "live login is verified without reusable credentials " + memberAvailable);
            Check(env.Calls.Count(c => c == "connect") == 1, "identity discovery connects once " + memberAvailable);
            Check(!env.Calls.Any(c => c is "startup.click" or "close" || c.StartsWith("launch:") || c.StartsWith("save:")), "incomplete login verification never changes credentials or game " + memberAvailable);
            Check(!env.Read().SessionComplete, "live verification does not manufacture saved session " + memberAvailable);
            using var proof = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "live", "diagnostics", "login-verified.json")));
            Check(!proof.RootElement.GetProperty("SessionComplete").GetBoolean() && proof.RootElement.GetProperty("observed").GetProperty("identityReady").GetBoolean(), "diagnostics separate saved login from live identity " + memberAvailable);
            bool saveRejected = false;
            try { DailyAccountIdentity.SavePlan(env.Read() with { GameRunning = false, CurrentKey = actual }); } catch (InvalidOperationException) { saveRejected = true; }
            Check(saveRejected, "incomplete local session remains unsavable " + memberAvailable);
            if (memberAvailable)
            {
                await coordinator.InspectAsync([env.Accounts[0]]);
                Check(!env.Calls.Any(c => c == "close" || c.StartsWith("launch:")), "checking the already active account does not require launch credentials");
                bool switchingBlocked = false;
                try { await coordinator.InspectAsync([env.Accounts[1]]); } catch (InvalidOperationException) { switchingBlocked = true; }
                Check(switchingBlocked && !env.Calls.Any(c => c == "close" || c.StartsWith("launch:")), "incomplete session cannot be discarded to switch accounts");
            }
            env.Calls.Clear();
            await CurrentGameObservation.ConnectAsync(env, env, _ => { }, CancellationToken.None, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(2));
            Check(env.Calls.SequenceEqual(new[] { "connect" }), "read-only observation works without owning accounts " + memberAvailable);
        }
        foreach (bool title in new[] { false, true })
        {
            string path = Path.Combine(root, "missing-live-login-" + title);
            var env = new DemoEnvironment(path) { CurrentKey = "", Ready = title, TitleVisible = title, IncompleteCatalogReads = int.MaxValue };
            env.ForcedKey = env.Accounts[0].AccountKey;
            var coordinator = new DailyCoordinator(env, env, path, new DailyOptions { PollInterval = TimeSpan.FromMilliseconds(2), LoginTimeout = TimeSpan.FromMilliseconds(40) });
            string error = "";
            try { await coordinator.ConnectCurrentAsync(); } catch (TimeoutException ex) { error = ex.Message; }
            Check(error == "connection.login_identity_unavailable", "missing live identity has actionable diagnosis " + title);
            Check(new DailyProfiles(path).Read().Count == 0 && env.TitleClicks == 0, "title or missing frames never authorize account " + title);
            Check(File.Exists(Path.Combine(path, "live", "diagnostics", "login-failed.json")), "failed discovery retains bounded evidence " + title);
        }
        {
            string path = Path.Combine(root, "cancel-discovery"); var env = new DemoEnvironment(path) { CurrentKey = "", Ready = false };
            var coordinator = new DailyCoordinator(env, env, path);
            coordinator.Progress += p => { if (p.State == "waiting_login") coordinator.Stop(); };
            await coordinator.ConnectCurrentAsync();
            Check(new DailyProfiles(path).Read().Count == 0 && env.TitleClicks == 0, "identity discovery stops immediately without input");
        }
        {
            var env = new DemoEnvironment(Path.Combine(root, "identity-cache"));
            var live = new DailyLiveAccountIdentity(); var first = env.ReadSnapshot()!;
            Check(live.Resolve(env.Game, first, DateTimeOffset.UtcNow) == "", "one frame cannot supply missing local identity");
            Check(live.Resolve(env.Game, first, DateTimeOffset.UtcNow) == "", "repeated frame cannot confirm identity");
            var second = env.ReadSnapshot()!;
            Check(live.Resolve(env.Game, second, DateTimeOffset.UtcNow) == env.CurrentKey, "two advancing frames supply missing local identity");
            Check(live.Resolve(env.Game, second, DateTimeOffset.UtcNow) == env.CurrentKey, "fresh duplicate preserves already verified identity");
            Check(live.Resolve(env.Game, second, DateTimeOffset.UtcNow.AddSeconds(6)) == "", "stale identity is never reused");
            live.Resolve(env.Game, env.ReadSnapshot(), DateTimeOffset.UtcNow);
            Check(live.Resolve(env.Game, env.ReadSnapshot(), DateTimeOffset.UtcNow) == env.CurrentKey, "fresh frames restore identity after staleness");
            env.ForcedKey = env.Accounts[1].AccountKey;
            Check(live.Resolve(env.Game, env.ReadSnapshot(), DateTimeOffset.UtcNow) == "", "account change invalidates prior proof");
            Check(live.Resolve(env.Game, env.ReadSnapshot(), DateTimeOffset.UtcNow) == env.ForcedKey, "new account needs its own advancing proof");
            Check(live.Resolve(null, env.ReadSnapshot(), DateTimeOffset.UtcNow) == "", "closed game removes volatile identity");
            var wrongProcess = env.ReadSnapshot()!; wrongProcess.ProcessStartTicks--;
            Check(live.Resolve(env.Game, wrongProcess, DateTimeOffset.UtcNow) == "", "wrong game start time is not an identity source");
        }
    }
}
