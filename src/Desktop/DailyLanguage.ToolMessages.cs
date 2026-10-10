namespace Dustweave.Desktop;

public sealed partial class DailyLanguage
{
    // Exact, finite messages from the tool catalog. Do not rewrite names or IDs inside arbitrary text.
    private readonly Dictionary<string, (string Template, string Name)> toolMessages = BuildToolMessages();
    private static Dictionary<string, (string Template, string Name)> BuildToolMessages()
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var tool in DailyToolCatalog.All)
        {
            string name = "tool." + tool.Id;
            result.Add("已共用游戏连接 · " + tool.Name + "。请在工具窗口确认设置后开始。", ("tools.shared_ready", name));
            result.Add(tool.Name + "的独立窗口已经打开，请先使用或关闭原窗口。", ("tools.existing_window", name));
            result.Add(tool.Name + "未能启动，请查看工具诊断记录。", ("tools.start_failed", name));
            result.Add(tool.Name + "窗口已打开。未启用自动化时，可以直接运行日常或切换工具。", ("tools.window_ready", name));
            result.Add("复用已准备组件 · " + tool.Name, ("suite.reuse", name));
            result.Add("首次准备 · " + tool.Name, ("suite.prepare", name));
        }
        result.Add("复用已准备组件 · 日常执行", ("suite.reuse", "suite.daily"));
        result.Add("首次准备 · 日常执行", ("suite.prepare", "suite.daily"));
        result.Add("连接一次即可切换工具；打开工具后不会自动开始操作。", ("tools.idle_notice", ""));
        result.Add("工具共用日常助手的连接，不会自动开始操作。仅在自动化启用或等待结算时占用控制权。", ("tools.idle_notice", ""));
        return result;
    }
    private string TranslateToolMessage(string original) => toolMessages.TryGetValue(original, out var entry)
        ? entry.Name.Length == 0 ? Get(entry.Template) : Get(entry.Template, Get(entry.Name)) : original;
}
