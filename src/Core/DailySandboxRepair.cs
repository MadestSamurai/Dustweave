using Dustweave.Accounts;

namespace Dustweave;

public sealed record DailySandboxCandidate(string Box, string Root, string Issue)
{
    public bool CanBind => Issue.Length == 0;
}

// Repair only the mutable box name. Account roots and encrypted sessions stay in place.
public static class DailySandboxRepair
{
    private static string BindingPath(string state) => Path.Combine(state,"root","user","current","AppData","Local","BD2DailyAssistant","sandbox-binding.json");
    public static async Task<DailySandboxCandidate[]> InspectAsync(string account, CancellationToken token)
    {
        DailySandbox.RequireHost();
        string state = DailySandbox.StateDirectory(account);
        string installation = DailySandbox.Installation() ?? throw new InvalidOperationException("parallel.install_required");
        SandboxProcessScope.RequireAvailable();
        var names = SandboxConfiguration.Boxes(installation).Where(DailySandbox.ValidBoxName).Distinct(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<DailySandboxCandidate>();
        foreach (string box in names)
        {
            string root = (await DailySandbox.Run(installation,"SbieIni.exe",["query",box,"FileRootPath"],token).ConfigureAwait(false)).Trim();
            string issue = ProofIssue(account,state,root);
            candidates.Add(new(box,root,issue));
        }
        return candidates.OrderByDescending(x=>x.CanBind).ThenBy(x=>x.Box,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static DailySandboxCandidate? Select(IEnumerable<DailySandboxCandidate> candidates, string? requested)
    {
        var eligible = candidates.Where(c=>c.CanBind && (requested == null || c.Box.Equals(requested,StringComparison.OrdinalIgnoreCase))).ToArray();
        if (eligible.Length > 1) throw new InvalidDataException("sandbox.repair.ambiguous");
        return eligible.SingleOrDefault();
    }
    public static async Task<string> RepairAsync(string account, string? box, CancellationToken token)
    {
        DailySandbox.RequireHost();
        await DailySandbox.ConfigurationGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var selected = Select(await InspectAsync(account,token).ConfigureAwait(false),box)
                ?? throw new InvalidDataException("sandbox.repair.required");
            await BindCoreAsync(account,selected.Box,token).ConfigureAwait(false);
            return selected.Box;
        }
        finally { DailySandbox.ConfigurationGate.Release(); }
    }
    internal static async Task BindCoreAsync(string account, string box, CancellationToken token)
    {
        DailySandbox.RequireHost();
        if (!DailySandbox.ValidBoxName(box)) throw new InvalidDataException("sandbox.repair.required");
        string installation = DailySandbox.Installation() ?? throw new InvalidOperationException("parallel.install_required");
        var candidates = await InspectAsync(account,token).ConfigureAwait(false);
        var selected = Select(candidates,box) ?? throw new InvalidDataException("sandbox.repair.required");
        // Multiple aliases can point at the same physical account data. Every alias
        // must be idle; checking only the newly selected name permits duplicate login.
        foreach (var candidate in candidates.Where(c=>SandboxConfiguration.SameRoot(c.Root,selected.Root)))
        {
            string pids = (await DailySandbox.Run(installation,"Start.exe",["/box:"+candidate.Box,"/listpids"],token).ConfigureAwait(false)).Trim();
            if (pids != "0") throw new InvalidOperationException("sandbox.repair.running");
        }
        foreach (string setting in new[]{"OpenKeyPath","OpenIpcPath","OpenPipePath","OpenFilePath","BreakoutProcess","BreakoutFolder","BreakoutDocument"})
            if (!string.IsNullOrWhiteSpace(await DailySandbox.Run(installation,"SbieIni.exe",["query",box,setting],token).ConfigureAwait(false)))
                throw new InvalidDataException("sandbox.repair.shared");
        string actual = (await DailySandbox.Run(installation,"SbieIni.exe",["query",box,"FileRootPath"],token).ConfigureAwait(false)).Trim();
        Commit(account,DailySandbox.StateDirectory(account),box,actual);
    }
    internal static string ProofIssue(string account, string state, string configuredRoot)
    {
        if (!SandboxConfiguration.SameRoot(configuredRoot,Path.Combine(state,"root"))) return "sandbox.repair.other_root";
        string marker = Path.Combine(state,"instance.json");
        var registered = DailyJson.TryRead<DailySandboxRequest>(marker);
        if (File.Exists(marker) && (registered == null || registered.Account != account)) return "sandbox.repair.other_account";
        var binding = DailyJson.TryRead<DailySandboxBinding>(BindingPath(state));
        if (binding?.Account != account || binding.Slot is < 1 or > 100) return "sandbox.repair.no_proof";
        try
        {
            string local = Path.Combine(state,"root","user","current","AppData","Local");
            var saved = new SessionVault(Path.Combine(local,SessionConstants.VaultDirectoryName)).TryLoadFixedSlot(binding.Slot);
            return saved != null && DailyIdentity.MemberKey(SessionIdentity.GetMemberId(saved)??"") == account ? "" : "sandbox.repair.no_proof";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or SessionManagerException)
        { return "sandbox.repair.no_proof"; }
    }
    internal static void Commit(string account, string state, string box, string configuredRoot)
    {
        if (!DailySandbox.ValidBoxName(box)) throw new InvalidDataException("sandbox.repair.required");
        string issue = ProofIssue(account,state,configuredRoot);
        if (issue.Length != 0) throw new InvalidDataException(issue);
        string bindingPath = BindingPath(state), marker = Path.Combine(state,"instance.json");
        var binding = DailyJson.TryRead<DailySandboxBinding>(bindingPath)!;
        var registered = DailyJson.TryRead<DailySandboxRequest>(marker)
            ?? new DailySandboxRequest(box,binding.Slot,account,binding.Name,GameLauncher.ResolveExecutable(),DateTimeOffset.UtcNow);
        // Each write is atomic. If interrupted between writes, proof checking accepts
        // either name for the same account and completes the repair on the next launch.
        WriteAtomic(bindingPath,binding with { Box=box });
        WriteAtomic(marker,registered with { Box=box });
        try { DailyJson.Write(Path.Combine(state,"binding-repair.json"),new {atUtc=DateTimeOffset.UtcNow,account,previous=registered.Box,box,rootUnchanged=true,credentialsUnchanged=true}); }
        catch(IOException) { } catch(UnauthorizedAccessException) { }
    }
    private static void WriteAtomic<T>(string path,T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary=path+".repair-"+Guid.NewGuid().ToString("N");
        try
        {
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            { System.Text.Json.JsonSerializer.Serialize(stream,value,DailyJson.Options); stream.Flush(true); }
            File.Move(temporary,path,true);
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
}
