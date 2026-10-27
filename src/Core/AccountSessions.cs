using Dustweave.Accounts;
namespace Dustweave;

public sealed record DailyAccount(int SlotNumber, string Name, string AccountKey, string MaskedMemberId, bool Valid, bool IsCurrent, string Error)
{
    public string UnreadableRevision { get; init; } = "";
}
public sealed record DailyLocalLoginEntry(string Name, bool Present, string? Kind, bool HasData, bool? Enabled);
public sealed record DailyLocalLoginState(bool RegistryPresent, string IdentitySource, IReadOnlyList<DailyLocalLoginEntry> Entries);
public sealed record DailyAccountCatalog(IReadOnlyList<DailyAccount> Accounts, string CurrentKey, int? CurrentSlot, bool SessionComplete, bool GameRunning, bool StarterRunning, bool HasRecovery)
{
    public IReadOnlyList<int> OccupiedSlots { get; init; } = [];
    public DailyLocalLoginState? LocalLogin { get; init; }
}
public interface IAccountSessions
{
    DailyAccountCatalog Read();
    void EnsureControl();
    void ActivateAndLaunch(int slot, string expectedKey);
    void Save(int slot, string name, string expectedKey);
    void Rename(int slot, string name, string expectedKey);
    void Delete(int slot, string expectedKey);
    void DeleteUnreadable(int slot, string expectedRevision) => throw new NotSupportedException();
    void LaunchCurrent() => throw new NotSupportedException();
    void LoginNew();
    void Recover();
    DailyLoginRecovery? LoginRecovery => null;
    void BeginLoginRecovery(int slot, string key) => throw new NotSupportedException();
    DailyLoginRecovery FinishLoginRecovery(bool launchOrdinary = false) => throw new NotSupportedException();
    void CancelLoginRecovery() => throw new NotSupportedException();
}
// Reuses the original session service and vault. No session bytes leave this adapter.
public sealed class AccountSessions : IAccountSessions, IDisposable
{
    private readonly IGameHost? host;
    private readonly DailyLiveAccountIdentity liveIdentity = new();
    public AccountSessions() : this(null) { }
    public AccountSessions(IGameHost? host) => this.host = host;
    private readonly SessionService service = new(); private readonly SessionVault vault = new(); private Mutex? ownership;
    public string VaultDirectory => vault.RootDirectory;
    public bool TryAcquire()
    {
        if (ownership != null)
            return true;
        var candidate = new Mutex(true, DailyApplication.AccountMutex, out bool created);
        if (!created)
        {
            candidate.Dispose();
            return false;
        }
        ownership = candidate;
        return true;
    }
    private void Own()
    {
        if (!TryAcquire())
            throw new InvalidOperationException("原账户切换工具正在运行。请先关闭它，再使用这里的账户操作。");
    }
    public void EnsureControl() => Own();
    public void LaunchCurrent()
    {
        DailySandbox.RequireHost();
        Own();
        var status = SessionRegistry.GetStatus();
        if (status.GameRunning || status.StarterRunning) throw new InvalidOperationException("onboarding.close_launcher");
        string executable = GameLauncher.ResolveExecutable();
        GameLauncher.ValidateLaunchContext(executable);
        GameLauncher.LaunchDirect(executable);
    }
    public DailyAccountCatalog Read()
    {
        var d = service.GetDashboard();
        var accounts = new List<DailyAccount>();
        foreach (var slot in d.Slots.Where(s => s.Occupied))
        {
            string key = "";
            bool valid = slot.Valid;
            string error = slot.Error ?? "";
            if (valid)
                try
                {
                    key = DailyIdentity.MemberKey(SessionIdentity.GetMemberId(vault.LoadFixedSlot(slot.Number)) ?? "");
                    valid = key.Length > 0;
                    if (!valid)
                        error = "槽位没有有效的成员身份";
                }
                catch { valid = false; error = "槽位暂时无法读取"; }
            string unreadableRevision = "";
            if (!valid)
                try { unreadableRevision = vault.UnreadableFixedSlotRevision(slot.Number); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            accounts.Add(new(slot.Number, slot.DisplayName ?? "未命名", key, slot.MaskedMemberId ?? "", valid, slot.IsCurrent, error)
            { UnreadableRevision = unreadableRevision });
        }
        string current = "";
        try { current = DailyIdentity.MemberKey(SessionRegistry.ReadMemberIdentity()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        string source = DailyProfiles.ValidKey(current) ? "local-member" : "unavailable";
        if (host != null && !DailyProfiles.ValidKey(current))
        {
            try { current = liveIdentity.Resolve(host.Find(), host.ReadSnapshot(), DateTimeOffset.UtcNow); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException)
            { liveIdentity.Resolve(null, null, DateTimeOffset.UtcNow); }
            if (DailyProfiles.ValidKey(current)) source = "live-game";
        }
        if (DailySandbox.Current is {} binding) accounts = accounts.Where(a => a.AccountKey == binding.Account).ToList();
        var catalog = new DailyAccountCatalog(accounts, current, d.CurrentSlotNumber, d.CurrentSessionComplete, d.Status.GameRunning, d.Status.StarterRunning, d.HasRecovery)
        {
            LocalLogin = new(d.Status.RegistryKeyPresent, source, d.Status.Entries.Select(e => new DailyLocalLoginEntry(e.LogicalName, e.Present, e.Kind, e.ByteLength > 0, e.Enabled)).ToArray())
        };
        try { catalog = DailyAccountIdentity.WithRecoveryHints(catalog, new DailyProfiles(DailyIdentity.DataRoot).Read()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return DailyAccountIdentity.Normalize(catalog);
    }
    private void RequireIdentity(int slot, string key)
    {
        DailySandbox.RequireBoundAccount(key);
        var actual = DailyIdentity.MemberKey(SessionIdentity.GetMemberId(vault.LoadFixedSlot(slot)) ?? "");
        if (!DailyProfiles.ValidKey(key) || actual != key)
            throw new InvalidOperationException("这个槽位的账号已改变，请刷新列表后重新选择。");
    }
    public void ActivateAndLaunch(int slot, string expectedKey)
    {
        Own();
        RequireIdentity(slot, expectedKey);
        DailySandbox.RequireAccountAvailable(expectedKey);
        var state = SessionRegistry.GetStatus();
        if (state.GameRunning || state.StarterRunning)
            throw new InvalidOperationException("请先关闭游戏和启动器，再切换登录账号。");
        if (Dustweave.Accounts.SandboxProcessScope.CurrentBox.Length == 0)
        {
            service.SynchronizeCurrentSlot();
            DailySandboxSessions.CollectForHost(expectedKey, vault, Environment.ProcessPath!);
        }
        service.UseSlotAndLaunch(slot);
    }
    public void Save(int slot, string name, string expectedKey)
    {
        DailySandbox.RequireHost();
        Own();
        var plan = DailyAccountIdentity.SavePlan(Read());
        if (plan.SlotNumber != slot || plan.AccountKey != expectedKey)
            throw new InvalidOperationException("保存期间账号或槽位发生变化，请重新保存；未覆盖任何账号。");
        string member = SessionIdentity.GetMemberId(SessionRegistry.ReadCurrent("save-identity")) ?? "";
        if (DailyIdentity.MemberKey(member) != expectedKey)
            throw new InvalidOperationException("保存期间登录身份已改变，请重新保存。");
        service.SaveCurrentToSlot(slot, name, member, plan.ReplacesUnreadable);
    }
    public void Rename(int slot, string name, string expectedKey)
    {
        DailySandbox.RequireHost();
        Own();
        RequireIdentity(slot, expectedKey);
        service.RenameSlot(slot, name);
    }
    public void Delete(int slot, string expectedKey)
    {
        DailySandbox.RequireHost();
        Own();
        RequireIdentity(slot, expectedKey);
        service.DeleteSlot(slot);
    }
    public void DeleteUnreadable(int slot, string expectedRevision)
    {
        DailySandbox.RequireHost();
        Own();
        service.DeleteUnreadableSlot(slot, expectedRevision);
    }
    public void LoginNew()
    {
        DailySandbox.RequireHost();
        Own();
        var d = service.GetDashboard();
        if (d.CurrentSessionComplete)
            service.PrepareNewLoginAndLaunch();
        else
            service.LaunchPreparedLogin();
    }
    public void Recover()
    {
        DailySandbox.RequireHost();
        Own();
        service.RecoverPreviousSession(launch: false);
    }
    public DailyLoginRecovery? LoginRecovery => DailyLoginRecovery.Read(DailyIdentity.DataRoot);
    public void BeginLoginRecovery(int slot, string key)
    {
        DailySandbox.RequireHost(); Own(); RequireIdentity(slot, key);
        DailySandbox.RequireAccountAvailable(key);
        var saved = SessionService.LatestSameAccount(vault.LoadFixedSlot(slot), vault.TryLoadFixedSlot);
        service.PrepareManualLogin();
        DailyJson.Write(DailyLoginRecovery.PathFor(DailyIdentity.DataRoot), new DailyLoginRecovery(slot, key,
            saved.Alias, SessionRegistry.AuthenticationStamp(saved), DateTimeOffset.UtcNow));
        // No collection/import of the disabled sandbox session in this route.
        service.LaunchPreparedLogin();
    }
    public DailyLoginRecovery FinishLoginRecovery(bool launchOrdinary = false)
    {
        DailySandbox.RequireHost(); Own();
        var recovery = LoginRecovery ?? throw new InvalidOperationException("account.relogin.no_pending");
        RequireIdentity(recovery.Slot, recovery.Account);
        DailySandbox.RequireAccountAvailable(recovery.Account);
        var current = SessionRegistry.Capture("manual-login-recovery");
        recovery.ValidateReplacement(current);
        service.SaveCurrentToSlot(recovery.Slot, recovery.Name, SessionIdentity.GetMemberId(current));
        // This explicit, freshly authenticated retry must not depend on the
        // sandbox exporter that caused the user to enter recovery in the first place.
        if (launchOrdinary) service.UseSlotAndLaunch(recovery.Slot);
        File.Delete(DailyLoginRecovery.PathFor(DailyIdentity.DataRoot));
        return recovery;
    }
    public void CancelLoginRecovery()
    {
        DailySandbox.RequireHost(); Own();
        File.Delete(DailyLoginRecovery.PathFor(DailyIdentity.DataRoot));
    }
    public void Dispose()
    {
        ownership?.Dispose();
        ownership = null;
    }
}
