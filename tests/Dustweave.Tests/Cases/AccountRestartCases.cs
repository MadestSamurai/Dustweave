using Dustweave;
using Dustweave.Desktop;
using BD2.LocalIpc;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
static class AccountRestartCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string label)
        {
            if (!ok)
                throw new Exception(label);
            cases.Add(label);
        }
        string root = Path.Combine(output, "restart-history"), a = new('a', 64), b = new('b', 64);
        var now = DateTime.UtcNow.Ticks;
        string Write(string id, string account, string state, long offset)
        {
            var path = DailyQueueEngine.RecordPath(root, id);
            DailyJson.Write(path, new JsonObject { ["schema"] = 1, ["id"] = id, ["state"] = "paused", ["context"] = new JsonObject { ["actor"] = new JsonArray(1, 2, "old-bridge", account, "player"), ["server"] = "server", ["cycle"] = "cycle" }, ["resetUtcTicks"] = now + TimeSpan.FromHours(8).Ticks, ["items"] = new JsonArray(new JsonObject { ["task"] = "mail", ["state"] = state }) });
            File.SetLastWriteTimeUtc(path, new DateTime(now + offset, DateTimeKind.Utc));
            return path;
        }
        string pa = Write(new('a', 32), a, "pending", -2000), pb = Write(new('b', 32), b, "blocked", -1000);
        DailyJson.Write(Path.Combine(root, "queue-ui.json"), new
        {
            account = b,
            record = pb
        });
        var session = new DailyQueueSession(root, new NoRun());
        var va = session.ReadView(a);
        var vb = session.ReadView(b);
        Check(va.Record == pa && vb.Record == pb && va.Account == a && vb.Account == b, "restart resolves each account history independently of global last queue");
        var restart = new DailyQueueSession(root, new NoRun());
        Check(restart.ReadView(a).Record == pa, "migrated account history survives desktop restart");
        Check(DailyQueueRetry.Validate(va, a, new(a, pa, ["mail"])).SequenceEqual(new[] { "mail" }), "same-cycle current-account unfinished work remains selectable after game restart");
        Check(session.ReadView(new('c', 64)).Record == "", "account without history does not inherit another account queue");
        bool rejected = false;
        try
        {
            DailyQueueRetry.Validate(vb, a, new(a, pb, ["mail"]));
        }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "cross-account retry remains rejected");
        string newer = Write(new('d', 32), a, "recovery_required", 1000);
        DailyQueueHistory.Remember(root, a, newer);
        Check(session.ReadView(a).Record == newer, "new per-account queue pointer replaces old migration index");
        DailyJson.Write(Path.Combine(root, "queue-history", a + ".json"), new
        {
            account = a,
            record = pb
        });
        Check(session.ReadView(a).Record == newer, "cross-account corrupted history pointer is rejected and rebuilt");
        string outside = Path.Combine(output, "outside-result.json");
        DailyJson.Write(Path.Combine(root, "queue-history", a + ".json"), new
        {
            account = a,
            record = outside
        });
        Check(session.ReadView(a).Record == newer, "external history path is rejected");
        var oldBytes = File.ReadAllBytes(pa);
        _ = session.ReadView(a);
        Check(oldBytes.SequenceEqual(File.ReadAllBytes(pa)), "history resolution preserves original business evidence");
        foreach (var scenario in new[] { "exited", "reused-pid", "new-process" })
        {
            string ipcRoot = Path.Combine(output, "restart-ipc-" + scenario);
            var env = new DemoEnvironment(ipcRoot);
            var old = new GameInstance(900001, 10, "old.exe");
            var client = DailyTransport.Bind(ipcRoot, old);
            typeof(PipeClient).GetField("ticket", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(client, "old-granted-ticket");
            env.Game = scenario == "exited" ? null : scenario == "reused-pid" ? old with
            {
                StartTicks = 11
            } : old with
            {
                ProcessId = 900002
            };
            var runner = new DailyCoordinator(env, env, ipcRoot);
            var timer = Stopwatch.StartNew();
            runner.Stop();
            Check(timer.ElapsedMilliseconds < 500, "revocation never waits on " + scenario + " game endpoint");
            if (env.Game != null)
                Check(!DesktopFiles.HasLease(ipcRoot), scenario + " cannot retain previous game writer ticket");
        }
        string aliveRoot = Path.Combine(output, "restart-alive");
        var alive = new GameInstance(Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks, "test.exe");
        var live = DailyTransport.Bind(aliveRoot, alive);
        typeof(PipeClient).GetField("ticket", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(live, "live-ticket");
        DailyTransport.Refresh(aliveRoot, alive);
        Check(DesktopFiles.HasLease(aliveRoot), "ordinary refresh does not revoke same-process writer");
        Check(!DailyTransport.EndpointReady(new(900003, 11, "missing.exe")), "absent runtime endpoint is observed without connection timeout");
        await Task.CompletedTask;
    }
    private class NoRun : IDailyQueueExecutor
    {
        public Task<int> PrepareAsync(Action<string> r) => throw new Exception("No game access"); public Task<int> ExecuteAsync(string root, string account, string output, string? resume, Action<string> report, bool syncCollection = false, IReadOnlyList<string>? tasks = null, string? retryOf = null) => throw new Exception("No game access");
    }
}
