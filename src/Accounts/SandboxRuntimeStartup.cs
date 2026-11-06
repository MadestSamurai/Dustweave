using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Dustweave.Accounts;

// Explicit launch/handoff only. Passive process detection must never start a
// service or request elevation. No installation, stop, restart or config edits.
internal static class SandboxRuntimeStartup
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    internal const string Failed = "sandbox.start_failed", TimedOut = "sandbox.start_timeout",
        Cancelled = "sandbox.start_cancelled", Missing = "sandbox.start_missing", Disabled = "sandbox.start_disabled";

    internal static Task EnsureAsync(CancellationToken token, Action<string>? report = null) =>
        EnsureAsync(Gate, SandboxProcessScope.RequireAvailable, SandboxServices.StartAsync, token, report);

    internal static bool DriverUnavailable(Exception error)
    {
        for (Exception? current = error; current != null; current = current.InnerException)
            if (current is SandboxDriverQueryException query) return query.Status == SandboxDriverScope.Unavailable;
        return false;
    }

    internal static async Task EnsureAsync(SemaphoreSlim gate, Action probe, Func<CancellationToken, Task> start,
        CancellationToken token, Action<string>? report = null, TimeSpan? timeout = null)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            try { probe(); return; }
            catch (Exception error) when (DriverUnavailable(error)) { }
            // A fresh probe after acquiring the gate lets concurrent launches
            // reuse recovery; failures are not cached across a later retry.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(45));
            Report("starting");
            try
            {
                await start(deadline.Token).ConfigureAwait(false);
                while (true)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    try { probe(); break; }
                    catch (Exception error) when (DriverUnavailable(error)) { }
                    await Task.Delay(100, deadline.Token).ConfigureAwait(false);
                }
                Report("ready");
            }
            catch (OperationCanceledException error) when (!token.IsCancellationRequested)
            { Report("timeout"); throw new SandboxStartupException(TimedOut, error); }
            catch (OperationCanceledException) { Report("cancelled"); throw; }
            catch (SandboxStartupException error) { Report(error.Message); throw; }
            catch (Exception error) when (error is IOException or Win32Exception or UnauthorizedAccessException)
            { Report("failed"); throw new SandboxStartupException(Failed, error); }
        }
        finally { gate.Release(); }
        void Report(string phase)
        {
            try { report?.Invoke(phase); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

internal sealed class SandboxStartupException(string key, Exception inner) : SandboxScopeException(key, inner);

internal static class SandboxServices
{
    internal const int Stopped = 1, Starting = 2, Stopping = 3, Running = 4;
    private const int AccessDenied = 5, AlreadyRunning = 1056;

    internal static async Task StartAsync(CancellationToken token)
    {
        // SbieSvc normally starts SbieDrv itself. If it was already running
        // while the driver was stopped, explicitly start only that driver.
        await EnsureServiceAsync("SbieSvc", State, StartCommandAsync, token).ConfigureAwait(false);
        await EnsureServiceAsync("SbieDrv", State, StartCommandAsync, token).ConfigureAwait(false);
    }

    internal static async Task EnsureServiceAsync(string name, Func<string, int> state,
        Func<string, bool, CancellationToken, Task<int>> start, CancellationToken token)
    {
        bool requested = false;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int current;
            try { current = state(name); }
            catch (Win32Exception error) when (error.NativeErrorCode == 1060)
            { throw new SandboxStartupException(SandboxRuntimeStartup.Missing, error); }
            if (current == Running) return;
            if (current == Stopped)
            {
                if (requested) throw new Win32Exception(1067, $"{name} stopped after its start request.");
                requested = true;
                int result = await start(name, false, token).ConfigureAwait(false);
                if (result == AccessDenied) result = await start(name, true, token).ConfigureAwait(false);
                if (result == 1060) throw new SandboxStartupException(SandboxRuntimeStartup.Missing, new Win32Exception(result));
                if (result == 1058) throw new SandboxStartupException(SandboxRuntimeStartup.Disabled, new Win32Exception(result));
                if (result != 0 && result != AlreadyRunning) throw new Win32Exception(result, $"Could not start {name} (Windows error {result}).");
            }
            else if (current != Starting && current != Stopping)
                throw new IOException($"{name} is in unsupported service state {current}.");
            await Task.Delay(100, token).ConfigureAwait(false);
        }
    }

    internal static ProcessStartInfo Command(string service, bool elevated)
    {
        if (service is not ("SbieSvc" or "SbieDrv")) throw new ArgumentException("Only Sandboxie services may be started.", nameof(service));
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe"))
        {
            UseShellExecute = elevated, Verb = elevated ? "runas" : "",
            CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System),
            RedirectStandardOutput = !elevated, RedirectStandardError = !elevated
        };
        info.ArgumentList.Add("start"); info.ArgumentList.Add(service);
        return info;
    }

    private static async Task<int> StartCommandAsync(string service, bool elevated, CancellationToken token)
    {
        // ShellExecute can wait on the Windows consent UI. Keep it off the UI
        // thread and let cancellation/deadline release the caller. If it returns
        // later, dispose only our sc.exe helper, never the Sandboxie service.
        var launch = Task.Run(() => Process.Start(Command(service, elevated))
            ?? throw new IOException("Windows did not start the Sandboxie service helper."), token);
        Process process;
        try { process = await launch.WaitAsync(token).ConfigureAwait(false); }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        { throw new SandboxStartupException(SandboxRuntimeStartup.Cancelled, error); }
        catch (OperationCanceledException)
        {
            _ = launch.ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion) { using var child = task.Result; StopHelper(child); }
                else _ = task.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
        using (process)
        {
            var stdout = elevated ? Task.CompletedTask : process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
            var stderr = elevated ? Task.CompletedTask : process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            try
            {
                await process.WaitForExitAsync(token).ConfigureAwait(false);
                await Task.WhenAll(stdout, stderr).WaitAsync(token).ConfigureAwait(false);
                return process.ExitCode;
            }
            finally { StopHelper(process); }
        }
    }

    private static void StopHelper(Process process)
    {
        try { if (!process.HasExited) process.Kill(); }
        catch (InvalidOperationException) { } catch (Win32Exception) { }
    }

    internal static int State(string name)
    {
        using var manager = OpenSCManager(null, null, 1); // SC_MANAGER_CONNECT
        if (manager.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        using var service = OpenService(manager, name, 4); // SERVICE_QUERY_STATUS only
        if (service.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!QueryServiceStatus(service, out var status)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return status.CurrentState;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus { public uint Type; public int CurrentState; public uint Controls, Win32Exit, ServiceExit, CheckPoint, WaitHint; }
    private sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private ServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenService(ServiceHandle manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(ServiceHandle service, out ServiceStatus status);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(nint handle);
}
