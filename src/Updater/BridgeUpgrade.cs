using System.Diagnostics;
using Dustweave;

internal static class BridgeUpgrade
{
    private static string T(string cn, string tw, string en) => RepairDialog.Text(cn, tw, en);
    internal static int Run(string installation)
    {
        try
        {
            DailyUpdateHost.RequireHost();
            if (Environment.GetEnvironmentVariable("DUSTWEAVE_UPDATE_ROLLBACK") == DailyUpdateBridge.TargetVersion)
            {
                RepairDialog.Notify(T("新版启动未成功，已还原更新。请稍后重新打开织尘重试；未启动游戏或定时任务。",
                    "新版啟動未成功，已還原更新。請稍後重新開啟織塵重試；未啟動遊戲或定時工作。",
                    "The new version could not start and the update was rolled back. Reopen Dustweave later to retry. No games or schedules were started."), true);
                return 1;
            }
            installation = Path.GetFullPath(installation); DailyUpdates.EnsureNoLinks(installation);
            var installed = DailyUpdateJson.TryRead<DailyUpdatePackage>(Path.Combine(installation, "update-package.json"));
            if (installed == null || !DailyUpdateBridge.IsBridge(installed.Version)) throw new InvalidDataException("updates.target_changed");
            string lockPath = Path.Combine(DailyUpdateHost.DataRoot, "updates", "bridge.lock");
            DailyUpdates.EnsureNoLinks(lockPath); Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            using var bridgeLease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var plan = RepairDialog.Run(T("正在完成 1.4.15 更新", "正在完成 1.4.15 更新", "Completing the 1.4.15 update"), async (progress, token) =>
            {
                await DailyUpdateBridge.WaitForPreviousInstallerAsync(installation, token);
                using var client = DailyUpdates.Client();
                var trust = DailyUpdateTrust.Production();
                var cached = DailyUpdateJson.TryRead<DailySignedUpdate>(Path.Combine(DailyUpdateHost.DataRoot, "updates", DailyUpdateBridge.TargetVersion + "-" + installed.Flavor, "signed-feed.json"));
                DailyVerifiedUpdate? verified = null;
                if (cached != null)
                {
                    try
                    {
                        var feed = DailyUpdateSignatures.Verify(cached, trust);
                        var target = feed.Releases.Single(r => r.Version == DailyUpdateBridge.TargetVersion);
                        DailyUpdateInventory.Validate(feed, target, installed.Version, installed.Flavor);
                        verified = new(feed, cached, "");
                    }
                    catch (Exception error) when (error is InvalidDataException or InvalidOperationException) { }
                }
                verified ??= await new DailyUpdateTransport(trust).FetchAsync(client, DailyUpdateHost.DataRoot, token);
                // The first approval authorizes this exact final release, never an
                // unrelated later version that appeared while the user was offline.
                var release = verified.Feed.Releases.SingleOrDefault(r => r.Version == DailyUpdateBridge.TargetVersion)
                    ?? throw new InvalidDataException("updates.unavailable");
                return await DailyUpdateInventory.PrepareAsync(trust, client, verified, release, installed.Version,
                    installed.Flavor, installation, DailyUpdateHost.DataRoot, progress, token);
            });
            using var storage = DailyUpdateHost.AcquireStorage(DailyUpdateHost.DataRoot);
            string nonce = Guid.NewGuid().ToString("N"), attempt = Path.Combine(plan.Directory, "attempts", nonce);
            DailyUpdates.EnsureNoLinks(attempt); Directory.CreateDirectory(attempt);
            // The destination helper is self-contained for BOTH editions. The
            // bridge helper must exit before files/runtime are replaced.
            string source = DailyUpdateRepair.VerifiedHelper(plan.Manifest, installation, DailyUpdateRepair.Staging(plan.Directory));
            string helper = Path.Combine(attempt, "Dustweave.Updater.exe"), jobPath = Path.Combine(attempt, "job.json");
            DailyUpdateTransaction.CopyDurable(source, helper);
            var release = plan.Verified.Feed.Releases.Single(r => r.Version == DailyUpdateBridge.TargetVersion);
            using var process = Process.GetCurrentProcess();
            var job = new DailyUpdateJob(plan.Directory, Path.Combine(installation, "Dustweave.exe"), process.Id,
                process.StartTime.ToUniversalTime().Ticks, release, release.Assets.Single(a => a.Flavor == installed.Flavor),
                nonce, DailyUpdateTransaction.Hash(Path.Combine(installation, "Dustweave.exe")))
                { InventoryUpgrade = true, FromVersion = installed.Version };
            DailyUpdateJson.Write(jobPath, job); DailyUpdateEngine.ValidateJob(job, jobPath);
            var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = attempt };
            start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(jobPath);
            using var child = Process.Start(start) ?? throw new IOException("updates.start_failed");
            return 0;
        }
        catch (Exception error)
        {
            try { DailyUpdateJson.Write(Path.Combine(DailyUpdateHost.DataRoot, "updates", "bridge-pending.json"),
                new { target = DailyUpdateBridge.TargetVersion, atUtc = DateTimeOffset.UtcNow, error = error.ToString() }); } catch { }
            RepairDialog.Notify(T("更新尚未完成。下次打开织尘会继续更新；不会启动游戏或定时任务。\n请检查网络后重新打开。",
                "更新尚未完成。下次開啟織塵會繼續更新；不會啟動遊戲或定時工作。\n請檢查網路後重新開啟。",
                "The update is not complete. Reopen Dustweave to resume; games and schedules remain paused.\nCheck your connection before retrying."), true);
            return 1;
        }
    }
}
