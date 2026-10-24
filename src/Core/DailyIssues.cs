namespace Dustweave;

public sealed record DailyIssue(string Code, string Category);
public static class DailyIssues
{
    public static DailyIssue Classify(Exception error) => error is DailyHelperException helper ? helper.Failure.Issue
        : error is DailyGameExitedException ? new("game-ended", "stopped")
        : error is OperationCanceledException ? new("cancelled", "stopped")
        : error.ToString().Contains("connection.scope_permission", StringComparison.Ordinal) ? new("scope-permission", "permission")
        : error.Message.StartsWith("管理员连接组件通信中断", StringComparison.Ordinal) ? new("elevation-pipe", "connection") : Classify(error.GetBaseException().Message);
    public static DailyIssue Classify(string raw)
    {
        string text = raw ?? "";
        bool Has(params string[] words) => words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (Has("connection.scope_permission")) return new("scope-permission", "permission");
        if (Has("connection.scope_changed")) return new("identity-changed", "identity");
        if (Has("connection.preparation_failed")) return new("preparation", "connection");
        if (Has("日常网络回执监听未恢复", "日常连接组件报告异常", "游戏观察数据未及时更新", "日常回执监听配置尚未生效", "尚未收到日常连接组件的数据")) return new("preparation", "connection");
        if (Has("已取消管理员连接授权")) return new("cancelled", "stopped");
        if (Has("DPAPI", "无法由当前 Windows 用户解密")) return new("credentials-unreadable", "identity");
        if (Has("原队列与当前账号、角色或重置周期不一致", "原队列的账号、进程或重置周期已变化")) return new("resume-context", "identity");
        if (text.StartsWith("trade.read.configuration:", StringComparison.Ordinal)) return new("trade-configuration", "connection");
        if (text.StartsWith("trade.read.native:", StringComparison.Ordinal)) return new("trade-read", "game");
        if (Has("管理员连接组件通信中断")) return new("elevation-pipe", "connection");
        if (Has("no replay", "uncertain", "结果尚未确认", "结果未知", "结果不明", "未确认消费", "unresolved", "requires reconciliation")) return new("uncertain", "result");
        if (Has("NullReferenceException", "Object reference not set to an instance of an object", "未将对象引用设置到对象的实例")) return new("internal-error", "unknown");
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

// A helper's exit code and final stack-trace line cannot explain a preparation
// failure. Preserve a bounded structured error alongside the complete stderr.
public sealed record DailyHelperFailure(int Schema, string Message, DailyIssue Issue, string ExceptionType, int HResult)
{
    public const string Prefix = "DUSTWEAVE_HELPER_ERROR:";
    public static string Encode(Exception error)
    {
        string message = error.ToString().Contains("connection.scope_permission", StringComparison.Ordinal)
            ? "connection.scope_permission" : error.Message;
        if (message.Length > 2048) message = message[..2048];
        return Prefix + System.Text.Json.JsonSerializer.Serialize(new DailyHelperFailure(1, message,
            DailyIssues.Classify(error), error.GetType().FullName ?? error.GetType().Name, error.HResult));
    }
    public static DailyHelperFailure? Read(string line)
    {
        if (!line.StartsWith(Prefix, StringComparison.Ordinal) || line.Length > 16384) return null;
        try
        {
            var value = System.Text.Json.JsonSerializer.Deserialize<DailyHelperFailure>(line[Prefix.Length..]);
            return value is { Schema: 1, Message.Length: > 0 and <= 2048, Issue.Code.Length: > 0 and <= 80,
                Issue.Category.Length: > 0 and <= 80, ExceptionType.Length: > 0 and <= 256 } ? value : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }
    public Exception ToException() => new DailyHelperException(this);
}

public sealed class DailyHelperException(DailyHelperFailure failure) : InvalidOperationException(failure.Message)
{
    public DailyHelperFailure Failure { get; } = failure;
}
