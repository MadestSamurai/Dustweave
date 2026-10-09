namespace Dustweave.Accounts;

internal sealed class SessionService
{
    internal const int SlotCount = SessionConstants.FixedSlotCount;

    private readonly SessionVault _vault;
    internal SessionService(SessionVault? vault = null) => _vault = vault ?? new();

    internal string VaultDirectory => _vault.RootDirectory;

    internal SessionDashboardState GetDashboard()
    {
        SessionStatus status = SessionRegistry.GetStatus();
        SessionSlot? current = null;
        string? currentFingerprint = null;
        string? currentMaskedMemberId = null;
        string? currentMemberId = null;
        if (status.Complete)
        {
            try
            {
                current = SessionRegistry.ReadCurrent("current-session");
                currentFingerprint = _vault.Fingerprint(current);
                currentMaskedMemberId = SessionIdentity.GetMaskedMemberId(current);
                currentMemberId = SessionIdentity.GetMemberId(current);
            }
            catch (SessionManagerException)
            {
                current = null;
            }
        }

        List<FixedSlotState> slots = [];
        int? currentSlotNumber = null;
        string currentDisplayName = current is null
            ? status.GameRunning || status.StarterRunning
                ? "正在建立登录会话"
                : "尚未登录"
            : "未保存的新账户";

        for (int number = 1; number <= SlotCount; number++)
        {
            if (!_vault.FixedSlotExists(number))
            {
                slots.Add(new FixedSlotState(
                    number,
                    Occupied: false,
                    Valid: true,
                    DisplayName: null,
                    CapturedAtUtc: null,
                    Fingerprint: null,
                    MaskedMemberId: null,
                    IsCurrent: false,
                    Error: null));
                continue;
            }

            try
            {
                SessionSlot slot = _vault.LoadFixedSlot(number);
                string? slotMemberId = SessionIdentity.GetMemberId(slot);
                bool isCurrent = current is not null
                    && (SessionRegistry.SessionsEqual(current, slot)
                        || (!string.IsNullOrEmpty(currentMemberId)
                            && string.Equals(currentMemberId, slotMemberId, StringComparison.Ordinal)));
                if (isCurrent && !currentSlotNumber.HasValue)
                {
                    currentSlotNumber = number;
                    currentDisplayName = slot.Alias;
                }

                slots.Add(new FixedSlotState(
                    number,
                    Occupied: true,
                    Valid: true,
                    DisplayName: slot.Alias,
                    CapturedAtUtc: slot.CapturedAtUtc,
                    Fingerprint: _vault.Fingerprint(slot),
                    MaskedMemberId: SessionIdentity.GetMaskedMemberId(slot),
                    IsCurrent: isCurrent,
                    Error: null));
            }
            catch
            {
                slots.Add(new FixedSlotState(
                    number,
                    Occupied: true,
                    Valid: false,
                    DisplayName: "槽位数据不可用",
                    CapturedAtUtc: null,
                    Fingerprint: null,
                    MaskedMemberId: null,
                    IsCurrent: false,
                    Error: "无法由当前 Windows 用户解密或文件已损坏"));
            }
        }

        return new SessionDashboardState(
            status,
            CurrentSessionComplete: current is not null,
            CurrentSlotNumber: currentSlotNumber,
            CurrentDisplayName: currentDisplayName,
            CurrentMaskedMemberId: currentMaskedMemberId,
            CurrentFingerprint: currentFingerprint,
            HasRecovery: _vault.HasRecovery,
            Slots: slots);
    }

    internal SlotSummary SaveCurrentToSlot(int slotNumber, string displayName, string? expectedMemberId = null)
    {
        SessionSlot current = PreserveCredentialAge(SessionRegistry.Capture(displayName));
        SessionSlot selected = LatestSameAccount(current, _vault.TryLoadFixedSlot);
        var result = SaveCapturedToSlot(selected, slotNumber, displayName, expectedMemberId,
            _vault.TryLoadFixedSlot, _vault.SaveFixedSlot);
        _vault.RememberObserved(current);
        return result;
    }

    // Tested with an isolated in-memory vault; the production path uses the same
    // decision and the vault's encrypted atomic write/read-back verification.
    internal static SlotSummary SaveCapturedToSlot(SessionSlot current, int slotNumber,
        string displayName, string? expectedMemberId, Func<int, SessionSlot?> load,
        Func<int, SessionSlot, string, bool, SlotSummary> save)
    {
        if (slotNumber < 1 || slotNumber > SlotCount) throw new SessionManagerException("账户槽位超出范围。");
        string? member = SessionIdentity.GetMemberId(current);
        if (string.IsNullOrEmpty(member)) throw new SessionManagerException("无法确认当前账号的完整成员 ID，未保存。");
        if (expectedMemberId != null && member != expectedMemberId)
            throw new SessionManagerException("保存期间登录身份已改变，未覆盖原账号。");
        for (int number = 1; number <= SlotCount; number++)
        {
            SessionSlot? existing;
            try { existing = load(number); } catch { continue; }
            if (existing != null && SessionIdentity.GetMemberId(existing) == member)
                return save(number, current, number == slotNumber ? displayName : existing.Alias, true);
        }
        if (load(slotNumber) != null) throw new SessionManagerException($"账户槽位 {slotNumber} 已被其他账号占用。");
        return save(slotNumber, current, displayName, false);
    }

