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
        var stampRoot = NewRoot();
        var stampSession = new DailyQueueSession(stampRoot, new FakeWorker());
        var stampPath = Path.Combine(stampRoot, "live", "queues", new string('d', 32), "result.json");
        var finished = new DateTimeOffset(2026, 10, 6, 11, 2, 37, TimeSpan.Zero);
        DailyJson.Write(stampPath, new { state = "completed", items = new object[] {
            new { task = "guild", state = "completed", finished = finished.Ticks, carried_forward = true },
            new { task = "mail", state = "completed" },
            new { task = "mirror", state = "skipped", finished = (object?)null },
            new { task = "hunting", state = "completed", finished = "not-a-time" },
            new { task = "equipment", state = "completed", finished = long.MaxValue },
            new { task = "trade", state = "completed", finished = -1L },
            new { task = "free_draws", state = "completed", finished = 1.5 }
        }});
        DailyJson.Write(stampSession.LastOutput, new { account, record = stampPath });
        var stampView = stampSession.ReadView();
        Check(stampView.Stages[0].FinishedAt == finished && stampView.Stages[0].FinishedAt!.Value.Offset == TimeSpan.Zero,
            "queue exposes durable per-stage completion time as UTC");
        Check(stampView.Stages[0].Carried && new DailyQueueSession(stampRoot, new FakeWorker()).ReadView().Stages[0].FinishedAt == finished,
            "carried completion time survives a fresh viewer without using refresh time");
        Check(stampView.Stages.Skip(1).All(s => s.FinishedAt == null),
            "missing null malformed and out-of-range completion times do not break the queue or fabricate a date");
        var detailRoot = NewRoot();
        var detailSession = new DailyQueueSession(detailRoot, new FakeWorker());
        var detailPath = Path.Combine(detailRoot, "live", "queues", new string('e', 32), "result.json");
        DailyJson.Write(detailPath, new { state = "partial", items = new object[] {
            new { task = "weekly_fishing", state = "skipped", error = "", result = new { detail = "", reason = "weekly_fishing_complete" }, progress = new { detail = "准备中" } },
            new { task = "weekly_book", state = "skipped", error = (string?)null, result = new { detail = (string?)null, reason = "本周末日之书任务已完成" } },
            new { task = "trade", state = "completed", result = new { detail = "结果摘要", reason = "reason_code" }, progress = new { detail = "准备中" } },
            new { task = "mail", state = "skipped", result = (object?)null, progress = new { detail = "准备中" } },
            new { task = "hunting", state = "running", result = new { reason = "old_result" }, progress = new { detail = "正在狩猎" } },
            new { task = "room", state = "blocked", error = "具体错误", result = new { detail = "旧结果" } },
            new { task = "weekly_mainline", state = "partial", result = new { detail = "", waiting_daily_reset = true, reason = "limit_reached" }, progress = new { detail = "准备中" } }
        }});
        DailyJson.Write(detailSession.LastOutput, new { account, record = detailPath });
        var details = detailSession.ReadView().Stages;
        Check(details[0].Detail == "weekly_fishing_complete", "empty error/detail and stale progress do not hide the final skip reason");
        Check(details[1].Detail == "本周末日之书任务已完成", "null optional fields do not hide the skip explanation");
        Check(details[2].Detail == "结果摘要", "completed stage keeps its explicit result summary");
        Check(details[3].Detail == "", "missing skip reason is not replaced by stale running progress");
        Check(details[4].Detail == "正在狩猎", "active stage uses current progress instead of a previous result");
        Check(details[5].Detail == "具体错误", "failure explanation retains the actual error");
        Check(details[6].Detail == "吸收次数用完，次日接续未完成地图", "reset-wait explanation takes precedence over stale progress");
        DailyJson.Write(detailPath, System.Text.Json.Nodes.JsonNode.Parse("""
        {"state":"completed","items":[{"task":"rewards","state":"completed","result":{
          "reason":"奖励检查完成；仍有未完成任务",
          "pending_tasks":[
            {"group":"weekly","id":201,"title":"每日登录","progress":2,"required":5,"status":"pending"},
            {"key":"MG_WEEKLY:228","id":228,"title":"向女神像许愿","progress":2,"required":3,"status":"pending"},
            {"pass_id":7,"event_id":91,"title":"通行证目标","progress":10,"required":10,"status":"claimable"},
            {"group":"event","id":201,"title":"活动目标","progress":1,"required":4,"status":"pending"},
            {"group":"daily","id":201,"title":"每日登录","progress":0,"required":1,"status":"pending"},
            {"group":"daily","title":"已领取","status":"claimed"},
            null,23,
            {"title":null,"progress":"not-a-number","required":null,"status":"locked"}
          ],"stages":{"passes":{"reports":[{"selected_pass_id":7,"pass_title":"回归通行证"}]}}
        }},{"task":"event_rewards","state":"completed","result":{"pending_tasks":null}}]}
        """));
        string beforeDetails = File.ReadAllText(detailPath);
        var pendingView=detailSession.ReadView();
        var pendingTasks=pendingView.Stages[0].PendingTasks!;
        Check(pendingTasks.Count==6 && pendingTasks[0].Title=="每日登录" && pendingTasks[0].Progress==2 && pendingTasks[0].Required==5,
            "viewer exposes saved task names and actual progress while excluding claimed rows");
        Check(pendingTasks[1].Group=="weekly" && pendingTasks[2].Group=="pass" && pendingTasks[2].Source=="回归通行证" && pendingTasks[2].Status=="claimable",
            "viewer identifies weekly and pass tasks with source title and reward status");
        Check(pendingTasks.Count(t=>t.Title=="每日登录")==2 && pendingTasks[3].Group=="event" && pendingTasks[4].Group=="daily",
            "same task ID or title in different categories is not merged");
        Check(pendingTasks[5].Progress==null && pendingTasks[5].Required==null && pendingTasks[5].Status=="locked"
            && pendingView.Stages[1].PendingTasks!.Count==0, "missing or malformed details remain unknown without breaking the timeline");
        Check(File.ReadAllText(detailPath)==beforeDetails,"opening task details does not mutate the saved execution record");
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
