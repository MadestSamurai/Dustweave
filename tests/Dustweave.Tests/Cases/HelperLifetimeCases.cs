using Dustweave;
using System.Diagnostics;

static class HelperLifetimeCases
{
    public static async Task Run(List<string> cases)
    {
        foreach (string reason in new[] { "stop", "identity", "timeout" })
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Environment.CurrentDirectory };
            start.ArgumentList.Add("--watchdog-child");
            using var child = Process.Start(start)!;
            int reads = 0;
            var time = Stopwatch.StartNew();
            Exception? failure = null;
            try
            {
                await DailyHelperLifetime.WaitAsync(child, () =>
                {
                    reads++;
                    if (reads >= 2 && reason == "stop")
                        throw new OperationCanceledException("operator_stop");
                    if (reads >= 2 && reason == "identity")
                        throw new StageHostException("identity", "changed");
                    return Task.CompletedTask;
                }, TimeSpan.FromMilliseconds(350));
            }
            catch (Exception error) { failure = error; }
            finally { if (!child.HasExited) child.Kill(true); }
            if (!child.HasExited || reads < 2 || time.Elapsed >= TimeSpan.FromSeconds(5) ||
                !(reason switch
                {
                    "stop" => failure is OperationCanceledException,
                    "identity" => failure is StageHostException,
                    _ => failure is TimeoutException
                }))
                throw new Exception("Helper lifetime failed: " + reason);
            cases.Add("actual hung mini-game helper exits promptly on " + reason);
        }
    }
}
