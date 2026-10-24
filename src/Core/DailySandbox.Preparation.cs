using Dustweave.Accounts;
using Dustweave.Compatibility;

namespace Dustweave;

public static partial class DailySandbox
{
    internal static async Task PrepareSpaceAsync(DailySandboxRequest request,string executable,Action<string> progress,CancellationToken token)
    {
        string installation=Installation()??throw new InvalidOperationException("parallel.install_required");
        string box=request.Box,state=StateDirectory(request.Account),root=Path.Combine(state,"root"),marker=Path.Combine(state,"instance.json");
        RequireNotRemoving(state);
        var previous=DailyJson.TryRead<DailySandboxRequest>(marker);
        if(SandboxConfiguration.Boxes(installation).Contains(box,StringComparer.OrdinalIgnoreCase))
            await DailySandboxProcesses.WaitIdleAsync(installation,box,state,token);
        // A previously registered box is never silently reconfigured while running.
        bool configured = SandboxConfiguration.Boxes(installation).Contains(box,StringComparer.OrdinalIgnoreCase);
        if (configured)
        {
            var owned = DailyJson.TryRead<DailySandboxRequest>(marker);

            string actual = (await Run(installation,"SbieIni.exe",["query",box,"FileRootPath"],token)).Trim();
            if(actual.Length==0)
            {
                var pids=(await Run(installation,"Start.exe",["/box:"+box,"/listpids"],token)).Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries);
                if(pids.Length!=1||pids[0].Trim()!="0")throw new InvalidOperationException("隔离空间尚未配置完整，但仍有程序运行，请先关闭该空间中的程序。");
                configured=false;
            }
            else
            {
                if (owned == null || owned.Account != request.Account || !string.Equals(owned.Box, box, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("同名沙箱不是由此账号登记的，未修改它。");
                if (!SandboxConfiguration.SameRoot(actual,root)) throw new InvalidOperationException("隔离目录已被修改，请先检查 Sandboxie 设置。");
            }
        }
        if(!configured)
        {
            // Record ownership before configuration so an interrupted setup is repairable.
            DailyJson.Write(marker,request);
            progress("准备独立账号空间…");
            var settings = new Dictionary<string,string> {
                ["FileRootPath"]=root, ["Enabled"]="y", ["ConfigLevel"]="10",
                ["AutoDelete"]="n", ["BoxNameTitle"]="y", ["HideOtherBoxes"]="y", ["HideNonSystemProcesses"]="y", ["BorderColor"]="#748FA5,ttl"
            };
            foreach (var pair in settings) await Run(installation,"SbieIni.exe",["set",box,pair.Key,pair.Value],token);
            // Keep normal game preferences through copy-on-write. The bootstrap writes
            // and verifies all account fields before any game is allowed to start.
            string low=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"AppData","LocalLow","Gamfs","BrownDust II");
            foreach(var child in new[]{"Vuplex.WebView","Neo","args.info"})
                await Run(installation,"SbieIni.exe",["append",box,"WriteFilePath",Path.Combine(low,child)],token);
            DailyJson.Write(marker,request);
        }
        foreach(string setting in new[]{"OpenKeyPath","OpenIpcPath","OpenPipePath","OpenFilePath","BreakoutProcess","BreakoutFolder","BreakoutDocument"})
            if(!string.IsNullOrWhiteSpace(await Run(installation,"SbieIni.exe",["query",box,setting],token)))
                throw new InvalidOperationException("隔离空间包含额外的共享或退出隔离设置，请先移除它们："+setting);
        // Keep unrelated host/box processes out of the isolated desktop. Integrated
        // tools also validate the exact game lifetime supplied by their parent.
        foreach(string setting in new[]{"HideOtherBoxes","HideNonSystemProcesses"})
            if((await Run(installation,"SbieIni.exe",["query",box,setting],token)).Trim()!="y")
                await Run(installation,"SbieIni.exe",["set",box,setting,"y"],token);
        // MacType also hooks CreateProcessInternalW; its delayed injection can retain
        // a stale Sandboxie trampoline. Restrict this compatibility exclusion to our
        // executable in this box, preserving the user's global font settings.
        var closed=(await Run(installation,"SbieIni.exe",["query",box,"ClosedFilePath"],token)).Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries);
        foreach(var dll in new[]{"MacType64.dll","MacType.dll"})
        {
            string rule=Path.GetFileName(executable)+",*\\"+dll;
            if(!closed.Contains(rule,StringComparer.OrdinalIgnoreCase))await Run(installation,"SbieIni.exe",["append",box,"ClosedFilePath",rule],token);
        }
        // The host owns its automation lock while scheduling. A fresh sandbox must
        // create its own lock instead of attempting to copy the host's open file.
        string controlLock = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BD2DailyAssistant", "tools", "control.lock");
        var privateFiles = (await Run(installation,"SbieIni.exe",["query",box,"WriteFilePath"],token)).Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries);
        var missingPrivatePaths = DailySandboxStorage.Paths(DailyIdentity.DataRoot).Append(controlLock)
            .Except(privateFiles,StringComparer.OrdinalIgnoreCase).ToArray();
        if(configured && missingPrivatePaths.Length>0)
        {
            var pids=(await Run(installation,"Start.exe",["/box:"+box,"/listpids"],token)).Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries);
            if(pids.Length!=1||pids[0].Trim()!="0")throw new InvalidOperationException("sandbox.storage_busy");
        }
        foreach(string path in missingPrivatePaths)
            await Run(installation,"SbieIni.exe",["append",box,"WriteFilePath",path],token);
        // Complete an already pending research recovery once, then install the
        // production rules. Normal launches create no research transaction.
        await DailyLoadingFileTransaction.RecoverAsync(root,box,
            () => Run(installation,"SbieIni.exe",["query",box,"ReadFilePath"],token),
            async () => (await Run(installation,"Start.exe",["/box:"+box,"/listpids"],token)).Trim()=="0",
            async rule => { await Run(installation,"SbieIni.exe",["append",box,"ReadFilePath",rule],token); },token);
        bool legacyFiles=await DailySandboxGameFiles.ExperimentAsync(request.GameExecutable,root,box,installation,
            () => Run(installation,"SbieIni.exe",["query",box,"ReadFilePath"],token),
            async () => (await Run(installation,"Start.exe",["/box:"+box,"/listpids"],token)).Trim()=="0",
            async rule => { await Run(installation,"SbieIni.exe",["delete",box,"ReadFilePath",rule],token); },token);
        if(!legacyFiles)await DailySandboxGameFiles.EnsureAsync(request.GameExecutable,
            () => Run(installation,"SbieIni.exe",["query",box,"ReadFilePath"],token),
            async () => (await Run(installation,"Start.exe",["/box:"+box,"/listpids"],token)).Trim()=="0",
            async rule => { await Run(installation,"SbieIni.exe",["append",box,"ReadFilePath",rule],token); }, progress, token,
            async rule => { await Run(installation,"SbieIni.exe",["delete",box,"ReadFilePath",rule],token); },request.FileMode,previous?.GameExecutable);
        if(!legacyFiles && request.FileMode==DailySandboxFileMode.Compatible)
            await DailySandboxProgramSync.SynchronizeAsync(request.GameExecutable,root,Path.Combine(state,"game-program-recovery"),
                async () => (await DailySandboxProcesses.ReadAsync(installation,box,token)).Length==0,progress,token);
        // Tables live outside the installation directory. Repair only recognized
        // stale design-table overlays, before a worker or game starts in this box.
        await DailySandboxGameData.SynchronizeAsync(DailySandboxGameData.HostTables,
            DailySandboxGameData.BoxTables(root), Path.Combine(StateDirectory(request.Account), "game-table-recovery"),
            async () => (await Run(installation,"Start.exe",["/box:"+box,"/listpids"],token)).Trim()=="0", progress, token);

        DailyJson.Write(marker,request with {Worker=null,CaptureSessionId=""});
    }
}
