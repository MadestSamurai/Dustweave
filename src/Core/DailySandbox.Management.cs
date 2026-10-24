using Dustweave.Accounts;
using Dustweave.Compatibility;

namespace Dustweave;

public sealed record DailySandboxInfo(string Box,string Root,bool Exists,string Issue,DailySandboxProcess[] Processes)
{
    public bool Running => Processes.Length>0;
    public bool CanRemove => Exists && !Running && Issue is "" or "sandbox.manage.removal_pending";
}

public static partial class DailySandbox
{
    internal sealed record RemovalIntent(int Schema,string Account,string Box,string Root,DailySandboxRequest Owner);
    internal static DailySandboxRequest? RemovalOwner(string state,string account,string box,string root)
    {
        var intent=DailyJson.TryRead<RemovalIntent>(Path.Combine(state,"removal.json"));
        if(intent==null)return null;
        if(intent.Schema!=1||intent.Account!=account||!intent.Box.Equals(box,StringComparison.OrdinalIgnoreCase)
            ||!SandboxConfiguration.SameRoot(intent.Root,root))throw new InvalidDataException("sandbox.manage.not_owned");
        ValidateManagedRoot(account,state,root,intent.Owner);
        return intent.Owner;
    }
    internal static void RequireNotRemoving(string state)
    {
        if(File.Exists(Path.Combine(state,"removal.json")))throw new InvalidOperationException("sandbox.manage.removal_pending");
    }
    internal static void ValidateManagedRoot(string account,string state,string configuredRoot,DailySandboxRequest? registered)
    {
        string expected=StateDirectory(account);
        if(!SandboxConfiguration.SameRoot(state,expected)||registered?.Account!=account||!ValidBoxName(registered.Box)
            ||!registered.Box.Equals(BoxName(account),StringComparison.OrdinalIgnoreCase)
            ||!SandboxConfiguration.SameRoot(configuredRoot,Path.Combine(expected,"root")))
            throw new InvalidDataException("sandbox.manage.not_owned");
        DailyCacheStorage.NoLinks(expected);DailyCacheStorage.NoLinks(configuredRoot);
    }
    public static async Task<DailySandboxInfo> InspectSpaceAsync(string account,CancellationToken token)
    {
        RequireHost();string state=StateDirectory(account),box=BoxName(account),root=Path.Combine(state,"root");
        string installation=Installation()??throw new InvalidOperationException("parallel.install_required");
        SandboxProcessScope.RequireAvailable();
        bool configured=SandboxConfiguration.Boxes(installation).Contains(box,StringComparer.OrdinalIgnoreCase);
        var owned=DailyJson.TryRead<DailySandboxRequest>(Path.Combine(state,"instance.json"))??RemovalOwner(state,account,box,root);
        string issue="";
        if(configured) {
            string actual=await Run(installation,"SbieIni.exe",["query",box,"FileRootPath"],token);
            try {ValidateManagedRoot(account,state,actual.Trim(),owned);}catch(InvalidDataException){issue="sandbox.repair.required";}
        } else if(owned!=null||(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any()))issue="sandbox.repair.required";
        if(File.Exists(Path.Combine(state,"removal.json")) && owned?.Account==account)issue="sandbox.manage.removal_pending";
        var processes=configured ? await DailySandboxProcesses.ReadAsync(installation,box,token) : [];
        return new(box,root,configured||owned!=null||(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any()),issue,processes);
    }
    public static async Task SaveFileModeAsync(DailySandboxFileMode mode,CancellationToken token)
    {
        RequireHost();await ConfigurationGate.WaitAsync(token);
        try {new DailySandboxPreferences(Mode:mode).Save();}
        finally {ConfigurationGate.Release();}
    }
    public static async Task CreateSpaceAsync(DailyAccount account,string executable,Action<string> progress,CancellationToken token)
    {
        RequireHost();if(!account.Valid)throw new InvalidDataException("run.account_unavailable");
        await ConfigurationGate.WaitAsync(token);
        try {
            string state=StateDirectory(account.AccountKey);RequireNotRemoving(state);
            await DailySandboxRegistration.EnsureAsync(account.AccountKey,token);
            string game=GameLauncher.ResolveExecutable();
            var request=new DailySandboxRequest(BoxName(account.AccountKey),account.SlotNumber,account.AccountKey,account.Name,game,DateTimeOffset.UtcNow)
                {FileMode=DailySandboxPreferences.Read().Mode,ClientKey=await Task.Run(()=>ClientInputs.ReadGame(game),token)};
            Validate(request);Directory.CreateDirectory(state);
            await PrepareSpaceAsync(request,executable,progress,token);
        } finally {ConfigurationGate.Release();}
    }
    public static async Task RemoveSpaceAsync(string account,string executable,Action<string> progress,CancellationToken token)
    {
        RequireHost();await ConfigurationGate.WaitAsync(token);
        try {
            string state=StateDirectory(account),box=BoxName(account),root=Path.Combine(state,"root"),marker=Path.Combine(state,"instance.json");
            string installation=Installation()??throw new InvalidOperationException("parallel.install_required");
            var owned=DailyJson.TryRead<DailySandboxRequest>(marker)??RemovalOwner(state,account,box,root);
            bool configured=SandboxConfiguration.Boxes(installation).Contains(box,StringComparer.OrdinalIgnoreCase);
            string pending=Path.Combine(state,"removal.json");
            if(!configured && !File.Exists(pending))throw new InvalidDataException("sandbox.manage.not_owned");
            string actual=configured?(await Run(installation,"SbieIni.exe",["query",box,"FileRootPath"],token)).Trim():root;
            ValidateManagedRoot(account,state,actual,owned);
            // Another alias must never retain access to a root being removed.
            foreach(string alias in SandboxConfiguration.Boxes(installation).Where(x=>!x.Equals(box,StringComparison.OrdinalIgnoreCase)))
                if(SandboxConfiguration.SameRoot(await Run(installation,"SbieIni.exe",["query",alias,"FileRootPath"],token),root))
                    throw new InvalidDataException("sandbox.manage.alias");
            if(File.Exists(DailyLoadingFileTransaction.Pending(root)))throw new InvalidOperationException("sandbox.manage.recovery_pending");
            if(configured) {
                await DailySandboxProcesses.WaitIdleAsync(installation,box,state,token);
                // No third-party deletion hooks execute as a side effect of this action.
                foreach(string setting in new[]{"DeleteCommand","OnBoxDelete","OnBoxTerminate","OnFileRecovery"})
                    if(!string.IsNullOrWhiteSpace(SandboxConfiguration.Read(installation,box,setting,includeTemplates:true)))throw new InvalidDataException("sandbox.manage.custom_delete");
                if(!File.Exists(pending)) {
                    progress("sandbox.manage.collecting");
                    await DailySandboxSessions.CollectAsync(account,new SessionVault(),executable,token);
                    await DailySandboxProcesses.WaitIdleAsync(installation,box,state,token);
                    DailyAccountProgress.ImportSandbox(DailyIdentity.DataRoot,account,force:true,requireSuccess:true);
                }
            }
            DailyJson.Write(pending,new RemovalIntent(1,account,box,root,owned!));
            progress("sandbox.manage.removing");
            if(configured) {
                // Sandboxie's own deletion handles its registry hive, long names and
                // reparse entries. Recheck exact ownership immediately before removal.
                ValidateManagedRoot(account,state,await Run(installation,"SbieIni.exe",["query",box,"FileRootPath"],token),owned);
                await DailySandboxProcesses.WaitIdleAsync(installation,box,state,token);
                await Run(installation,"Start.exe",["/box:"+box,"/silent","delete_sandbox"],token,TimeSpan.FromMinutes(3));
                if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any())throw new IOException("sandbox.manage.remove_incomplete");
                await Run(installation,"SbieIni.exe",["set",box,"*",""],token);
                if(SandboxConfiguration.Boxes(installation).Contains(box,StringComparer.OrdinalIgnoreCase))throw new IOException("sandbox.manage.remove_incomplete");
            } else if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any())throw new IOException("sandbox.manage.remove_incomplete");
            foreach(string name in new[]{"game-program-recovery","game-table-recovery"}) {
                string recovery=Path.GetFullPath(Path.Combine(state,name));
                if(!recovery.StartsWith(Path.GetFullPath(state)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("sandbox.manage.not_owned");
                if(!Directory.Exists(recovery))continue;
                void CheckTree(string path){DailyCacheStorage.NoLinks(path);if(Directory.Exists(path))foreach(string child in Directory.EnumerateFileSystemEntries(path))CheckTree(child);}
                CheckTree(recovery);token.ThrowIfCancellationRequested();Directory.Delete(recovery,true);
            }
            // Never delete the host account/vault, daily settings or merged progress.
            File.Delete(marker);File.Delete(pending);
        } finally {ConfigurationGate.Release();}
    }
}
