using Dustweave.Accounts;
namespace Dustweave;

public sealed record DailyLoginRecovery(int Slot, string Account, string Name, string PreviousAuthentication, DateTimeOffset StartedUtc)
{
    internal static string PathFor(string root) => Path.Combine(root, "login-recovery.json");
    public static DailyLoginRecovery? Read(string root)
    {
        var value = DailyJson.TryRead<DailyLoginRecovery>(PathFor(root));
        return value is { Slot: >= 1 and <= 100 } && DailyProfiles.ValidKey(value.Account) ? value : null;
    }
    internal void ValidateReplacement(SessionSlot current)
    {
        SessionRegistry.ValidateSlot(current);
        if (DailyIdentity.MemberKey(SessionIdentity.GetMemberId(current) ?? "") != Account)
            throw new InvalidOperationException("account.relogin.wrong_account");
        if (SessionRegistry.AuthenticationStamp(current) == PreviousAuthentication || current.CapturedAtUtc < StartedUtc)
            throw new InvalidOperationException("account.relogin.unchanged");
    }
}
