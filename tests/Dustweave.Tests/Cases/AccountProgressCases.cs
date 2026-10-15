using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;

static class AccountProgressCases
{
    public static void Run(string root, List<string> checks)
    {
        void Check(bool yes, string message) { if (!yes) throw new Exception(message); checks.Add(message); }
        string host = Path.Combine(root, "progress-host"), box = Path.Combine(root, "progress-box"), account = new('a', 64), other = new('b', 64);
        // Account keys use the product's identity derivation, not arbitrary slot numbers.
        account = DailyIdentity.MemberKey("101"); other = DailyIdentity.MemberKey("102");
        var now = DateTimeOffset.UtcNow;
        string cycle = (now.UtcTicks + TimeSpan.FromDays(1).Ticks + TimeSpan.FromHours(9).Ticks).ToString();
        JsonObject Context(string player = "player", string server = "server") => O(("actor", new JsonArray(1, 2, "hook", account, player)), ("server", server), ("cycle", cycle));
        string Queue(string dir, string task, string state, DateTimeOffset at)
        {
            string path = DailyQueueEngine.RecordPath(dir, Guid.NewGuid().ToString("N"));
            DailyJson.Write(path, O(("schema", 1), ("state", state), ("context", Context()), ("items", new JsonArray(O(("task", task), ("state", state), ("finished", at.UtcTicks))))));
            File.SetLastWriteTimeUtc(path, at.UtcDateTime); DailyQueueHistory.Remember(dir, account, path); return path;
        }
        string Cache(string dir, long pack, string week = "500") => Path.Combine(dir, "live", "server-collection", account, week, pack + ".json");
        JsonObject Snapshot(long pack, double at, bool complete = true) => O(("schema", 1), ("source", "FieldObjectInfoResponse+MonsterInfoResponse"), ("account", account), ("player", "player"), ("at", at),
            ("network", O(("State", "ready"), ("Error", ""), ("Pack", pack), ("Week", 500), ("Now", 100), ("Maps", new JsonArray(pack * 10 + 1)),
            ("Drops", new JsonArray(O(("Id", 10), ("Map", pack * 10 + 1)))), ("Rewards", complete ? "[{\"id\":10}]" : "[]"), ("Targets", new JsonArray()), ("Monsters", "[]"))));
        string ordinaryQueue = Queue(host, "guild_checkin", "completed", now.AddMinutes(-4));
        Queue(box, "weekly_mainline", "completed", now.AddMinutes(-2));
        DailyJson.Write(Cache(box, 2), Snapshot(2, 200));
        DailyJson.Write(Cache(host, 3), Snapshot(3, 300));
        DailyJson.Write(Cache(box, 3), Snapshot(3, 100, false));
        DailyJson.Write(Path.Combine(box, "live", "business", "uncertain.json"), new { state = "unknown" });
        DailyJson.Write(Path.Combine(box, "connection.json"), new { ProcessId = 123 });
        DailyAccountProgress.Copy(box, host, account);
        var copied = DailyJson.TryRead<JsonObject>(Cache(host, 2))!;
        Check(S(copied["server"]) == "server", "legacy map snapshot imports only with account/player server identity established by its queue");
        Check(N(DailyJson.TryRead<JsonObject>(Cache(host, 3))!["at"]) == 300, "older sandbox snapshots cannot replace newer normal progress");
        var local = new QueueView("completed", "", ordinaryQueue, [new("guild_checkin", "completed", "")], account, new("server", cycle, "player"));
        var combined = DailyParallelProgress.Read(host, account, local, now);
        Check(combined.Stages.Any(s => s.Task == "guild_checkin") && combined.Stages.Any(s => s.Task == "weekly_mainline"), "ordinary and isolated task outcomes are combined for the same daily cycle");
        Check(!File.Exists(Path.Combine(host, "connection.json")) && !Directory.Exists(Path.Combine(host, "live", "business")), "progress exchange never copies connection ownership or pending consuming operations");
        Check(Directory.GetDirectories(Path.Combine(host, "live", "queues")).Length == 1, "sandbox queue does not become an ordinary resumable execution journal");
        DailyAccountProgress.Copy(host, box, account);
        Check(File.Exists(Cache(box, 3)) && N(DailyJson.TryRead<JsonObject>(Cache(box, 3))!["at"]) == 300, "normal-to-isolated exchange restores the latest map progress too");
        var boxView = DailyParallelProgress.Read(box, account, new("idle", "", "", [], account), now);
        Check(boxView.Stages.Any(s => s.Task == "guild_checkin"), "normal daily completion is visible inside the isolated account");
        var wrong = Snapshot(4, 500); wrong["account"] = other; DailyJson.Write(Cache(box, 4), wrong);
        var unowned = Snapshot(5, 500); unowned["player"] = "other-player"; DailyJson.Write(Cache(box, 5), unowned);
        var corrupt = Snapshot(6, 500); corrupt["network"]!["State"] = "failed"; DailyJson.Write(Cache(box, 6), corrupt);
        var foreign = Snapshot(3, 500); foreign["server"] = "other-server"; DailyJson.Write(Cache(box, 3), foreign);
        DailyAccountProgress.Copy(box, host, account);
        Check(!File.Exists(Cache(host, 4)) && !File.Exists(Cache(host, 5)) && !File.Exists(Cache(host, 6)), "foreign identities and failed server replies cannot become shared completion");
        Check(N(DailyJson.TryRead<JsonObject>(Cache(host, 3))!["at"]) == 300, "different servers never overwrite each other's snapshots");
        DailyAccountProgress.Copy(box, host, other);
        Check(!Directory.Exists(Path.Combine(host, "live", "server-collection", other)), "exchange never discovers other accounts by scanning roots");
        Check(DailyParallelProgress.Read(host, account, new("idle", "", "", [], account), now.AddDays(2)).Stages.Count == 0, "shared daily completion expires at the game reset");
        long unchanged = File.GetLastWriteTimeUtc(Cache(host, 2)).Ticks;
        DailyAccountProgress.Copy(box, host, account);
        Check(File.GetLastWriteTimeUtc(Cache(host, 2)).Ticks == unchanged, "unchanged snapshots are not rewritten during repeated exchange");
        string rerun = Queue(host, "weekly_mainline", "partial", now.AddMinutes(-1));
        DailyAccountProgress.Copy(box, host, account);
        var latest = new QueueView("partial", "", rerun, [new("weekly_mainline", "partial", "", FinishedAt: now.AddMinutes(-1))], account, new("server", cycle, "player"));
        var merged = DailyParallelProgress.Read(host, account, latest, now);
        Check(merged.PlanStages!.Single(s => s.Task == "weekly_mainline").State == "partial"
            && merged.PlanStages.Any(s => s.Task == "guild_checkin"), "older imported completion cannot hide a newer partial rerun or erase another completed task");
        DailyAccountProgress.Copy(host, box, account);
        Check(DailyParallelProgress.Read(box, account, new("idle", "", "", [], account), now).Stages.Single(s => s.Task == "weekly_mainline").State == "partial",
            "stage timestamps preserve the latest outcome in both directions");
        string unrelated = Queue(box, "mail", "completed", now);
        DailyAccountProgress.Copy(box, host, account);
        var combinedAgain = DailyParallelProgress.Read(host, account, latest, now);
        Check(combinedAgain.Stages.Count == 3 && combinedAgain.Stages.Single(s => s.Task == "weekly_mainline").State == "partial",
            "a later one-task queue preserves earlier per-task outcomes from either launch mode");
        File.SetLastWriteTimeUtc(rerun, now.AddMinutes(1).UtcDateTime);
        var carried = latest with { Stages = [new("weekly_mainline", "completed", "", Carried: true, FinishedAt: now.AddMinutes(-5))] };
        Check(DailyParallelProgress.Read(host, account, carried, now).PlanStages!.Single(s => s.Task == "weekly_mainline").State == "partial",
            "a newer queue file cannot hide newer per-stage outcomes with old carried results");
        var unstarted = latest with { Stages = [new("weekly_mainline", "pending", "准备中")] };
        Check(DailyParallelProgress.Read(host, account, unstarted, now).PlanStages!.Single(s => s.Task == "weekly_mainline").State == "partial",
            "new unstarted queue row cannot overwrite a previously observed task outcome");
        Queue(box, "mail", "pending", now.AddMinutes(2));
        DailyAccountProgress.Copy(box, host, account);
        Check(DailyParallelProgress.Read(host, account, new("idle", "", "", [], account), now).Stages.Single(s => s.Task == "mail").State == "completed",
            "later unstarted sandbox queue cannot erase completed ordinary progress");
        string unavailable = Path.Combine(root, "progress-summary-unavailable");
        string durable = Queue(unavailable, "mail", "completed", now);
        File.WriteAllText(Path.Combine(unavailable, "parallel-progress"), "blocked directory");
        DailyParallelProgress.Capture(unavailable, account);
        Check(File.Exists(durable) && File.Exists(Path.Combine(unavailable, "progress-sync-error.json")),
            "summary storage failure preserves the durable queue and reports diagnostics without stopping daily execution");
    }
}
