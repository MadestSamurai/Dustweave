using System.Text.Json;
using Dustweave.Accounts;

namespace Dustweave;

// The host marker is an index, not the only proof of an instance's identity.
// Recover a missing index only from a matching configured root, inner binding
// and a decryptable account snapshot; never take ownership from its name alone.
internal static class DailySandboxRegistration
{
    internal static async Task<DailySandboxRequest?> EnsureAsync(string account, CancellationToken token)
    {
        DailySandbox.RequireHost();
        string state = Path.Combine(DailySandbox.Store,DailySandbox.BoxName(account));
        if (!Directory.Exists(state)) return null;
        string installation = DailySandbox.Installation() ?? throw new InvalidOperationException("无法找到 Sandboxie，尚未核对隔离空间。");
        string actual = (await DailySandbox.Run(installation,"SbieIni.exe",["query",DailySandbox.BoxName(account),"FileRootPath"],token).ConfigureAwait(false)).Trim();
        return Resolve(account,state,actual,GameLauncher.ResolveExecutable);
    }

    internal static DailySandboxRequest? Resolve(string account, string state, string configuredRoot, Func<string> gamePath)
    {
        string box=DailySandbox.BoxName(account), root=Path.Combine(state,"root"), marker=Path.Combine(state,"instance.json");
        bool markerPresent=File.Exists(marker);
        var registered=DailyJson.TryRead<DailySandboxRequest>(marker);
        if (markerPresent && registered==null) throw new InvalidDataException("隔离空间登记文件无法读取，已保留原文件；仅此账号暂未启动。");
        if (registered!=null && (registered.Account!=account || registered.Box!=box))
            throw new InvalidDataException("隔离空间登记属于其他账号，未修改；仅此账号暂未启动。");
        if(configuredRoot.Length>0 && !Path.GetFullPath(configuredRoot).TrimEnd('\\').Equals(Path.GetFullPath(root).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("隔离目录与登记位置不一致，未修改；仅此账号暂未启动。");
        string local=Path.Combine(root,"user","current","AppData","Local");
        string bindingPath=Path.Combine(local,"BD2DailyAssistant","sandbox-binding.json");
        bool bindingPresent=File.Exists(bindingPath);
        var binding=DailyJson.TryRead<DailySandboxBinding>(bindingPath);
        if(bindingPresent && binding==null)throw new InvalidDataException("隔离账号绑定暂时无法读取，已保留原文件；仅此账号暂未启动。");
        if(binding!=null && (binding.Account!=account || binding.Box!=box))throw new InvalidDataException("隔离窗口绑定属于其他账号，未修改；仅此账号暂未启动。");
        if(registered!=null)return registered;
        if(configuredRoot.Length==0 && binding==null && (!Directory.Exists(root)||!Directory.EnumerateFileSystemEntries(root).Any()))return null;
        if(configuredRoot.Length==0 || binding==null)throw new InvalidDataException("隔离空间缺少可核对的账号绑定，已保留数据；仅此账号暂未启动。");
        if(binding.Slot is <1 or >100)throw new InvalidDataException("隔离空间的账号位置无效，未修改登记。");
        var vault=new SessionVault(Path.Combine(local,SessionConstants.VaultDirectoryName));
        var saved=vault.TryLoadFixedSlot(binding.Slot);
        if(saved==null || DailyIdentity.MemberKey(SessionIdentity.GetMemberId(saved)??"")!=account)
            throw new InvalidDataException("隔离空间的加密账号记录与绑定不一致，未修改登记。");
        var recovered=new DailySandboxRequest(box,binding.Slot,account,binding.Name,gamePath(),DateTimeOffset.UtcNow);
        DailySandbox.Validate(recovered);
        Directory.CreateDirectory(state);
        string temporary=marker+".recovery-"+Guid.NewGuid().ToString("N");
        try
        {
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            { JsonSerializer.Serialize(stream,recovered,DailyJson.Options); stream.Flush(true); }
            try { File.Move(temporary,marker); }
            catch(IOException) when(File.Exists(marker))
            {
                var raced=DailyJson.TryRead<DailySandboxRequest>(marker);
                if(raced?.Account!=account || raced.Box!=box)throw new InvalidDataException("登记恢复期间账号发生变化，未覆盖任何记录。");
                return raced;
            }
            DailyJson.Write(Path.Combine(state,"registration-recovery.json"),new {atUtc=DateTimeOffset.UtcNow,account,box,reason="missing_host_index",configuredRootMatched=true,innerBindingMatched=true,encryptedAccountMatched=true});
            return recovered;
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
}