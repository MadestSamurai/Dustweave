using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;

static class ObservationWaitCases
{
    public static async Task Run(string root, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        WorkflowHarness New(string name, DailyBusinessProof[]? proofs = null) => new(Path.Combine(root, "observation-wait-" + name), proofs ?? []);
        async Task<StageHostException> Failure(Func<Task> action)
        {
            try { await action(); } catch (StageHostException e) { return e; }
            throw new Exception("Expected bounded observation failure");
        }
        foreach (string gap in new[] { "old-request", "old-frame", "old-evidence", "io", "identity-pending" })
        using (var f = New(gap))
        {
            f.ChangeEvidence = e =>
            {
                if (f.Time >= 12) return;
                if (gap == "old-request") e["ObservationRequest"] = "previous";
                if (gap == "old-frame") e["Frame"]!["AtUtcTicks"] = f.Ticks - 4 * TimeSpan.TicksPerSecond;
                if (gap == "old-evidence") e["AtUtcTicks"] = f.Ticks - 4 * TimeSpan.TicksPerSecond;
                if (gap == "io") e["Error"] = "System.IO.IOException: temporarily locked";
                if (gap == "identity-pending") { e["Frame"]!["Error"] = "Waiting for fresh account identity"; e["Frame"]!["AccountKey"] = ""; e["Frame"]!["PlayerKey"] = ""; }
            };
            var fresh = await f.Driver.EvidenceAsync(["dispatch"]);
            Check(f.Time >= 12 && f.Time < 12.2 && f.Box.Commands.Count == 0 && DailyEvidence.SameActor(f.Frame, fresh["Frame"]!.AsObject()),
                "shared direct read survives reported twelve-second loading stall: " + gap);
            Check(f.Ticks - N(fresh["AtUtcTicks"]) <= 3 * TimeSpan.TicksPerSecond && f.Ticks - N(fresh["Frame"]!["AtUtcTicks"]) <= 3 * TimeSpan.TicksPerSecond,
                "longer readiness wait never accepts stale game data: " + gap);
        }
        using (var f = New("request-renewal"))
        {
            var ids = new HashSet<string>();
            f.ChangeEvidence = e => { ids.Add(S(e["ObservationRequest"])); if (f.Time < 35) e["ObservationRequest"] = "old"; };
            await f.Driver.EvidenceAsync(["navigation.pack"]);
            Check(f.Time >= 35 && ids.Count == 1 && f.Box.Commands.Count == 0,
                "slow producer retains one live request across multiple renewals without gameplay");
        }
        foreach (double deadline in new[] { 5d, 80d })
        using (var f = New("deadline-" + deadline))
        {
            f.ChangeEvidence = e => e["ObservationRequest"] = "unavailable";
            var failure = await Failure(() => f.Workflow.WaitEvidence(["mirror"], _ => true, deadline, "Waiting for battle"));
            Check(failure.ObservationTimedOut && failure.HoldQueue && f.Time >= deadline && f.Time < deadline + .2 && f.Box.Commands.Count == 0,
                "nested evidence uses exact enclosing deadline without resetting it: " + deadline);
            Check(DailyObservationWindow.Current == null, "wait budget is released after failure: " + deadline);
        }
        using (var f = New("nested-deadline"))
        {
            f.ChangeEvidence = e => e["ObservationRequest"] = "unavailable";
            using (DailyObservationWindow.Begin(5, () => f.Time))
            {
                var failure = await Failure(() => f.Workflow.WaitEvidence(["mail"], _ => true, 80));
                Check(failure.HoldQueue && f.Time >= 5 && f.Time < 5.2, "a long nested wait cannot extend its parent operation");
            }
            f.ChangeEvidence = null;
            double started = f.Time;
            await f.Driver.EvidenceAsync(["mail"]);
            Check(f.Time == started && DailyObservationWindow.Current == null, "fresh reads remain immediate after a previous expired wait");
        }
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int startedAccounts = 0;
        async Task<double> AccountBudget(double seconds)
        {
            using var own = DailyObservationWindow.Begin(seconds, () => 0);
            if (Interlocked.Increment(ref startedAccounts) == 2) bothStarted.SetResult();
            await bothStarted.Task;
            await Task.Yield();
            return DailyObservationWindow.Current!.Remaining;
        }
        var independent = await Task.WhenAll(AccountBudget(5), AccountBudget(80));
        Check(independent.SequenceEqual(new[] { 5d, 80d }) && DailyObservationWindow.Current == null,
            "concurrent accounts keep independent wait deadlines across asynchronous continuations");
        using (var f = New("native-batch"))
        {
            f.Page("BattleUI_EventBattle");
            f.Readings = () => [WorkflowCases.Reading("event.native", ("$self", O(("Result", f.Time >= 72), ("Continuing", f.Time < 72))))];
            f.ChangeEvidence = e => { if (f.Time >= 1 && f.Time < 71) e["ObservationRequest"] = "old"; };
            await DailyEventNative.WaitBatch(f.Workflow);
            Check(f.Time >= 72 && f.Time < 72.3 && f.Box.Commands.Count == 0,
                "native consecutive battle keeps its fifteen-minute owner through a seventy-second transition");
        }
        using (var f = New("cartridge"))
        {
            f.Page("GameFieldDefaultUI"); f.Frame["Scene"] = "Map1001_001";
            f.Readings = () => [WorkflowCases.Reading("navigation.pack", (DailyTravel.CurrentPack + ".Id", 1), (DailyTravel.CurrentPack + ".PackType", 1))];
            f.ChangeEvidence = e => { if (f.Time < 12) e["ObservationRequest"] = "old"; };
            Check(await DailyTravel.Reuse(f.Workflow, packId: 1) && f.Time >= 12 && f.Time < 14 && f.Box.Commands.Count == 0,
                "cartridge reuse waits for delayed destination evidence without re-entering the cartridge");
        }
        foreach (string outcome in new[] { "success", "timeout", "identity", "stop" })
        using (var f = New("submitted-" + outcome,
            [new DailyBusinessProof("test.claim", "mail", ["mail"], (_, _, _) => O(("verified", true)))]))
        {
            f.Page("MailUI", "_claim");
            var op = f.Business.Create(f.Context, "test.claim", f.Evidence(), new(), O(("ui", "MailUI"), ("field", "_claim")));
            f.OnCommand = _ => f.ChangeEvidence = e => { if (outcome != "success" || f.Time < 12) e["ObservationRequest"] = "old"; };
            f.OnDelay = () => { if (f.Time >= 1 && outcome == "identity") f.Context["cycle"] = "tomorrow"; if (f.Time >= 1 && outcome == "stop") f.Stopped = true; };
            if (outcome == "success")
            {
                await f.Business.CommitAsync(op, f.Context, 40);
                Check(S(op["state"]) == "completed" && f.Time >= 12 && f.Box.Commands.Count == 1,
                    "submitted claim waits for delayed proof without submitting the claim twice");
            }
            else
            {
                Exception? error = null;
                try { await f.Business.CommitAsync(op, f.Context.DeepClone().AsObject(), 4); } catch (Exception e) { error = e; }
                Check(error != null && S(op["state"]) == "unknown" && f.Box.Commands.Count == 1, "uncertain submitted operation remains recorded once: " + outcome);
                Check(outcome switch {
                    "timeout" => error is StageHostException { Kind: "pending", HoldQueue: true, ObservationTimedOut: true } && f.Time >= 4 && f.Time < 4.2,
                    "identity" => error is DailyStepException { Kind: "identity" } or StageHostException { Kind: "identity" },
                    _ => error is StageHostException { Kind: "stopped" }
                }, "read-only waiting preserves timeout, identity and stop semantics: " + outcome);
            }
        }
        using (var f = New("frozen-live-observation"))
        {
            var frame = CommandDriverCases.Frame(); frame["Protocol"] = 1; frame["BridgeVersion"] = DailyStageObservation.BridgeVersion;
            frame["AtUtcTicks"] = f.Ticks - 4 * TimeSpan.TicksPerSecond;
            var daily = new DailySnapshot { ProcessId = 7, ProcessStartTicks = 10, AccountKey = new('a', 64), PlayerKey = new('b', 64), State = "identified", FrameUtcTicks = f.Ticks, Guild = new() { ServerKey = "server", CycleKey = "today" } };
            var observation = new DailyStageObservation(daily.AccountKey, () => frame, () => daily, now: () => f.Ticks, timeout: TimeSpan.Zero,
                delay: t => { f.Time += t.TotalSeconds; if (f.Time >= 12) { frame["AtUtcTicks"] = f.Ticks; daily.FrameUtcTicks = f.Ticks; } return Task.CompletedTask; });
            using (DailyObservationWindow.Begin(30, () => f.Time)) await observation.ReadFrameAsync();
            Check(f.Time >= 12 && f.Time < 12.3, "shared live snapshot reader also inherits the operation deadline during loading");
        }
        using (var f = New("navigation-loading"))
        {
            f.Frame["Scene"] = "Empty"; f.Page("LoadingUI");
            var navigation = new DailyStageNavigation(() => Task.FromResult(new DailyStageFrame(f.Frame, f.Context)), () => false,
                clock: () => f.Time, delay: t => { f.Time += t.TotalSeconds; if (f.Time >= 40) f.Page("MenuUI"); return Task.CompletedTask; });
            var recovered = await navigation.RecoverAsync("mail", f.Context, "loading", (_, _, _) => throw new Exception("No clicks during loading"));
            Check(B(recovered["safe"]) && f.Time >= 40 && f.Time < 45 && f.Box.Commands.Count == 0,
                "shared recovery accepts a forty-second loading screen without dismissing it");
            f.Page("SubSkillCutSceneUI");
            Check(DailyHomeDecision.Inspect(f.Frame, DailyNavigationPolicy.Load()).Kind == "waiting", "skill presentation is a transient wait, never a missing-menu failure");
            f.Frame["Surfaces"]!.AsArray().Add(HomeNavigationCases.Surface("BattleUI_EventBattle", id: 5));
            Check(DailyHomeDecision.Inspect(f.Frame, DailyNavigationPolicy.Load()).Kind == "blocked", "generic navigation still cannot take over an active battle");
        }
    }
}
