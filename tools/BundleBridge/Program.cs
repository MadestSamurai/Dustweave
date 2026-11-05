using System.Diagnostics;
using Dustweave;

internal static class Program
{
    internal const string Version = "1.5.17", Target = "1.6.0";
    private static string T(string cn, string tw, string en) => RepairDialog.Text(cn, tw, en);
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] is "--apply-update" or "--recover-update")
                return DailyUpdateEngine.Run(args[1], args[0] == "--recover-update");
            string installation = DailyUpdateHost.InstallRoot, root = DailyUpdateHost.DataRoot;
            if (Dustweave.Bootstrap.Recovery.TryRun(args, Environment.ProcessPath!, root)) return 0;
            DailyUpdateHost.RequireHost();
            if (!DailyUpdateBridge.IsInteractiveEntry(args)) return 2;
            using var instance = new Mutex(false, DailyApplication.InstanceMutex);
            bool owns; try { owns = instance.WaitOne(0); } catch (AbandonedMutexException) { owns = true; }
            if (!owns) throw new IOException("updates.app_busy");
            try
            {
                if (Environment.GetEnvironmentVariable("DUSTWEAVE_UPDATE_ROLLBACK") == Target)
                {
                    MarkVisible(root);
                    RepairDialog.Notify(T("新版启动失败，已回退。请稍后重新打开织尘继续更新；账号和设置已保留。", "新版啟動失敗，已回復。請稍後重新開啟織塵繼續更新；帳號與設定已保留。", "The new version could not start and was rolled back. Reopen Dustweave to retry. Accounts and settings are preserved."), true);
                    return 1;
                }
                var installed = DailyUpdateJson.TryRead<DailyUpdatePackage>(Path.Combine(installation, "update-package.json"));
                if (installed?.Version != Version || installed.Layout != 3) throw new InvalidDataException("updates.target_changed");
                var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var result = RepairDialog.Run(T("正在完成 1.6.0 更新", "正在完成 1.6.0 更新", "Completing the 1.6.0 update"), async (progress, token) =>
                {
                    await shown.Task.WaitAsync(token);
                    if (args is ["--updated", var nonce]) DailyUpdateEngine.Acknowledge(nonce, Version);
                    MarkVisible(root);
                    await DailyUpdateBridge.WaitForPreviousInstallerAsync(installation, token);
                    using var client = DailyUpdates.Client();
                    var trust = DailyUpdateTrust.Production(); var transport = new DailyUpdateTransport(trust);
                    string cache = Path.Combine(root, "updates", Target + "-" + installed.Flavor, "signed-feed.json");
                    DailyVerifiedUpdate? verified = null;
                    if (File.Exists(cache))
                    {
                        try
                        {
                            var signed = DailyUpdateJson.TryRead<DailySignedUpdate>(cache)!;
                            var feed = DailyUpdateSignatures.Verify(signed, trust);
                            var final = feed.Releases.Single(r => r.Version == Target);
                            if (DailyUpdates.ManualUpdateReason(feed, final, installed) == null) verified = new(feed, signed, "");
                        }
                        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or NullReferenceException) { }
                    }
                    verified ??= await transport.FetchAsync(client, root, token);
                    var release = DailyUpdates.WithBaselines(verified.Feed, verified.Feed.Releases.Single(r => r.Version == Target));
                    var meter = new Progress<int>(n => progress.Report(new("downloading", n, 100)));
                    string directory = await transport.DownloadAsync(client, verified, Target, installed.Flavor, root, meter, token, installation);
                    return (directory, release);
                }, () => shown.TrySetResult());
                using var storage = DailyUpdateHost.AcquireStorage(root);
                using var control = DailyUpdateHost.AcquireControl(root);
                if (DailyUpdateHost.HasIsolatedWindows()) throw new IOException("updates.isolated_open");
                string attemptNonce = Guid.NewGuid().ToString("N"), attempt = Path.Combine(result.directory, "attempts", attemptNonce);
                DailyUpdates.EnsureNoLinks(attempt); Directory.CreateDirectory(attempt);
                string helper = Path.Combine(attempt, "Dustweave.Updater.exe"), jobPath = Path.Combine(attempt, "job.json");
                DailyUpdateTransaction.CopyDurable(Environment.ProcessPath!, helper);
                using var process = Process.GetCurrentProcess();
                var choice = DailyUpdateJson.TryRead<DailyUpdateDownload>(Path.Combine(result.directory, "download-choice.json"))!;
                var job = new DailyUpdateJob(result.directory, Environment.ProcessPath!, process.Id, process.StartTime.ToUniversalTime().Ticks,
                    result.release, result.release.Assets.Single(a => a.Flavor == installed.Flavor), attemptNonce,
                    DailyUpdateTransaction.Hash(Environment.ProcessPath!), choice.DeltaFileName) { FromVersion = Version };
                DailyUpdateJson.Write(jobPath, job); DailyUpdateEngine.ValidateJob(job, jobPath);
                var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = attempt };
                start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(jobPath);
                using var child = Process.Start(start) ?? throw new IOException("updates.start_failed");
                return 0;
            }
            finally { instance.ReleaseMutex(); }
        }
        catch (OperationCanceledException) { return 2; }
        catch (Exception error)
        {
            try { DailyUpdateJson.Write(Path.Combine(DailyUpdateHost.DataRoot, "updates", "bundle-bridge-pending.json"), new { target = Target, atUtc = DateTimeOffset.UtcNow, error = error.ToString() }); } catch { }
            RepairDialog.Notify(T("更新尚未完成。请检查网络后重新打开织尘继续更新。账号和设置已保留，未启动游戏或日常任务。", "更新尚未完成。請檢查網路後重新開啟織塵繼續更新。帳號與設定已保留，未啟動遊戲或日常工作。", "The update is not complete. Check your connection and reopen Dustweave to continue. Accounts and settings are preserved; no game or daily tasks were started."), true);
            return 1;
        }
    }
    private static void MarkVisible(string root) => DailyUpdateJson.Write(Path.Combine(root, "startup-state.json"), new { stage = "bridge-window-visible", version = Version, atUtc = DateTimeOffset.UtcNow });
}
