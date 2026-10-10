namespace Dustweave;

public sealed record DailyIssue(string Code, string Category);
public static class DailyIssues
{
    public static DailyIssue Classify(Exception error) => error is OperationCanceledException ? new("cancelled", "stopped") : Classify(error.GetBaseException().Message);
    public static DailyIssue Classify(string raw)
    {
        string text = raw ?? "";
        bool Has(params string[] words) => words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (text.StartsWith("trade.read.configuration:", StringComparison.Ordinal)) return new("trade-configuration", "connection");
        if (text.StartsWith("trade.read.native:", StringComparison.Ordinal)) return new("trade-read", "game");
        if (Has("no replay", "uncertain", "结果尚未确认", "结果未知", "结果不明", "未确认消费", "unresolved", "requires reconciliation")) return new("uncertain", "result");
        if (Has("field.not-ready:", "field.route-interrupted:", "Need one observed GameFieldDefaultUI; found 0")) return new("field-transition", "game");
        if (Has("suite.owner-exited", "suite.owner-replaced", "suite.owner-missing")) return new("owner-ended", "connection");
        if (Has("suite.owner-unreadable")) return new("owner-unreadable", "permission");
        if (Has("connection.login_identity_unavailable")) return new("live-login-unavailable", "identity");
        if (Has("account.local_login_incomplete", "尚未找到完整登录会话", "当前注册表会话不完整", "没有可保存的完整登录身份")) return new("local-login-incomplete", "identity");
        if (Has("suite.identity-unavailable", "login-required", "游戏身份尚未就绪", "Waiting for fresh account identity")) return new("identity-unavailable", "identity");
        if (Has("suite.account-changed", "suite.player-changed", "account-changed", "账号与目标账号不一致", "角色身份发生变化", "连接期间账号已切换")) return new("identity-changed", "identity");
        if (Has("suite.control-changed", "功能控制权已变化", "switch-already-pending", "此工具尚未恢复就绪")) return new("handoff", "connection");
        if (Has("会话已停止，请在日常助手重新选择功能", "对话已停止", "游戏身份已变化或连接已过期")) return new("legacy-session", "connection");
        if (Has("game-changed", "游戏已退出", "游戏进程已变化", "游戏会话已结束")) return new("game-ended", "connection");
        if (Has("登录失效", "令牌无效", "登入凭据不可用", "登录凭据不可用")) return new("login-expired", "identity");
        if (Has("module-", "组件未就绪", "组件尚未就绪", "MissingMethodException", "TypeLoadException", "Failed to resolve assembly", "Missing read member", "unsupported response")) return new("component", "compatibility");
        if (Has("Access to the path", "Permission denied", "UnauthorizedAccessException", "拒绝访问", "拒绝存取")) return new("access", "permission");
        if (Has("unknown popup", "unrecognized popup", "未识别弹窗", "未知确认框", "Foreground popup")) return new("popup", "game");
        if (Has("timed out", "timeout", "超时", "连接已失效", "连接已过期")) return new("timeout", "connection");
        return new("unknown", "unknown");
    }
}
