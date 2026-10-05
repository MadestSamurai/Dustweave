namespace BD2Daily;

public sealed record DailyToolDefinition(string Id, string Name, string Category, string Description, string Repository, string AssemblyName, string MutexName);

public static class DailyToolCatalog
{
    public static IReadOnlyList<DailyToolDefinition> All { get; } = new DailyToolDefinition[]
    {
        new("fishing", "自动钓鱼", "经营与装备", "钓鱼、鱼饵、背包出售和地图往返。", "bd2-fishing", "BD2Fishing", @"Local\BD2Fishing.Desktop"),
        new("territory", "领地自动化", "经营与装备", "种植、采集、料理、库存出售和布局。", "bd2-territory", "BD2Territory", @"Local\BD2Territory.Desktop"),
        new("equipment", "装备助手", "经营与装备", "规划搓粉与批量精炼，确认预算后执行。", "bd2-equipment-assistant", "BD2EquipmentAssistant", @"Local\BD2EquipmentAssistant-v1"),
        new("sichuan", "连连看", "小游戏", "自动识别盘面、求解和续局，可调操作间隔。", "bd2-sichuan", "BD2Sichuan", @"Local\BD2Sichuan.Desktop"),
        new("rhythm", "音游", "小游戏", "读取当前谱面并操作，保留随机偏移设置。", "bd2-rhythm", "BD2Rhythm", @"Local\BD2Rhythm.Private.Desktop"),
        new("apostle-defense", "使徒运气防守", "小游戏", "自动召唤、合成、布局与结算续局。", "bd2-apostle-defense", "BD2ApostleDefense", @"Local\BD2ApostleDefense-v1"),
        new("secret-vision", "SECRET VISION", "小游戏", "自动圈地、避让与失败重试。", "bd2-secret-vision", "BD2SecretVision", @"Local\BD2SecretVisionAssistant"),
        new("fiend-hunter", "恶魔猎人", "小游戏", "自动移动、攻击决策和关卡重试。", "bd2-fiend-hunter", "BD2FiendHunter", @"Local\BD2FiendHunterDesktop"),
        new("infinite-gacha", "无限抽抽乐", "抽取", "A／B 双列表、停止条件和命中提醒。", "bd2-infinite-gacha", "BD2InfiniteGacha", @"Local\BD2InfiniteGacha-v1")
    };
    public static DailyToolDefinition Find(string id) => All.SingleOrDefault(t => t.Id == id)
        ?? throw new ArgumentException("未知工具：" + id);
    public static IEnumerable<DailyToolDefinition> Search(string text) => All.Where(t =>
        string.IsNullOrWhiteSpace(text) || (t.Name + " " + t.Category + " " + t.Description + " " + t.Repository).Contains(text.Trim(), StringComparison.OrdinalIgnoreCase));
}
/// <summary>Only real headless modes may omit the interactive hosted operation guard.</summary>
public static class DailyToolArguments
{
    public static bool TryClassify(string id, string[] args, out bool helper)
    {
        helper = false;
        if (!DailyToolCatalog.All.Any(t => t.Id == id)) return false;
        if (args.Length == 0 || id == "equipment" && args.SequenceEqual(new[] { "--refine" })) return true;
        helper = args[0] switch
        {
            "--smoke" => args.Length == 2,
            "--smoke-en" => args.Length == 2 && id is "territory" or "equipment",
            "--identity" => args.Length == 2 && id != "equipment",
            "--check-client" => id != "equipment" && args.Length == (id == "fiend-hunter" ? 2 : 3),
            "--connection-smoke" => args.Length == 2 && id is "fishing" or "territory" or "apostle-defense",
            "--runtime-check" => args.Length == 2 && id == "equipment",
            "--connection" => args.Length >= 2 && id == "equipment",
            _ => false
        };
        return helper;
    }
}