    internal static SessionSlot LatestSameAccount(SessionSlot saved, Func<int, SessionSlot?> load)
    {
        string? member = SessionIdentity.GetMemberId(saved);
        if (string.IsNullOrEmpty(member)) return saved;
        var latest = saved;
        var candidates = new List<SessionSlot> { saved };
        for (int number = 1; number <= SlotCount; number++)
        {
            SessionSlot? candidate;
            try { candidate = load(number); } catch { continue; }
            if (candidate == null || SessionIdentity.GetMemberId(candidate) != member) continue;
            candidates.Add(candidate);
            if (candidate.CapturedAtUtc > latest.CapturedAtUtc) latest = candidate;
        }
        if (latest.Agreement == null)
        {
            var record = candidates.OrderByDescending(x => x.CapturedAtUtc)
                .FirstOrDefault(x => x.Agreement != null && SessionRegistry.SameAuthentication(x, latest));
            if (record != null) latest = latest with { Agreement = record.Agreement };
        }
        return latest with { Alias = saved.Alias };
    }

    internal SlotSummary RenameSlot(int slotNumber, string displayName)
    {
        return _vault.RenameFixedSlot(slotNumber, displayName);
    }

    internal ActivationResult UseSlotAndLaunch(int slotNumber)
    {
        GameLauncher.ValidateLaunchContext();
        SynchronizeCurrentSlot();
        SessionSlot saved = _vault.LoadFixedSlot(slotNumber);
        SessionSlot target = LatestSameAccount(saved, _vault.TryLoadFixedSlot);
        if (!SessionRegistry.SessionsEqual(saved, target))
            _vault.SaveFixedSlot(slotNumber, target, saved.Alias, replace: true);
        SessionStatus status = SessionRegistry.GetStatus();
        ActivationResult activation = SessionSwitcher.Activate(
            _vault,
            target,
            allowIncompleteCurrent: !status.Complete);
        _vault.RememberObserved(target);
        GameLauncher.LaunchDirect();
        return activation;
    }

    internal SlotSummary PrepareNewLogin()
    {
        SynchronizeCurrentSlot();
        SessionDashboardState dashboard = GetDashboard();
        if (dashboard.Status.GameRunning || dashboard.Status.StarterRunning)
        {
            throw new SessionManagerException("请先直接关闭游戏和启动器，再准备登录新账户。");
        }

        if (!dashboard.CurrentSlotNumber.HasValue)
        {
            throw new SessionManagerException("当前账户尚未保存到槽位，不能安全准备新登录。");
        }

        if (dashboard.Slots.All(slot => slot.Occupied))
        {
            throw new SessionManagerException(
                $"{SlotCount} 个账户槽位均已占用，请先删除一个不再使用的槽位。");
        }

        SessionSlot preserved = _vault.LoadFixedSlot(dashboard.CurrentSlotNumber.Value);
        return SessionSwitcher.PrepareLogin(_vault, preserved);
    }

    internal int PrepareNewLoginAndLaunch()
    {
        GameLauncher.ValidateLaunchContext();
        PrepareNewLogin();
        return GameLauncher.LaunchDirect();
    }

    internal int LaunchPreparedLogin()
    {
        SessionDashboardState dashboard = GetDashboard();
        if (dashboard.Status.GameRunning || dashboard.Status.StarterRunning)
        {
            throw new SessionManagerException("游戏或启动器仍在运行，无需重复启动。");
        }

        if (dashboard.CurrentSessionComplete)
        {
            throw new SessionManagerException(
                "当前已经存在完整账户会话，请使用对应槽位启动游戏。");
        }

        return GameLauncher.LaunchDirect();
    }

    internal ActivationResult RecoverPreviousSession(bool launch)
    {
        if (launch) GameLauncher.ValidateLaunchContext();
        SessionStatus status = SessionRegistry.GetStatus();
        if (status.GameRunning || status.StarterRunning)
        {
            throw new SessionManagerException("请先直接关闭游戏和启动器，再恢复账户。");
        }

        SessionSlot recovery = _vault.LoadRecovery();
        ActivationResult activation = SessionSwitcher.Activate(
            _vault,
            recovery,
            allowIncompleteCurrent: true);
        _vault.RememberObserved(recovery);
        if (launch)
        {
            GameLauncher.LaunchDirect();
        }

        return activation;
    }

