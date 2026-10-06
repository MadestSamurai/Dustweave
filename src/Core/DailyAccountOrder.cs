namespace Dustweave;

public sealed record DailyAccountEntry(DailyAccount Account, DailyAccountProfile Profile);

public static class DailyAccountOrder
{
    // Saving preferences or verifying a login must use the same default rank as
    // an account without a profile. Only the explicit move action changes it.
    public static DailyAccountProfile ProfileFor(DailyAccount account, IReadOnlyCollection<DailyAccountProfile> saved)
        => saved.FirstOrDefault(p => p.AccountKey == account.AccountKey)
            ?? new DailyAccountProfile { AccountKey = account.AccountKey, SlotNumber = account.SlotNumber, Order = account.SlotNumber - 1 };

    public static IReadOnlyList<DailyAccountEntry> Arrange(IEnumerable<DailyAccount> accounts, IReadOnlyCollection<DailyAccountProfile> saved)
        => accounts.Select(a => new DailyAccountEntry(a, ProfileFor(a, saved)))
            .OrderBy(entry => entry.Profile.Order).ThenBy(entry => entry.Account.SlotNumber).ToArray();
}
