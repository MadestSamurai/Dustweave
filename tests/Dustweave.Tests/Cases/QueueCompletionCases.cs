using Dustweave;
using System.Text.Json;

static class QueueCompletionCases
{
    public static async Task Run(string output, List<string> checks)
    {
        string account = new('a', 64), other = new('b', 64);
        var result = new QueueView("completed", "done", "fixture", [new("mail", "completed", ""), new("mirror", "skipped", "")], account);
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        async Task Reject(Host host, DailyQueueCompletion? target, string label)
        {
            try { await DailyQueueCompletion.CloseAsync(host, target, account, result, false); }
            catch (InvalidOperationException) { Check(host.Closed == 0, label); return; }
            throw new Exception(label);
        }
        var root = Path.Combine(output, "completion-preferences"); Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "desktop-behavior.json"), "{\"CloseAction\":\"tray\",\"RunOnOpen\":true}");
        var old = DailyDesktopBehavior.Read(root);
        Check(!old.CloseGameAfterRun && old.RunOnOpen && old.CloseAction == "tray", "Old desktop preferences do not silently enable game close");
        (old with { CloseGameAfterRun = true }).Save(root);
        Check(DailyDesktopBehavior.Read(root).CloseGameAfterRun && new DailySchedulePlan().Execution.CloseGame == false,
            "Game close is opt-in and scheduled plans keep an independent choice");
        var host = new Host(account);
        var target = DailyQueueCompletion.Capture(host, account);
        Check(target?.Account == account, "Completion captures the verified game account and process");
        foreach (string state in new[] { "paused", "failed", "running", "recovery_required", "preparing" })
        {
            await DailyQueueCompletion.CloseAsync(host, target, account, result with { State = state }, false);
            Check(host.Closed == 0, "Completion preserves game on " + state);
        }
        foreach (var test in new[] { result with { Account = other }, result with { Expired = true }, result with { Stages = [] }, result with { Stages = [new("mail", "recovery_required", "")] } })
        {
            await DailyQueueCompletion.CloseAsync(host, target, account, test, false);
            Check(host.Closed == 0, "Invalid or unfinished queue cannot close a game");
        }
        await DailyQueueCompletion.CloseAsync(host, target, account, result, true);
        Check(host.Closed == 0, "Late user stop overrides successful completion");
        using (var stop = new CancellationTokenSource())
        {
            stop.Cancel();
            try { await DailyQueueCompletion.CloseAsync(host, target, account, result, false, stop.Token); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { Check(host.Closed == 0, "Cancelled close never sends a window command"); }
        }
        await Reject(host, null, "Missing initial process identity never authorizes game exit");
        var original = host.Game!;
        host.Game = original with { StartTicks = original.StartTicks + 1 };
        await Reject(host, target, "Reused PID with new creation time remains open"); host.Game = original;
        host.Snapshot.AccountKey = other;
        await Reject(host, target, "Changed account remains open"); host.Snapshot.AccountKey = account;
        host.Snapshot.PlayerKey = other;
        await Reject(host, target, "Changed game role remains open"); host.Snapshot.PlayerKey = new('c', 64);
        host.Snapshot.FrameUtcTicks = DateTime.UtcNow.AddMinutes(-1).Ticks;
        await Reject(host, target, "Stale observation does not close an unverified game"); host.Snapshot.FrameUtcTicks = DateTime.UtcNow.Ticks;
        host.Snapshot.InstanceId = "reconnected";
        await DailyQueueCompletion.CloseAsync(host, target, account, result, false);
        Check(host.Closed == 1 && host.Game == null, "Verified completion closes exact game even after bridge reconnect");
        await DailyQueueCompletion.CloseAsync(host, target, account, result, false);
        Check(host.Closed == 1, "Already exited game is a no-op");
        var first = new Host(account); var second = new Host(other);
        var secondTarget = DailyQueueCompletion.Capture(second, other);
        await DailyQueueCompletion.CloseAsync(first, DailyQueueCompletion.Capture(first, account), account, result, false);
        Check(first.Closed == 1 && second.Closed == 0, "Parallel account completion cannot close the other account");
        await DailyQueueCompletion.CloseAsync(second, secondTarget, other, result with { Account = other }, false);
        Check(second.Closed == 1, "Next sequential or parallel account closes using its own identity");
        var partial = new Host(account);
        var deferred = result with { State = "partial", Stages = [new("weekly_mainline", "partial", "absorption_exhausted"), new("event_battle", "partial", "event_auto_failed_swept"), new("mail", "completed", "")] };
        await DailyQueueCompletion.CloseAsync(partial, DailyQueueCompletion.Capture(partial, account), account, deferred, false);
        Check(partial.Closed == 1, "Finished queue with deferred collection or event fallback honors exit preference");
        var unsettled = new Host(account);
        foreach (string state in new[] { "pending", "running", "recovery_required" })
            await DailyQueueCompletion.CloseAsync(unsettled, DailyQueueCompletion.Capture(unsettled, account), account,
                deferred with { Stages = [new("mail", state, "")] }, false);
        Check(unsettled.Closed == 0, "Unsettled operations still preserve the exact game even in a partial queue");
        var timeout = new Host(account) { RemainOpen = true };
        try { await DailyQueueCompletion.CloseAsync(timeout, DailyQueueCompletion.Capture(timeout, account), account, result, false); throw new Exception("Unclosed game hidden"); }
        catch (InvalidOperationException e) { Check(e.Message == "completion.not_closed" && timeout.Closed == 1 && result.State == "completed", "Exit failure retains completed queue and never retries or kills"); }
    }
    private sealed class Host : IGameHost
    {
        public GameInstance? Game = new(123, 456, @"C:\Game\BrownDust II.exe");
        public DailySnapshot Snapshot;
        public int Closed;
        public bool RemainOpen;
        public Host(string account) => Snapshot = new() { AccountKey = account, PlayerKey = new('c', 64), ProcessId = 123, ProcessStartTicks = 456,
            InstanceId = "fixture", Sequence = 1, FrameUtcTicks = DateTime.UtcNow.Ticks, State = "identified" };
        public GameInstance? Find() => Game;
        public Task CloseAsync(GameInstance instance, CancellationToken cancellation)
        {
            if (instance != Game) throw new Exception("Wrong game");
            Closed++; if (!RemainOpen) Game = null; return Task.CompletedTask;
        }
        public DailySnapshot? ReadSnapshot() => Snapshot;
        public DailyRuntimeStatus? ReadStatus() => null;
        public Task ConnectAsync(GameInstance instance, Action<string> progress, CancellationToken cancellation) => throw new Exception("Unexpected connection");
    }
}
