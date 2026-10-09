namespace Dustweave;

public static class DailyLoginReadiness
{
    public const string AgreementMessage = "等待你在游戏内确认服务协议；登录凭据仍保留，确认后会自动继续，无需重新保存账号。";
    public static bool NeedsAgreement(DailySnapshot? snapshot) => snapshot?.Startup?.Visible == true
        && snapshot.Startup.BlockReason.Split(new[] { '：', '、', ':' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(x => x.Trim() == "AgreementPopupUI");

    public static void Check(DailySnapshot? snapshot, GameInstance game, long now)
    {
        if (snapshot == null || snapshot.ProcessId != game.ProcessId || snapshot.ProcessStartTicks != game.StartTicks
            || !StartupPolicy.Fresh(snapshot, now)) return;
        if (snapshot.State == "read_error" && snapshot.ErrorCode == "NeonInitException")
            throw new InvalidOperationException("游戏登录 SDK 已缓存初始化失败。请更新日常工具后重新启动一次游戏；更改权限或反复连接无法清除此状态。");
    }
    public static bool CanRecover(DailySnapshot snapshot, string targetAccount) =>
        snapshot.State != "waiting_sdk" && snapshot.State != "read_error" && snapshot.AccountKey == targetAccount;
}
