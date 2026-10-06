namespace Dustweave;

public sealed record DailyStageDefinition(string Id, string Name, Func<DailyPreferences, bool> Enabled);
public static class DailyStageCatalog
{
    public static IReadOnlyList<DailyStageDefinition> All
    {
        get;
    } = [
        new("guild","公会签到",p=>p.Stages.Guild),new("room","小屋奖励",p=>p.Stages.Room),
        new("management","经营与餐厅",p=>p.Stages.CafeteriaIncome||p.Stages.CafeteriaGuests),
        new("free_draws","每日免费抽取",p=>p.Stages.FreeDraws),
        new("equipment","装备分解与精炼",p=>p.Equipment.Enabled||p.Equipment.RefineEnabled),
        new("weekly_equipment","周常制作装备",p=>p.Weekly.EquipmentCraft),
        new("weekly_mainline","周收集",p=>p.Weekly.Mainline),new("weekly_npc","周NPC任务",p=>p.Weekly.Npc),new("weekly_steal","每周偷窃",p=>p.Weekly.Steal),
        new("hunting","普通与圣石狩猎",p=>p.Hunt.Enabled||p.Hunt.StonesEnabled),
        new("mirror","镜中免费战斗",p=>p.Mirror.Enabled),new("daily_dispatch","每日派遣",p=>p.Tasks.Dispatch),
        new("square","广场奖励",p=>p.Tasks.Goddess||p.Tasks.SquareRanking),
        new("weekly_book","周常末日之书",p=>p.Weekly.Book),new("friendship","亲密度咨询",p=>p.Friendship.Enabled),
        new("tactics","战术教材",p=>DailyPlugin.Current.Supports("tactics")&&p.Tactics.Enabled),new("event_battle",DailyPlugin.Current.Supports("event_battle")?"活动战斗":"活动每日挑战15",p=>p.EventBattle.Enabled),
        new("monster_hunt","魔兽最高档快速战斗",p=>p.MonsterHunt.Enabled),
        new("weekly_room_likes","周常小屋点赞",p=>p.Weekly.RoomLikes),new("weekly_fishing","周常钓鱼",p=>p.Weekly.Fishing),new("weekly_sichuan","周常连连看",p=>p.Weekly.Sichuan),
        new("trade","跑商／料理／高价售卖",p=>p.Trade.Enabled),new("event_rewards","活动任务／转盘／代币兑换",p=>p.Events.Enabled),
        new("rewards","日常／周常／通行证奖励",p=>p.Stages.DailyRewards||p.Stages.WeeklyRewards||p.Tasks.Pass),
        new("mail","邮箱收尾",p=>p.Stages.Mail)
    ];
    // Preserve aliases used by existing schema-1 queues and explicit diagnostic commands.
    public static readonly string[] CompatibilityAdapters = ["cafeteria_income", "cafeteria_guests", "equipment_recycle", "equipment_refine", "daily_hunt", "stones", "goddess", "square_ranking", "daily_rewards", "weekly_rewards", "pass_rewards", "mission_rewards", "daily_hunt_minimal", "life_helpers", "collection_sync"];
    public static IEnumerable<string> Adapters => All.Select(s => s.Id).Concat(CompatibilityAdapters).Distinct(StringComparer.Ordinal);
    public static IEnumerable<DailyStageDefinition> Selectable => All.Where(s => s.Id != "tactics" || DailyPlugin.Current.Supports(s.Id));
    public static bool IsWeeklyRoute(string id) => id is "weekly_mainline" or "weekly_npc" or "weekly_steal";
    public static string Name(string id) => Selectable.FirstOrDefault(s => s.Id == id)?.Name ?? id switch
    {
        "collection_sync" => "手动检查收集进度", "life_helpers" => "经营收益领取",
        "cafeteria_income" => "餐厅收益", "cafeteria_guests" => "餐厅顾客奖励",
        "equipment_recycle" => "装备分解", "equipment_refine" => "装备精炼",
        "daily_hunt" => "章节狩猎", "daily_hunt_minimal" => "每日章节狩猎",
        "stones" => "圣石狩猎", "goddess" => "女神像奖励", "square_ranking" => "广场排行榜奖励",
        "daily_rewards" => "日常任务奖励", "weekly_rewards" => "周常任务奖励",
        "pass_rewards" => "通行证奖励", "mission_rewards" => "日常与周常任务奖励",
        _ => "未识别环节（" + id + "）"
    };
    public static bool Enabled(string id, DailyPreferences p) => All.FirstOrDefault(s => s.Id == id)?.Enabled(p) ?? (id switch
    {
        "collection_sync" or "life_helpers" or "daily_hunt_minimal" => true,
        "cafeteria_income" => p.Stages.CafeteriaIncome,
        "cafeteria_guests" => p.Stages.CafeteriaGuests,
        "equipment_recycle" => p.Equipment.Enabled,
        "equipment_refine" => p.Equipment.RefineEnabled,
        "daily_hunt" => p.Hunt.Enabled,
        "stones" => p.Hunt.StonesEnabled,
        "goddess" => p.Tasks.Goddess,
        "square_ranking" => p.Tasks.SquareRanking,
        "mission_rewards" => p.Stages.DailyRewards || p.Stages.WeeklyRewards,
        "daily_rewards" => p.Stages.DailyRewards,
        "weekly_rewards" => p.Stages.WeeklyRewards,
        "pass_rewards" => p.Tasks.Pass,
        _ => throw new InvalidDataException("未知日常环节：" + id)
    });
}
