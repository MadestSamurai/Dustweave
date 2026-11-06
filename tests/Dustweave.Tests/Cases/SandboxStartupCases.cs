using System.ComponentModel;
using System.IO.Compression;
using Dustweave;
using Dustweave.Accounts;

internal static class SandboxStartupCases
{
    internal static async Task Run(string root, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add("sandbox startup: " + name); }
        static Exception Unavailable() => new SandboxScopeException(new SandboxDriverQueryException(SandboxDriverScope.Unavailable));
        using var gate = new SemaphoreSlim(1, 1);
        bool ready = true; int starts = 0, continued = 0;
        void Probe() { if (!ready) throw Unavailable(); }
        Task Start(CancellationToken token) { token.ThrowIfCancellationRequested(); starts++; ready = true; return Task.CompletedTask; }
        await SandboxRuntimeStartup.EnsureAsync(gate, Probe, Start, default);
        Check(starts == 0, "healthy driver needs no service start or elevation");
        ready = false;
        var phases = new List<string>();
        await SandboxRuntimeStartup.EnsureAsync(gate, Probe, Start, default, phases.Add);
        continued++;
        await SandboxRuntimeStartup.EnsureAsync(gate, Probe, Start, default);
        Check(continued == 1 && starts == 1 && phases.SequenceEqual(new[] { "starting", "ready" }),
            "missing driver is recovered and the pending launch continues without another start");

        ready = false; starts = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task SlowStart(CancellationToken token) { starts++; entered.SetResult(); await release.Task.WaitAsync(token); ready = true; }
        var first = SandboxRuntimeStartup.EnsureAsync(gate, Probe, SlowStart, default);
        await entered.Task;
        var others = Enumerable.Range(0, 5).Select(_ => SandboxRuntimeStartup.EnsureAsync(gate, Probe, SlowStart, default)).ToArray();
        release.SetResult(); await Task.WhenAll(others.Append(first));
        Check(starts == 1, "concurrent account launches share the restored driver");

        foreach (int status in new[] { unchecked((int)0xC0000022), unchecked((int)0xC000000D) })
        {
            starts = 0; bool rejected = false;
            try { await SandboxRuntimeStartup.EnsureAsync(gate, () => throw new SandboxScopeException(new SandboxDriverQueryException(status)), Start, default); }
            catch (SandboxScopeException) { rejected = true; }
            Check(rejected && starts == 0, "unrelated process query failure does not start/reconfigure services: " + status);
        }
        ready = false; starts = 0; bool timedOut = false;
        try { await SandboxRuntimeStartup.EnsureAsync(gate, Probe, _ => { starts++; return Task.CompletedTask; }, default, timeout: TimeSpan.FromMilliseconds(80)); }
        catch (SandboxStartupException error) { timedOut = error.Message == SandboxRuntimeStartup.TimedOut; }
        Check(timedOut && starts == 1, "successful start command is insufficient without an actual driver reply; waiting is bounded");
        await SandboxRuntimeStartup.EnsureAsync(gate, Probe, Start, default);
        Check(ready && starts == 2, "retry after timeout is allowed and actually completes recovery");

