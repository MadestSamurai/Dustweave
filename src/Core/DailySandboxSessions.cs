using Dustweave.Accounts;

namespace Dustweave;

// Only a stopped game's sandbox can export its current session. The transfer is
// DPAPI-protected, read by the host from the owned box and removed after merging.
public static class DailySandboxSessions
{
    public const string ExportSwitch = "--export-sandbox-session";
    public sealed record ExportResult(string Account, string Nonce, bool Complete);
    private static string TransferDirectory => Path.Combine(new SessionVault().RootDirectory,"transfers");

    public static void Export(string account, string nonce)
    {
        if (!DailyProfiles.ValidKey(account) || !Guid.TryParseExact(nonce,"N",out _)) throw new InvalidDataException("无效的登录交接请求。");
        SandboxProcessScope.Require(DailySandbox.BoxName(account));
        var binding = DailySandbox.Current;
        if (binding == null || binding.Account != account) throw new InvalidDataException("隔离窗口的账号绑定不一致。");
        var status = SessionRegistry.GetStatus();
        if (status.GameRunning || status.StarterRunning) throw new InvalidOperationException("请先关闭这个账号的隔离游戏，再交接登录状态。");
        var vault = new SessionVault();
        bool complete = false;
        if (status.Complete)
        {
            var current = SessionRegistry.Capture("sandbox-return");
            if (DailyIdentity.MemberKey(SessionIdentity.GetMemberId(current) ?? "") != account)
                throw new InvalidDataException("隔离窗口已登录其他账号，未导出凭据。");
            new SessionService(vault).SynchronizeCaptured(current);
            var newest = SessionService.LatestSameAccount(vault.LoadFixedSlot(binding.Slot),vault.TryLoadFixedSlot);
            vault.WriteLaunchSnapshot(newest,Path.Combine(TransferDirectory,nonce+".bd2slot"));
            complete = true;
        }
        DailyJson.Write(Path.Combine(TransferDirectory,nonce+".json"),new ExportResult(account,nonce,complete));
    }

    internal static void Collect(string account, SessionVault vault, string executable)
        => CollectAsync(account,vault,executable,CancellationToken.None).GetAwaiter().GetResult();

    internal static async Task CollectAsync(string account, SessionVault vault, string executable, CancellationToken token)
    {
        DailySandbox.RequireHost();
        DailySandbox.RequireAccountAvailable(account);
        string box = DailySandbox.BoxName(account), state = Path.Combine(DailySandbox.Store,box);
        if (!Directory.Exists(state)) return;
        var registered = await DailySandboxRegistration.EnsureAsync(account,token).ConfigureAwait(false);
        if (registered == null) return; // An unused directory has no credentials to collect.
        string root = Path.Combine(state,"root");
        string local = Path.Combine(root,"user","current","AppData","Local");
        var binding = DailyJson.TryRead<DailySandboxBinding>(Path.Combine(local,"BD2DailyAssistant","sandbox-binding.json"));
        if (binding == null) return; // Interrupted before the first bootstrap completed.
        if (binding.Account != account || binding.Box != box) throw new InvalidDataException("隔离账号绑定不一致，未读取凭据。");
        string installation = DailySandbox.Installation() ?? throw new InvalidOperationException("无法找到 Sandboxie，尚未收回隔离窗口的登录状态。");
        string actual = (await DailySandbox.Run(installation,"SbieIni.exe",["query",box,"FileRootPath"],token).ConfigureAwait(false)).Trim();
        if (!string.Equals(root,actual,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("隔离目录已变化，未读取凭据。");
        string nonce=Guid.NewGuid().ToString("N"), directory=Path.Combine(local,SessionConstants.VaultDirectoryName,"transfers");
        string sessionPath=Path.Combine(directory,nonce+".bd2slot"), resultPath=Path.Combine(directory,nonce+".json");
        try
        {
            await DailySandbox.Run(installation,"Start.exe",["/box:"+box,"/silent","/wait",Path.GetFullPath(executable),ExportSwitch,account,nonce],token).ConfigureAwait(false);
            var result=DailyJson.TryRead<ExportResult>(resultPath);
            if(result?.Account!=account || result.Nonce!=nonce) throw new IOException("未收到隔离登录状态，已停止启动，避免使用旧凭据。");
            if (!result.Complete) return; // Never revive credentials disabled by the game.
            var returned=vault.ReadLaunchSnapshot(sessionPath);
            if(DailyIdentity.MemberKey(SessionIdentity.GetMemberId(returned)??"")!=account) throw new InvalidDataException("返回的登录凭据不属于所选账号。");
            new SessionService(vault).AcceptTransferred(returned,SessionIdentity.GetMemberId(returned)!);
        }
        finally { File.Delete(sessionPath); File.Delete(resultPath); }
    }
}