    internal void DeleteSlot(int slotNumber)
    {
        SessionDashboardState dashboard = GetDashboard();
        if (dashboard.Status.GameRunning || dashboard.Status.StarterRunning)
        {
            throw new SessionManagerException("请先直接关闭游戏和启动器，再删除账户槽位。");
        }

        if (!dashboard.CurrentSessionComplete)
        {
            throw new SessionManagerException("当前没有完整活动会话。请先完成新账户登录或恢复原账户，再删除槽位。");
        }

        if (dashboard.CurrentSlotNumber == slotNumber)
        {
            throw new SessionManagerException("不能删除当前活动账户的唯一槽位，请先切换到其他账户。");
        }

        _vault.DeleteFixedSlot(slotNumber);
    }

    internal SlotSummary MigrateLegacySlot(
        string legacyAlias,
        int slotNumber,
        string displayName)
    {
        return _vault.MigrateLegacySlot(legacyAlias, slotNumber, displayName);
    }

    internal int? SynchronizeCurrentSlot()
    {
        SessionStatus status = SessionRegistry.GetStatus();
        if (!status.Complete || status.GameRunning || status.StarterRunning)
        {
            return null;
        }

        SessionSlot current = SessionRegistry.Capture("current-session");
        return SynchronizeCaptured(current);
    }

    // Observe the registry separately from the shared vault: after accepting a
    // renewed sandbox session, unchanged host registry bytes must not win it back.
    internal int? SynchronizeCaptured(SessionSlot current)
    {
        SessionRegistry.ValidateSlot(current);
        string? member = SessionIdentity.GetMemberId(current);
        if (string.IsNullOrEmpty(member)) return null;
        var matching = new List<(int Number, SessionSlot Slot)>();
        for (int n = 1; n <= SlotCount; n++)
        {
            SessionSlot? s;
            try { s = _vault.TryLoadFixedSlot(n); } catch { continue; }
            if (s != null && SessionIdentity.GetMemberId(s) == member) matching.Add((n,s));
        }
        if (matching.Count == 0) return null;
        var observed = _vault.ReadObserved();
        if (observed != null && SessionRegistry.SameAuthentication(current, observed) && current.Agreement == observed.Agreement) return matching[0].Number;
        // A known token read from another context retains its actual capture age.
        var captured = PreserveCredentialAge(current);
        AcceptTransferred(captured, member);
        _vault.RememberObserved(captured);
        return matching[0].Number;
    }

    // Reading/saving the same credential again must not make it newer than a
    // token renewed in another instance. Metadata-only changes retain its age.
    internal SessionSlot PreserveCredentialAge(SessionSlot current)
    {
        SessionSlot? known = _vault.ReadObserved();
        if (known == null || !SessionRegistry.SameAuthentication(known, current)) known = null;
        for (int n = 1; n <= SlotCount; n++)
        {
            SessionSlot? candidate;
            try { candidate = _vault.TryLoadFixedSlot(n); } catch { continue; }
            if (candidate != null && SessionRegistry.SameAuthentication(candidate, current)
                && (known == null || candidate.CapturedAtUtc < known.CapturedAtUtc)) known = candidate;
        }
        return known == null ? current : current with { CapturedAtUtc = known.CapturedAtUtc };
    }
    internal void AcceptTransferred(SessionSlot incoming, string expectedMember)
    {
        SessionRegistry.ValidateSlot(incoming);
        if (SessionIdentity.GetMemberId(incoming) != expectedMember)
            throw new SessionManagerException("登录凭据与所选账号不一致，未覆盖已保存账号。");
        for (int n = 1; n <= SlotCount; n++)
        {
            var saved = _vault.TryLoadFixedSlot(n);
            if (saved == null || SessionIdentity.GetMemberId(saved) != expectedMember) continue;
            var latest = LatestSameAccount(saved, _vault.TryLoadFixedSlot);
            if (SessionRegistry.SameAuthentication(incoming,latest))
            {
                if (incoming.Agreement != null && incoming.Agreement != latest.Agreement)
                    _vault.SaveFixedSlot(n, latest with { Agreement = incoming.Agreement }, saved.Alias, true);
                return;
            }
            if (incoming.CapturedAtUtc < latest.CapturedAtUtc) return;
            if (incoming.CapturedAtUtc == latest.CapturedAtUtc)
                throw new SessionManagerException("同一时刻存在不同的登录凭据，已保留原账号；请重新登录后保存。");
            _vault.SaveFixedSlot(n,incoming,saved.Alias,true);
            return;
        }
        throw new SessionManagerException("要同步的账号已不存在，未新建或覆盖其他槽位。");
    }
    internal SlotSummary UpdateSlotFromRecovery(int slotNumber)
    {
        SessionSlot existing = _vault.LoadFixedSlot(slotNumber);
        SessionSlot recovery = _vault.LoadRecovery();
        string? existingMemberId = SessionIdentity.GetMemberId(existing);
        string? recoveryMemberId = SessionIdentity.GetMemberId(recovery);
        if (string.IsNullOrEmpty(existingMemberId)
            || !string.Equals(existingMemberId, recoveryMemberId, StringComparison.Ordinal))
        {
            throw new SessionManagerException("恢复点与目标账户槽位不属于同一成员 ID。");
        }

        return _vault.SaveFixedSlot(
            slotNumber,
            recovery,
            existing.Alias,
            replace: true);
    }

}
