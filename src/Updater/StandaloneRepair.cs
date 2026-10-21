using System.Diagnostics;
using Dustweave;

internal static class StandaloneRepair
{
    private static string T(string cn, string tw, string en) => RepairDialog.Text(cn, tw, en);
    public static int Run(string installRoot)
    {
        try
        {
            DailyUpdateHost.RequireHost();
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
            if (!RepairDialog.Confirm(T("检查并修复以下安装？\n账号、设置和插件会保留。\n\n", "檢查並修復以下安裝？\n帳號、設定和外掛會保留。\n\n",
                "Check and repair this installation?\nAccounts, settings and plugins will be preserved.\n\n") + installRoot)) return 0;
            var installed = DailyUpdateJson.TryRead<DailyUpdatePackage>(Path.Combine(installRoot, "update-package.json"));
            string? version = installed?.Version, flavor = installed?.Flavor;
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
                var verified = await DailyUpdateTransport.Production().FetchAsync(client, DailyUpdateHost.DataRoot, token);
                if (!verified.Feed.Releases.Any(r => r.Version == version)) version = verified.Feed.Releases[0].Version;
                return await DailyUpdateRepair.Production().InspectAsync(client, verified, version!, flavor, installRoot, DailyUpdateHost.DataRoot, progress, token);
            });
            if (plan.Problems.Length == 0)
            { RepairDialog.Notify(T("安装文件完整，无需修复。", "安裝檔案完整，無需修復。", "Installation verified. No repairs needed.")); return 0; }
            string summary = $"{plan.Manifest.Version} · {plan.Manifest.Flavor}\n{plan.Problems.Length} " +
                T("个文件 · 预计下载 ", "個檔案 · 預計下載 ", "files · Estimated download ") + $"{plan.DownloadBytes / 1048576d:0.##} MB\n\n";
            if (!RepairDialog.Confirm(summary + T("下载所需文件，完成后再确认重启修复？", "下載所需檔案，完成後再確認重新啟動修復？",
                "Download the required files? You will confirm again before restarting to apply them."))) return 0;
            RepairDialog.Run(T("下载并准备修复", "下載並準備修復", "Download and prepare repairs"), async (progress, token) =>
            {
                using var client = DailyUpdates.Client();
                await DailyUpdateRepair.Production().PrepareAsync(client, plan, DailyUpdateHost.DataRoot, progress, token);
                return true;
            });
            if (!RepairDialog.Confirm(T("修复文件已验证。现在替换文件并启动织尘？\n如果启动失败，将还原本次更改。",
                "修復檔案已驗證。現在替換檔案並啟動織塵？\n如果啟動失敗，將還原本次變更。",
                "Repairs verified. Replace files and start Dustweave now?\nChanges will be rolled back if startup fails."))) return 0;
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
