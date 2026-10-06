using Dustweave;
using System.Collections.Concurrent;
using System.Diagnostics;

static class QueueExecutionThreadCases
{
    public static async Task Run(string root, List<string> cases)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var context = new PumpContext();
                SynchronizationContext.SetSynchronizationContext(context);
                var directory = Path.Combine(root, "queue-ui-isolation");
                Directory.CreateDirectory(directory);
                var worker = new SynchronousWorker();
                var session = new DailyQueueSession(directory, worker);
                int uiThread = Environment.CurrentManagedThreadId;
                bool wrongProgressThread = false;
                session.Progress += _ => wrongProgressThread |= Environment.CurrentManagedThreadId != uiThread;
                var time = Stopwatch.StartNew();
                var task = session.RunAsync(new string('a', 64));
                if (time.Elapsed > TimeSpan.FromSeconds(1))
                    throw new Exception("Synchronous native work blocked the operator thread");
                if (!worker.Started.Wait(TimeSpan.FromSeconds(2)))
                    throw new Exception("Background queue did not start");
                session.Stop();
                while (!task.IsCompleted && time.Elapsed < TimeSpan.FromSeconds(5))
                    context.Pump();
                task.GetAwaiter().GetResult();
                if (worker.Context != null || worker.ThreadId == uiThread || wrongProgressThread || session.IsRunning)
                    throw new Exception("Queue crossed UI/background execution boundaries");
                completion.TrySetResult(true);
            }
            catch (Exception error) { completion.TrySetException(error); }
        })
        {
            IsBackground = true,
            Name = "daily-test-ui"
        };
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(8));
        cases.Add("synchronous native/rule work leaves operator thread responsive to stop");
        cases.Add("workflow executes without UI context while progress stays on operator context");
    }
    private sealed class SynchronousWorker : IDailyQueueExecutor
    {
        public readonly ManualResetEventSlim Started = new();
        public SynchronizationContext? Context;
        public int ThreadId;
        public Task<int> PrepareAsync(Action<string> report) => Task.FromResult(0);
        public Task<int> ExecuteAsync(string root, string account, string output, string? resume, Action<string> report, bool syncCollection = false, IReadOnlyList<string>? tasks = null, string? retryOf = null)
        {
            Context = SynchronizationContext.Current;
            ThreadId = Environment.CurrentManagedThreadId;
            Started.Set();
            var time = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(root, "queue-stop")))
            {
                if (time.Elapsed > TimeSpan.FromSeconds(3))
                    throw new Exception("Operator could not stop synchronous work");
                Thread.Sleep(5);
            }
            return Task.FromResult(1);
        }
    }
    private sealed class PumpContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> callbacks = new();
        public override void Post(SendOrPostCallback callback, object? state) => callbacks.Add((callback, state));
        public void Pump()
        {
            if (callbacks.TryTake(out var item, 100))
                item.Callback(item.State);
        }
        public void Dispose() => callbacks.Dispose();
    }
}
