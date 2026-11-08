namespace Dustweave;

public static class DailyLoginDiagnostics
{
    public static void Capture(string root, string phase, DailyAccountCatalog catalog, IGameHost host, IAccountSessions sessions, string attemptId = "")
    {
        GameInstance? game = null; DailyIdentityRead read = new(null, "game-missing"); DailyRuntimeStatus? status = null;
        DailyAccountCatalog? current = null; string catalogError = "";
        try { current = sessions.Read(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException)
        { catalogError = e.GetType().Name; }
        try { game = host.Find(); if(game != null) { read = host.ReadIdentity(game); status = host.ReadStatus(game); } }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException)
        { read = read with { Problem = e.GetType().Name }; }
        Record(root, phase, current, game, read.Snapshot, read.Problem, read.Detail, status, attemptId, catalogError);
    }
    // Fixed summaries only, never token bytes or the full game DTO.
    public static void Record(string root, string phase, DailyAccountCatalog? catalog, GameInstance? game, DailySnapshot? s,
        string readError = "", string readDetail = "", DailyRuntimeStatus? status = null, string attemptId = "", string catalogReadError = "")
    {
        bool terminalFile = phase is "request" or "verified" or "failed" or "preparation-failed";
        if (!terminalFile && phase is not ("waiting-agreement" or "waiting-credentials" or "waiting-start")) return;
        try
        {
            string scope;
            try { scope = Dustweave.Accounts.SandboxProcessScope.CurrentBox.Length == 0 ? "ordinary" : "isolated"; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { scope = "unavailable"; }
            var record = new
            {
                atUtc = DateTimeOffset.UtcNow, phase, attemptId, observerScope = scope, readError, readDetail, catalogReadError, SessionComplete = catalog?.SessionComplete,
                CurrentKey = catalog?.CurrentKey, LocalLogin = catalog?.LocalLogin,
                savedAccounts = catalog?.Accounts.Count(a => a.Valid), status,
                game = game == null ? null : new { game.ProcessId, game.StartTicks },
                observed = s == null ? null : new { s.Runtime, s.ProcessId, s.ProcessStartTicks, s.InstanceId, s.Sequence, s.FrameUtcTicks,
                    s.State, s.Scene, s.Loading, s.MainMenuReady, s.ErrorCode, s.AccountKey, s.PlayerKey, hasLease = !string.IsNullOrEmpty(s.LeaseOwner),
                    startupVisible = s.Startup?.Visible, startupStage = s.Startup?.Stage, startupBlock = s.Startup?.BlockReason,
                    identityReady = game != null && DailyIdentityGuard.Ready(s, game, DateTimeOffset.UtcNow) }
            };
            // Keep a small, current attempt even when the user exports while still
            // waiting. Old terminal files remain historical, identified by attempt.
            DailyJson.Write(Path.Combine(root, "live", "diagnostics", "login-current.json"), record);
            DailyJson.Write(Path.Combine(root, "live", "diagnostics", "login-" + phase + ".json"), record);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // Describe the last actual read; never infer logout or account mismatch from
    // a missing mailbox, stalled observer, or an unfinished scene load.
    public static string WaitReason(DailyIdentityRead read, GameInstance game, DateTimeOffset now)
    {
        var s = read.Snapshot;
        if (s == null) return read.Problem switch
        {
            "endpoint-unavailable" or "transport-timeout" or "transport-read" => "身份数据连接暂时不可用，请点击重新连接；未更改账号或沙盒。",
            "snapshot-invalid" or "snapshot-empty" => "收到的身份数据无法读取，请重新连接；反复出现时请导出诊断。",
            "process-mismatch" => "游戏进程已变化，请重新连接。",
            _ => "连接组件已就绪，但尚未收到游戏身份数据。请重新连接；反复出现时请导出诊断，无需删除账号或沙盒。"
        };
        if (s.ProcessId != game.ProcessId || s.ProcessStartTicks != game.StartTicks) return "游戏进程已变化，请重新连接。";
        if (!StartupPolicy.Fresh(s, now.UtcTicks)) return "游戏身份数据已停止刷新，请重新连接；未判定账号失效。";
        if (s.State == "waiting_sdk") return "游戏登录服务尚未初始化，请等待游戏完成启动后重试。";
        if (s.State == "read_error") return "读取游戏身份或启动状态失败，请重新连接；反复出现时请导出诊断。";
        if (s.Startup?.Visible == true || s.Scene is "Splash" or "ReGame" || s.State is "waiting_login" or "waiting_start" or "starting_game") return "游戏尚未完成登录，请在游戏内处理启动页或登录弹窗后重试。";
        if (s.Loading) return "游戏仍在加载场景，请等加载完成后重试。";
        if (s.State == "waiting_player" || string.IsNullOrEmpty(s.PlayerKey)) return "登录服务已就绪，角色信息尚未加载，请进入游戏后重试。";
        return "已读到游戏身份，但未能完成连接确认，请重新连接；未更改登录账号。";
    }
}
