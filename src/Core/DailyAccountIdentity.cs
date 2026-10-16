namespace Dustweave;

public sealed record DailyAccountSavePlan(int SlotNumber, string AccountKey, string Name, bool Existing)
{
    public bool ReplacesUnreadable { get; init; }
}

public static class DailyAccountIdentity
{
    // Full stable member identity determines the account. Rotating login tokens,
    // display names, masked suffixes and duplicate vault files do not create accounts.
    public static DailyAccountCatalog Normalize(DailyAccountCatalog value)
    {
        var occupied = value.OccupiedSlots.Concat(value.Accounts.Select(a => a.SlotNumber)).Distinct().Order().ToArray();
        var accounts = value.Accounts.Where(a => !a.Valid || !DailyProfiles.ValidKey(a.AccountKey))
            .Concat(value.Accounts.Where(a => a.Valid && DailyProfiles.ValidKey(a.AccountKey))
                .GroupBy(a => a.AccountKey, StringComparer.Ordinal).Select(g => g.OrderBy(a => a.SlotNumber).First()))
            .OrderBy(a => a.SlotNumber)
            .Select(a => a with { IsCurrent = a.Valid && a.AccountKey == value.CurrentKey && DailyProfiles.ValidKey(value.CurrentKey) })
            .ToArray();
        return value with { Accounts = accounts, OccupiedSlots = occupied,
            CurrentSlot = accounts.FirstOrDefault(a => a.IsCurrent)?.SlotNumber };
    }

    public static DailyAccountSavePlan SavePlan(DailyAccountCatalog value)
    {
        value = Normalize(value);
        if (value.GameRunning || value.StarterRunning)
            throw new InvalidOperationException("请先正常退出游戏和启动器，再保存最新登录凭据。");
        if (!value.SessionComplete || !DailyProfiles.ValidKey(value.CurrentKey))
            throw new InvalidOperationException("没有可保存的完整登录身份，请先完成登录。");
        var existing = value.Accounts.FirstOrDefault(a => a.Valid && a.AccountKey == value.CurrentKey);
        if (existing != null) return new(existing.SlotNumber, value.CurrentKey, existing.Name, true);
        var recoverable = value.Accounts.Where(a => !a.Valid && a.AccountKey == value.CurrentKey).ToArray();
        if (recoverable.Length == 1) return new(recoverable[0].SlotNumber, value.CurrentKey, recoverable[0].Name, true) { ReplacesUnreadable = true };
        int slot = Enumerable.Range(1, 100).FirstOrDefault(n => !value.OccupiedSlots.Contains(n));
        if (slot == 0) throw new InvalidOperationException("100 个账号槽位已满。");
        return new(slot, value.CurrentKey, $"账号 {slot:00}", false);
    }

    // Historical profiles may identify an unreadable slot for an explicit
    // replacement. They never make the old credentials valid for launch.
    public static DailyAccountCatalog WithRecoveryHints(DailyAccountCatalog value, IReadOnlyList<DailyAccountProfile> profiles)
        => value with { Accounts = value.Accounts.Select(a => {
            var matches = profiles.Where(p => p.SlotNumber == a.SlotNumber && DailyProfiles.ValidKey(p.AccountKey)).ToArray();
            return !a.Valid && matches.Length == 1 ? a with { AccountKey = matches[0].AccountKey,
                Name = matches[0].PlayerName.Length > 0 ? matches[0].PlayerName : a.Name } : a;
        }).ToArray() };

    public static bool SameCatalog(DailyAccountCatalog? previous, DailyAccountCatalog next)
        => previous != null && previous.CurrentKey == next.CurrentKey && previous.CurrentSlot == next.CurrentSlot
            && previous.SessionComplete == next.SessionComplete && previous.GameRunning == next.GameRunning
            && previous.StarterRunning == next.StarterRunning && previous.HasRecovery == next.HasRecovery
            && previous.Accounts.SequenceEqual(next.Accounts) && previous.OccupiedSlots.SequenceEqual(next.OccupiedSlots);
}
