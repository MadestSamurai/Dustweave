namespace BD2Daily;

public static class DailyLoginFailure
{
    public static InvalidOperationException Mismatch(string root, string runId, DailyAccount target,
        DailySnapshot snapshot, DailyAccountCatalog catalog, bool startup)
    {
        var actual = catalog.Accounts.FirstOrDefault(a => a.Valid && a.AccountKey == snapshot.AccountKey);
        string expectedName = $"{target.Name}（槽位 {target.SlotNumber}）";
        string actualName = actual == null ? "未保存账号" : $"{actual.Name}（槽位 {actual.SlotNumber}）";
        if (!string.IsNullOrWhiteSpace(snapshot.PlayerName)) actualName += " · " + snapshot.PlayerName;
        bool storesDiffer = DailyProfiles.ValidKey(catalog.CurrentKey) && catalog.CurrentKey != snapshot.AccountKey;
        var diagnostic = new
        {
            atUtc = DateTimeOffset.UtcNow, runId, phase = startup ? "startup" : "player",
            classification = storesDiffer ? "tool_game_session_disagreement" : "different_logged_in_account",
            expected = new { target.SlotNumber, target.Name, target.AccountKey },
            actual = new { slotNumber = actual?.SlotNumber, name = actual?.Name, snapshot.AccountKey, snapshot.PlayerKey, snapshot.PlayerName },
            localSessionKey = catalog.CurrentKey, snapshot.ProcessId, snapshot.ProcessStartTicks,
            snapshot.InstanceId, snapshot.Sequence, snapshot.FrameUtcTicks, snapshot.Scene, snapshot.State,
            startupStage = snapshot.Startup?.Stage
        };
        // Only identity hashes and stage metadata; never persist registry values or credentials.
        try { DailyJson.Write(Path.Combine(root, "login-identity-error.json"), diagnostic); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        string help = storesDiffer
            ? "工具与游戏读取的登录会话不同。请从 Windows 资源管理器重新打开日常助手，再选择目标账号检查。"
            : "请确认勾选的目标账号，再重新执行登录检查。";
        return new InvalidOperationException($"登录账号不一致，已停止。目标：{expectedName}；游戏实际：{actualName}。{help}");
    }
}
