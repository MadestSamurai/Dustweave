using System.Diagnostics;
using System.Text.Json;
using Dustweave;

static class ParallelProcessCases
{
    public static async Task Child(string directory)
    {
        var job = DailyJson.TryRead<DailyParallelJob>(Path.Combine(directory, "job.json"))!;
        var lease = new DailyParallelLease(job.Id, job.Account, DateTimeOffset.UtcNow);
        using var process = Process.GetCurrentProcess();
        while (true)
        {
            lease.Observe(DailyJson.TryRead<DailyParallelCommand>(Path.Combine(directory, "control.json")), DateTimeOffset.UtcNow);
            string state = lease.Stopped ? (lease.ParentLost ? "interrupted" : "stopped") : !lease.Authorized ? "connecting" : lease.Paused ? "paused" : "running";
            DailyJson.Write(Path.Combine(directory, "status.json"), new DailyParallelStatus(job.Id, job.Account, state, "synthetic worker", DateTimeOffset.UtcNow, process.Id, process.StartTime.ToUniversalTime().Ticks));
            if (lease.Stopped) return;
            await Task.Delay(50);
        }
    }

    public static async Task Run(string root, List<string> cases)
    {
        using var runtime = new LocalRuntime(Path.Combine(root, "parallel-process"));
        var session = new DailyParallelSession(Path.Combine(root, "parallel-process-host"), runtime);
        var accounts = new[] {
            new DailyAccount(1,"Synthetic A",new string('a',64),"",true,false,""),
            new DailyAccount(2,"Synthetic B",new string('b',64),"",true,false,"") };
        await session.StartAsync(accounts, new(true,2), _ => new());
        async Task Until(Func<bool> condition, string name)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(12);
            while (DateTimeOffset.UtcNow < deadline) { await session.TickAsync(); if (condition()) { cases.Add(name); return; } await Task.Delay(60); }
            throw new Exception(name + ": " + JsonSerializer.Serialize(session.Current));
        }
        await Until(() => session.Current!.Items.All(x => x.State == "running"), "actual two-process workers accept independent parent leases");
        var jobs = session.Current!.Items.Select(x => x.Job).ToArray();
        session.Control(accounts[0].AccountKey, "pause");
        await Until(() => session.Current!.Items[0].State == "paused" && session.Current.Items[1].State == "running", "actual worker pause leaves peer process running");
        session.Control(accounts[0].AccountKey, "resume");
        await Until(() => session.Current!.Items[0].State == "running" && runtime.Read(jobs[0])?.State == "running", "actual worker resumes after new command sequence");
        session.Control("", "stop");
        await Until(() => !session.HasWork, "actual worker processes acknowledge stop before queue releases capacity");
        foreach (var job in jobs) await runtime.ProcessFor(job).WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        cases.Add("all synthetic worker processes exit after stop without game processes");

        // A host crash stops renewing the lease. Keep real elapsed-time semantics.
        await session.StartAsync([accounts[0]], new(true,1), _ => new());
        await Until(() => session.Current!.Items[0].State == "running", "orphan scenario begins only after parent authorization");
        var orphan = session.Current!.Items[0].Job;
        await runtime.ProcessFor(orphan).WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(36));
        if (runtime.Read(orphan)?.State != "interrupted") throw new Exception("Orphan kept running after parent heartbeat stopped");
        cases.Add("actual orphan worker stops automatically when host heartbeat disappears");
    }

    private sealed class LocalRuntime(string root) : IDailyParallelRuntime, IDisposable
    {
        private readonly Dictionary<string,Process> processes = new();
        private string DirectoryFor(DailyParallelJob job) => DailyParallelRuntime.WorkerDirectory(root,job.Id);
        public Task StartAsync(DailyAccount account, DailyParallelJob job, CancellationToken token)
        {
            string directory = DirectoryFor(job); Directory.CreateDirectory(directory);
            DailyJson.Write(Path.Combine(directory,"job.json"),job);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Environment.CurrentDirectory };
            start.ArgumentList.Add("--parallel-worker-fixture");start.ArgumentList.Add(directory);
            lock(processes) processes.Add(job.Id,Process.Start(start)!);
            return Task.CompletedTask;
        }
        public Process ProcessFor(DailyParallelJob job) { lock(processes) return processes[job.Id]; }
        public DailyParallelStatus? Read(DailyParallelJob job) => DailyJson.TryRead<DailyParallelStatus>(Path.Combine(DirectoryFor(job),"status.json"));
        public bool Alive(DailyParallelStatus status)
        { lock(processes) return processes.TryGetValue(status.Id,out var process) && process.Id==status.ProcessId && !process.HasExited && process.StartTime.ToUniversalTime().Ticks==status.ProcessStartTicks; }
        public bool Busy(DailyParallelJob job) { lock(processes) return processes.TryGetValue(job.Id,out var process)&&!process.HasExited; }
        public void Send(DailyParallelJob job,string action,long sequence)
        {
            string directory=DirectoryFor(job);if(!Directory.Exists(directory))return;
            DailyJson.Write(Path.Combine(directory,"control.json"),new DailyParallelCommand(job.Id,job.Account,action,sequence,DateTimeOffset.UtcNow));
        }
        public void Dispose()
        { lock(processes) foreach(var process in processes.Values) { if(!process.HasExited)process.Kill(true);process.Dispose(); } }
    }
}
