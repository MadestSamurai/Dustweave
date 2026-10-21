using System.Diagnostics;
using Dustweave;

internal static class StandaloneRepair
{
    private static string ProductVersion => typeof(StandaloneRepair).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion.Split('+')[0];
    private static string T(string cn, string tw, string en) => RepairDialog.Text(cn, tw, en);
    public static int Run(string installRoot, bool approved = false, string? expectedVersion = null, string? expectedFlavor = null)
    {
        try
        {
            DailyUpdateHost.RequireHost();
            if (expectedVersion != null && expectedVersion != ProductVersion || expectedFlavor != null && expectedFlavor is not ("Lite" or "Portable"))
                throw new InvalidDataException("updates.invalid_package");
            installRoot = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar);
            DailyUpdates.EnsureNoLinks(installRoot);
            if (!Directory.Exists(Path.Combine(installRoot, "app"))) throw new InvalidDataException("Not a packaged Dustweave directory.");
            if (File.Exists(Path.Combine(installRoot, DailyUpdateTransaction.MarkerName)))
                throw new IOException(T("上次替换尚未恢复。请先启动 Dustweave.exe 完成回退。", "上次替換尚未恢復。請先啟動 Dustweave.exe 完成回復。", "An interrupted replacement needs recovery. Start Dustweave.exe first."));
            using (var instance = new Mutex(false, DailyApplication.InstanceMutex))
            {
                bool owned; try { owned = instance.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
                if (!owned) throw new IOException(T("请先从托盘完全退出织尘。", "請先從系統匣完全退出織塵。", "Exit Dustweave from the system tray first."));
                instance.ReleaseMutex();
            }
            if (!approved && !RepairDialog.Confirm(T("检查并修复以下安装？\n账号、设置和插件会保留。\n\n", "檢查並修復以下安裝？\n帳號、設定和外掛會保留。\n\n",
                "Check and repair this installation?\nAccounts, settings and plugins will be preserved.\n\n") + installRoot)) return 0;
            var installed = DailyUpdateJson.TryRead<DailyUpdatePackage>(Path.Combine(installRoot, "update-package.json"));
            // The independently running helper identifies its own release.
            // Damaged local metadata never silently selects another version.
            string version = expectedVersion ?? ProductVersion;
            string? flavor = expectedFlavor ?? installed?.Flavor;
            // Conflicting/missing descriptors require an explicit edition choice.
            if (flavor is not ("Lite" or "Portable"))
            {
                bool portable = RepairDialog.Confirm(T("是否修复为 Portable（含运行库）版本？\n选择“否”使用 Lite，需要已安装 .NET 8 桌面运行库。",
                    "是否修復為 Portable（含執行階段）版本？\n選擇「否」使用 Lite，需要已安裝 .NET 8 桌面執行階段。",
                    "Repair as Portable (includes the runtime)?\nChoose No for Lite, which requires the .NET 8 Desktop Runtime."));
                flavor = portable ? "Portable" : "Lite";
            }
            var plan = RepairDialog.Run(T("检查安装文件", "檢查安裝檔案", "Check installation"), async (progress, token) =>
            {
                using var client = DailyUpdates.Client();
                var trust = DailyUpdateTrust.Production();
                DailyVerifiedUpdate? verified = null;
                var cached = DailyUpdateJson.TryRead<DailySignedUpdate>(Path.Combine(DailyUpdateHost.DataRoot, "updates", version + "-" + flavor, "signed-feed.json"));
                if (cached != null)
                {
                    try
                    {
                        var feed = DailyUpdateSignatures.Verify(cached, trust);
                        if (feed.Releases.Any(r => r.Version == version)) verified = new(feed, cached, "");
                    }
                    catch (InvalidDataException) { }
                }
                verified ??= await new DailyUpdateTransport(trust).FetchAsync(client, DailyUpdateHost.DataRoot, token);
                if (!verified.Feed.Releases.Any(r => r.Version == version)) throw new InvalidDataException("updates.repair_unavailable");
                return await DailyUpdateRepair.Production().InspectAsync(client, verified, version, flavor, installRoot, DailyUpdateHost.DataRoot, progress, token);
            });
            if (plan.Problems.Length == 0)
            { RepairDialog.Notify(T("安装文件完整，无需修复。", "安裝檔案完整，無需修復。", "Installation verified. No repairs needed.")); return 0; }
            RepairDialog.Run(T("下载并准备修复", "下載並準備修復", "Download and prepare repairs"), async (progress, token) =>
            {
                using var client = DailyUpdates.Client();
                await DailyUpdateRepair.Production().PrepareAsync(client, plan, DailyUpdateHost.DataRoot, progress, token);
                return true;
            });
            using var storage = DailyUpdateHost.AcquireStorage(DailyUpdateHost.DataRoot);
            string nonce = Guid.NewGuid().ToString("N"), attempt = Path.Combine(plan.Directory, "attempts", nonce);
            DailyUpdates.EnsureNoLinks(attempt); Directory.CreateDirectory(attempt);
            string helper = Path.Combine(attempt, "Dustweave.Updater.exe"), target = Path.Combine(installRoot, "Dustweave.exe");
            // Copy this independently running helper, never execute a potentially
            // damaged application DLL to perform the repair.
            DailyUpdateTransaction.CopyDurable(Environment.ProcessPath!, helper);
            var release = plan.Verified.Feed.Releases.Single(r => r.Version == plan.Manifest.Version);
            using var process = Process.GetCurrentProcess();
            var job = new DailyUpdateJob(plan.Directory, target, process.Id, process.StartTime.ToUniversalTime().Ticks,
                release, release.Assets.Single(a => a.Flavor == plan.Manifest.Flavor), nonce,
                File.Exists(target) ? DailyUpdateTransaction.Hash(target) : "") { Repair = true };
            string jobPath = Path.Combine(attempt, "job.json"); DailyUpdateJson.Write(jobPath, job); DailyUpdateEngine.ValidateJob(job, jobPath);
            var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = attempt };
            start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(jobPath);
            using var child = Process.Start(start) ?? throw new IOException("updates.start_failed");
            return 0;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception error)
        {
            string path = Path.Combine(DailyUpdateHost.DataRoot, "updates", "standalone-repair-error.json");
            try { DailyUpdateJson.Write(path, new { atUtc = DateTimeOffset.UtcNow, error = error.ToString() }); } catch { }
            RepairDialog.Notify(T("修复未完成。请检查网络后重试，或下载完整软件包。\n日志：", "修復未完成。請檢查網路後重試，或下載完整軟體包。\n記錄：",
                "Repair did not complete. Check your connection and retry, or download the full package.\nLog: ") + path + "\n\n" + error.Message, true);
            return 1;
        }
    }
}
