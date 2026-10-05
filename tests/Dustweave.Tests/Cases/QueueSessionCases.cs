using BD2Daily;
static class QueueSessionCases
{
    public static async Task Run(string output, List<string> cases)
    {
        string account = new('a', 64);
        string NewRoot()
        {
            string p = Path.Combine(output, "queue-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(p);
            return p;
        }
        void Check(bool ok, string name)
        {
            if (!ok)
                throw new Exception(name);
            cases.Add(name);
        }
        var root = NewRoot();
        var fake = new FakeWorker();
        var session = new DailyQueueSession(root, fake);
        var result = await session.RunAsync(account);
        Check(result.State == "completed" && result.Stages.Count == 1, "queue renders actual durable stages");
        await session.RunAsync(account, true);
        Check(fake.Resume != null, "queue resumes recorded ID");
        await session.RunAsync(account, syncCollection: true);
        Check(fake.SyncCollection && fake.Resume == null, "manual sync uses dedicated worker mode");
        var prefsStore = new DailyPreferenceStore(root);
        prefsStore.Save(account, new DailyPreferences());
        string savedPreferences = File.ReadAllText(prefsStore.PathFor(account));
        await session.RunAsync(account, selection: new(account, ["mail", "equipment", "free_draws"]));
        Check(fake.Resume == null && fake.RetryOf == null && fake.Tasks!.SequenceEqual(new[] { "free_draws", "equipment", "mail" }), "fresh selection sends only chosen tasks in dependency order without old record");
        Check(savedPreferences == File.ReadAllText(prefsStore.PathFor(account)), "one-run selection never changes saved preferences");
        async Task RejectPlan(QueuePlanRequest plan, string label, bool resume = false, bool sync = false, QueueRetryRequest? retry = null)
        {
            int before = fake.Preparations;
            bool rejected = false;
            try
            {
                await session.RunAsync(account, resume, sync, retry, plan);
            }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && fake.Preparations == before, label);
        }
        await RejectPlan(new(account, []), "empty plan cannot fall back to full daily run");
        await RejectPlan(new(account, ["mail", "mail"]), "fresh plan rejects duplicate tasks before connection");
        await RejectPlan(new(account, ["unknown"]), "fresh plan rejects unknown task before connection");
        await RejectPlan(new(account, ["collection_sync"]), "fresh plan cannot request manual inspection implicitly");
        await RejectPlan(new(account, ["trade"]), "fresh plan cannot silently enable disabled spending stage");
        await RejectPlan(new(new string('b', 64), ["mail"]), "fresh plan rejects account change before connection");
        await RejectPlan(new(account, ["mail"]), "fresh plan cannot be combined with resume", resume: true);
        await RejectPlan(new(account, ["mail"]), "fresh plan cannot be combined with manual sync", sync: true);
        await RejectPlan(new(account, ["mail"]), "fresh plan cannot be combined with historical retry", retry: new(account, "fixture", ["mail"]));
        await RejectPlan(new(account, ["weekly_steal"]), "disabled theft cannot be enabled by a stale one-run plan");
        prefsStore.Save(account, new DailyPreferences { Weekly = new() { Steal = true, Npc = true } });
        savedPreferences = File.ReadAllText(prefsStore.PathFor(account));
        await session.RunAsync(account, selection: new(account, ["weekly_steal", "weekly_npc"]));
        Check(fake.Tasks!.SequenceEqual(new[] { "weekly_npc", "weekly_steal" }) && savedPreferences == File.ReadAllText(prefsStore.PathFor(account)), "weekly selection keeps independent choices and dependency order");
        Check(DailyStageCatalog.All.Any(s => s.Id == "weekly_steal") && !DailyStageCatalog.Enabled("weekly_steal", new DailyPreferences()) && DailyStageCatalog.Name("weekly_steal") == "每周偷窃", "theft is a normal opt-in stage");
        var source = Path.Combine(root, "live", "queues", new string('c', 32), "result.json");
        DailyJson.Write(source, new
        {
            state = "paused",
            items = new[] { new { task = "mirror", state = "completed" }, new { task = "daily_dispatch", state = "pending" }, new { task = "trade", state = "blocked" }, new { task = "mail", state = "pending" } }
        });
        DailyJson.Write(session.LastOutput, new
        {
            account,
            record = source
        });
        var request = new QueueRetryRequest(account, source, ["mail", "daily_dispatch"]);
        await session.RunAsync(account, retry: request);
        Check(fake.Resume == null && fake.RetryOf == new string('c', 32) && fake.Tasks!.SequenceEqual(new[] { "daily_dispatch", "mail" }), "selective retry starts new ordered queue and retains source");
        async Task RejectRetry(string target, QueueRetryRequest selection, string label)
        {
            int before = fake.Executions;
            bool rejected = false;
            try
            {
                await session.RunAsync(target, retry: selection);
            }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && fake.Executions == before, label);
        }
        await RejectRetry(account, request, "stale selection rejected before preparation");
        DailyJson.Write(session.LastOutput, new
        {
            account,
            record = source
        });
        await RejectRetry(new string('b', 64), request, "retry rejects another account");
        await RejectRetry(account, request with
        {
            Tasks = ["mirror"]
        }, "completed stage cannot be selected for retry");
        await RejectRetry(account, request with
        {
            Tasks = []
        }, "empty retry never becomes whole daily run");
        await RejectRetry(account, request with
        {
            Tasks = ["mail", "mail"]
        }, "duplicate retry stage rejected");
        await RejectRetry(account, request with
        {
            Tasks = ["unknown"]
        }, "unknown retry stage rejected");
        var uncertain = new QueueStage("mail", "recovery_required", "");
        Check(DailyQueueRetry.CanSelect(uncertain), "uncertain settlement can be selected for reconciliation before continuation");
        Check(DailyQueueRetry.CanSelect(new QueueStage("square", "recovery_required", "")), "square permits native-state recovery after component upgrade");
        fake.FailStartup = true;
        bool startupFailed = false;
        try
        {
            await session.RunAsync(account, retry: request);
        }
        catch (InvalidOperationException e) { startupFailed = e.Message.Contains("test startup rejected"); }
        Check(startupFailed && session.ReadView().Record == source, "failed selected startup preserves prior report and actionable error");
        fake.FailStartup = false;
        var hold = new TaskCompletionSource<int>();
        fake = new FakeWorker { Prepare = () => hold.Task };
        session = new(root, fake);
        var running = session.RunAsync(account);
        session.Stop();
        hold.SetResult(0);
        result = await running;
        Check(fake.Executions == 0 && result.State == "paused", "stop during preparation never starts worker");
        root = NewRoot();
        fake = new FakeWorker { WaitForStop = true };
        session = new(root, fake);
        running = session.RunAsync(account);
        await fake.Started.Task;
        session.Stop();
        await running;
        Check(File.Exists(Path.Combine(root, "queue-stop")) && !File.Exists(Path.Combine(root, "live", "pause")), "stop durable marker survives worker startup race");
        bool failed = false;
        try
        {
            await session.RunAsync(new string('b', 64), true);
        }
        catch (InvalidOperationException) { failed = true; }
        Check(failed, "resume rejects another account before worker");
        root = NewRoot();
        fake = new FakeWorker { Prepare = () => Task.FromResult(2) };
        session = new(root, fake);
        failed = false;
        try
        {
            await session.RunAsync(account);
        }
        catch (InvalidOperationException) { failed = true; }
        Check(failed && fake.Executions == 0, "failed preparation dispatches no daily action");
        root = NewRoot();
        DailyJson.Write(Path.Combine(root, "queue-ui.json"), new
        {
            record = Path.Combine(output, "external.json"),
            account
        });
        session = new(root, new FakeWorker());
        Check(session.ReadView().Record == "", "queue viewer does not follow external file paths");
    }
    private sealed class FakeWorker : IDailyQueueExecutor
    {
        public Func<Task<int>> Prepare = () => Task.FromResult(0); public int Executions, Preparations; public string? Resume; public bool WaitForStop, SyncCollection, FailStartup;
        public IReadOnlyList<string>? Tasks; public string? RetryOf;
        public TaskCompletionSource<bool> Started = new();
        public Task<int> PrepareAsync(Action<string> report)
        {
            Preparations++;
            return Prepare();
        }
        public async Task<int> ExecuteAsync(string root, string account, string output, string? resume, Action<string> report, bool syncCollection = false, IReadOnlyList<string>? tasks = null, string? retryOf = null)
        {
            Executions++;
            Tasks = tasks;
            RetryOf = retryOf;
            Resume = resume;
            SyncCollection = syncCollection;
            Started.TrySetResult(true);
            if (FailStartup)
            {
                DailyJson.Write(output, new
                {
                    account,
                    record = "",
                    state = "preparing"
                });
                report("test startup rejected");
                return 2;
            }
            if (WaitForStop)
            {
                var end = DateTime.UtcNow.AddSeconds(3);
                while (!File.Exists(Path.Combine(root, "queue-stop")))
                {
                    if (DateTime.UtcNow > end)
                        throw new Exception("test stop timeout");
                    await Task.Delay(10);
                }
            }
            string id = resume ?? Guid.NewGuid().ToString("N"), record = Path.Combine(root, "live", "queues", id, "result.json");
            DailyJson.Write(record, new
            {
                state = "completed",
                items = new[] { new { task = "mail", state = "completed" } }
            });
            DailyJson.Write(output, new
            {
                account,
                record,
                state = "completed"
            });
            return 0;
        }
    }
}