        using (var stop = new CancellationTokenSource())
        {
            ready = false; starts = 0; bool cancelled = false;
            async Task CancelDuringStart(CancellationToken token) { starts++; stop.Cancel(); await Task.Delay(Timeout.Infinite, token); }
            try { await SandboxRuntimeStartup.EnsureAsync(gate, Probe, CancelDuringStart, stop.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && starts == 1, "cancel during recovery ends waiting without claiming launch success");
        }
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); starts = 0;
            try { await SandboxRuntimeStartup.EnsureAsync(gate, Probe, Start, cancelled.Token); }
            catch (OperationCanceledException) { }
            Check(starts == 0, "an already cancelled operation cannot dispatch recovery");
        }
        await SandboxRuntimeStartup.EnsureAsync(gate, Probe, Start, default);
        Check(ready, "cancellation releases the recovery gate for a later attempt");
        ready = false;
        await SandboxRuntimeStartup.EnsureAsync(gate, Probe, Start, default, _ => throw new IOException("audit unavailable"));
        Check(ready, "an unavailable diagnostic destination cannot block restored startup");

        var command = SandboxServices.Command("SbieSvc", false);
        var elevated = SandboxServices.Command("SbieDrv", true);
        Check(Path.IsPathFullyQualified(command.FileName) && Path.GetFileName(command.FileName) == "sc.exe"
            && command.ArgumentList.SequenceEqual(new[] { "start", "SbieSvc" }) && !command.UseShellExecute
            && elevated.ArgumentList.SequenceEqual(new[] { "start", "SbieDrv" }) && elevated.Verb == "runas"
            && elevated.WindowStyle == System.Diagnostics.ProcessWindowStyle.Hidden,
            "recovery can only start fixed services using the Windows helper; no game/configuration commands");
        bool invalid = false; try { SandboxServices.Command("other-service", true); } catch (ArgumentException) { invalid = true; }
        Check(invalid, "no arbitrary service target can reach elevation");

        int state = SandboxServices.Stopped; var requests = new List<bool>();
        await SandboxServices.EnsureServiceAsync("SbieSvc", _ => state, (_, admin, _) =>
        { requests.Add(admin); if (admin) state = SandboxServices.Running; return Task.FromResult(admin ? 0 : 5); }, default);
        Check(requests.SequenceEqual(new[] { false, true }), "request administrator consent only after Windows denies the ordinary start");
        requests.Clear();
        await SandboxServices.EnsureServiceAsync("SbieSvc", _ => SandboxServices.Running, (_, admin, _) => { requests.Add(admin); return Task.FromResult(0); }, default);
        Check(requests.Count == 0, "running service is never restarted");

        int reads = 0;
        await SandboxServices.EnsureServiceAsync("SbieSvc", _ => ++reads == 1 ? SandboxServices.Starting : SandboxServices.Running,
            (_, _, _) => throw new Exception("Duplicate service start"), default);
        Check(reads == 2, "service already starting is observed rather than started again");
        reads = 0; requests.Clear(); state = SandboxServices.Stopped;
        await SandboxServices.EnsureServiceAsync("SbieDrv", _ => ++reads == 1 ? SandboxServices.Stopping : state,
            (_, admin, _) => { requests.Add(admin); state = SandboxServices.Running; return Task.FromResult(0); }, default);
        Check(requests.SequenceEqual(new[] { false }), "service stopping may finish before one bounded start");
        state = SandboxServices.Stopped;
        await SandboxServices.EnsureServiceAsync("SbieDrv", _ => state,
            (_, _, _) => { state = SandboxServices.Running; return Task.FromResult(1056); }, default);
        Check(state == SandboxServices.Running, "another process winning the service-start race is accepted after reobservation");
        using (var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(80)))
        {
            bool ended = false;
            try { await SandboxServices.EnsureServiceAsync("SbieSvc", _ => SandboxServices.Starting, (_, _, _) => throw new Exception("Repeated start"), deadline.Token); }
            catch (OperationCanceledException) { ended = true; }
            Check(ended, "a stuck Windows start-pending state obeys the recovery deadline");
        }
        foreach (var (code, key) in new[] { (1058, SandboxRuntimeStartup.Disabled), (1060, SandboxRuntimeStartup.Missing) })
        {
            bool rejected = false;
            try { await SandboxServices.EnsureServiceAsync("SbieSvc", _ => SandboxServices.Stopped, (_, _, _) => Task.FromResult(code), default); }
            catch (SandboxStartupException error) { rejected = error.Message == key; }
            Check(rejected, "disabled or missing services retain a specific recovery explanation: " + code);
        }
        bool missing = false;
        try { await SandboxServices.EnsureServiceAsync("SbieSvc", _ => throw new Win32Exception(1060), (_, _, _) => throw new Exception("Unexpected install"), default); }
        catch (SandboxStartupException error) { missing = error.Message == SandboxRuntimeStartup.Missing; }
        Check(missing, "missing service registration does not cause an automatic install");
        requests.Clear(); bool stopped = false;
        try { await SandboxServices.EnsureServiceAsync("SbieSvc", _ => SandboxServices.Stopped, (_, admin, _) => { requests.Add(admin); return Task.FromResult(0); }, default); }
        catch (Win32Exception error) { stopped = error.NativeErrorCode == 1067; }
        Check(stopped && requests.Count == 1, "service failing after start cannot trigger an infinite restart loop");

        if (OperatingSystem.IsWindows())
        {
            // Exercise the production SCM marshalling read-only. Never start,
            // stop or reconfigure the machine's installed services in tests.
            try { Check(SandboxServices.State("SbieSvc") is >= 1 and <= 7, "native SCM query reads an installed service without changing it"); }
            catch (Win32Exception error) when (error.NativeErrorCode == 1060)
            { Check(true, "native SCM query preserves missing-service status without installing it"); }
        }

        foreach (string key in new[] { SandboxRuntimeStartup.Failed, SandboxRuntimeStartup.TimedOut, SandboxRuntimeStartup.Missing, SandboxRuntimeStartup.Disabled, SandboxRuntimeStartup.Cancelled })
        {
            var failure = new SandboxStartupException(key, new Win32Exception(5));
            var expected = key == SandboxRuntimeStartup.Cancelled ? "cancelled" : "sandbox-unavailable";
            Check(DailyIssues.Classify(failure).Code == expected && DailyIssues.Classify(new IOException("wrapper", failure)).Code == expected
                && DailyHelperFailure.Read(DailyHelperFailure.Encode(failure))?.Issue.Code == expected,
                "service error retains its classification across helper propagation: " + key);
            Check(DailyUserText.Error(failure) != DailyUserText.Untranslated && DailyUserText.Error(failure).Contains("Sandboxie"),
                "service failure has a readable explanation: " + key);
        }
        string data = Path.Combine(root, "sandbox-startup-export"); Directory.CreateDirectory(data);
        DailyJson.Write(Path.Combine(data, "sandbox-runtime-recovery.json"), new { phase = "ready", atUtc = DateTimeOffset.UtcNow });
        var exported = await DailyDiagnosticExport.CreateAsync(data, Path.Combine(root, "sandbox-startup.zip"), "fixture", "test", default);
        using var zip = ZipFile.OpenRead(exported.Path);
        Check(zip.GetEntry("logs/main/sandbox-runtime-recovery.json") != null, "recovery phase is included in the ordinary diagnostic export");
    }
}
