using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
namespace Dustweave;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class DailyPreferences
{
    [JsonRequired]
    public int Schema { get; set; } = 16;
    [JsonRequired] public FriendshipPreferences Friendship { get; set; } = new();
    [JsonRequired]
    public HuntPreferences Hunt { get; set; } = new();
    [JsonRequired]
    public EquipmentPreferences Equipment { get; set; } = new();
    [JsonRequired]
    public TaskPreferences Tasks { get; set; } = new();
    [JsonRequired]
    public StagePreferences Stages { get; set; } = new();
    [JsonRequired]
    public MirrorPreferences Mirror { get; set; } = new();
    [JsonRequired] public WeeklyPreferences Weekly { get; set; } = new();
    [JsonRequired] public MonsterHuntPreferences MonsterHunt { get; set; } = new();
    [JsonRequired] public EventBattlePreferences EventBattle { get; set; } = new();
    [JsonRequired] public TradePreferences Trade { get; set; } = new();
    [JsonRequired] public EventRewardPreferences Events { get; set; } = new();
    [JsonRequired] public TacticsPreferences Tactics { get; set; } = new();
    public static DailyPreferences Parse(string json)
    {
        var node = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException("日常设置不能为空。");
        if (node["Schema"]?.GetValue<int>() == 1)
        {
            if (!node.Select(p => p.Key).Order().SequenceEqual(new[] { "Equipment", "Hunt", "Schema", "Tasks" }))
                throw new InvalidDataException("旧版日常设置字段不完整。");
            node["Schema"] = 3;
            node["Stages"] = JsonSerializer.SerializeToNode(new StagePreferences());
            node["Mirror"] = JsonSerializer.SerializeToNode(new MirrorPreferences());
        }
        if (node["Schema"]?.GetValue<int>() == 2)
        {
            var stages = node["Stages"]?.AsObject() ?? throw new InvalidDataException("旧版环节字段不完整。");
            if (!stages.Select(p => p.Key).Order().SequenceEqual(new[] { "CafeteriaGuests", "CafeteriaIncome", "DailyRewards", "FreeDraws", "Guild", "Room", "WeeklyRewards" }))
                throw new InvalidDataException("旧版环节字段不完整。");
            stages["Mail"] = true;
            node["Schema"] = 3;
        }
        if (node["Schema"]?.GetValue<int>() == 3)
        {
            if (!node.Select(p => p.Key).Order().SequenceEqual(new[] { "Equipment", "Hunt", "Mirror", "Schema", "Stages", "Tasks" }))
                throw new InvalidDataException("旧版日常设置字段不完整。");
            node["Schema"] = 4;
            node["Weekly"] = JsonSerializer.SerializeToNode(new WeeklyPreferences());
            node["MonsterHunt"] = JsonSerializer.SerializeToNode(new MonsterHuntPreferences());
        }
        if (node["Schema"]?.GetValue<int>() == 4)
        {
            if (!node.Select(p => p.Key).Order().SequenceEqual(new[] { "Equipment", "Hunt", "Mirror", "MonsterHunt", "Schema", "Stages", "Tasks", "Weekly" }))
                throw new InvalidDataException("旧版日常设置字段不完整。");
            node["Schema"] = 5;
            node["Trade"] = JsonSerializer.SerializeToNode(new TradePreferences());
        }
        if (node["Schema"]?.GetValue<int>() == 5)
        {
            if (!node.Select(p => p.Key).Order().SequenceEqual(new[] { "Equipment", "Hunt", "Mirror", "MonsterHunt", "Schema", "Stages", "Tasks", "Trade", "Weekly" }))
                throw new InvalidDataException("旧版日常设置字段不完整。");
            node["Schema"] = 6;
            node["EventBattle"] = JsonSerializer.SerializeToNode(new EventBattlePreferences());
        }
        if (node["Schema"]?.GetValue<int>() == 6)
        {
            if (node.ContainsKey("Events"))
                throw new InvalidDataException("旧版活动偏好字段不匹配。");
            node["Schema"] = 7;
            node["Events"] = JsonSerializer.SerializeToNode(new EventRewardPreferences());
        }
        if (node["Schema"]?.GetValue<int>() == 7)
        {
            var events = node["Events"] as JsonObject ?? throw new InvalidDataException("活动设置缺失。");
            foreach (var key in new[] { "Enabled", "Missions", "Roulette", "Exchange" })
                if (!events.ContainsKey(key))
                    throw new InvalidDataException("旧版活动设置不完整。");
            if (!events.ContainsKey("Quiz"))
                events["Quiz"] = true;
            if (!events.ContainsKey("Dice"))
                events["Dice"] = true;
            node["Schema"] = 8;
        }
        if (node["Schema"]?.GetValue<int>() == 8)
        {
            if (node.ContainsKey("Tactics"))
                throw new InvalidDataException("旧版战术教材字段不匹配。");
            node["Schema"] = 9;
            node["Tactics"] = JsonSerializer.SerializeToNode(new TacticsPreferences());
        }
        if (node["Schema"]?.GetValue<int>() == 9)
        {
            if (node.ContainsKey("Friendship"))
                throw new InvalidDataException("旧版咨询设置字段不匹配。");
            node["Schema"] = 10;
            node["Friendship"] = JsonSerializer.SerializeToNode(new FriendshipPreferences());
        }
        if (node["Schema"]?.GetValue<int>() == 10)
        {
            var weekly = node["Weekly"] as JsonObject ?? throw new InvalidDataException("周常设置缺失。");
            if (!weekly.ContainsKey("Book"))
                weekly["Book"] = true;
            if (!weekly.ContainsKey("EquipmentCraft"))
                weekly["EquipmentCraft"] = true;
            node["Schema"] = 11;
        }
        if (node["Schema"]?.GetValue<int>() == 11)
        {
            var weekly = node["Weekly"] as JsonObject ?? throw new InvalidDataException("周常设置缺失。");
            if (!weekly.ContainsKey("Sichuan"))
                weekly["Sichuan"] = true;
            node["Schema"] = 12;
        }
        if (node["Schema"]?.GetValue<int>() == 12)
        {
            var weekly = node["Weekly"] as JsonObject ?? throw new InvalidDataException("周常设置缺失。");
            if (!weekly.ContainsKey("Steal"))
                weekly["Steal"] = true;
            node["Schema"] = 13;
        }
        if (node["Schema"]?.GetValue<int>() == 13)
        {
            var weekly = node["Weekly"] as JsonObject ?? throw new InvalidDataException("周常设置缺失。");
            if (!weekly.ContainsKey("Mainline") || !weekly.ContainsKey("Steal"))
                throw new InvalidDataException("旧版周常设置不完整。");
            weekly["Steal"] = weekly["Mainline"]!.GetValue<bool>() && weekly["Steal"]!.GetValue<bool>();
            weekly["Npc"] = false;
            weekly["NpcHunting"] = false;
            node["Schema"] = 14;
        }
        if (node["Schema"]?.GetValue<int>() == 14)
        {
            var events = node["Events"] as JsonObject ?? throw new InvalidDataException("活动设置缺失。");
            if (!events.ContainsKey("Puzzle")) events["Puzzle"] = true;
            node["Schema"] = 15;
        }
        if (node["Schema"]?.GetValue<int>() == 15)
        {
            var weekly = node["Weekly"] as JsonObject ?? throw new InvalidDataException("周常设置缺失。");
            // The maintained route catalog now defines coverage for all three weekly activities.
            // Preserve task switches and an explicitly selected walking preference.
            foreach (string field in new[] { "FirstChapter", "LastChapter", "CharacterCartridges", "EventCartridges" })
                weekly.Remove(field);
            node["Schema"] = 16;
        }
        var value = node.Deserialize<DailyPreferences>(DailyJson.Options) ?? throw new InvalidDataException("日常设置不能为空。");
        value.Validate();
        return value;
    }
    public void Validate()
    {
        if (Schema != 16 || Friendship == null || Tactics == null || Events == null || EventBattle == null || Trade == null || Weekly == null || MonsterHunt == null || Hunt == null || Equipment == null || Tasks == null || Stages == null || Mirror == null)
            throw new InvalidDataException("日常偏好版本或字段不完整。");
        if (Tactics.SearchSeconds is < 1 or > 120)
            throw new InvalidDataException("战术教材搜索时间必须为 1 至 120 秒。");
        if (EventBattle.SearchSeconds is < 1 or > 120)
            throw new InvalidDataException("活动搜索时间必须为 1 至 120 秒。");
        if (Mirror.Multiplier is < 1 or > 40)
            throw new InvalidDataException("镜中倍率必须为 1 至 40。");
        if (Hunt.OrdinaryChapter is < 1 or > 10)
            throw new InvalidDataException("普通狩猎关卡必须为 1 至 10。");
        if (Hunt.TorchLimit is < 0 or > 60)
            throw new InvalidDataException("免费火炬上限必须为 0 至 60。");
        if (Equipment.EnhanceLevel is < 0 or > 9)
            throw new InvalidDataException("分解前强化等级必须为 0 至 9。");
        if (Hunt.Priority == null || !Hunt.Priority.Order().SequenceEqual(new[] { "gold", "ordinary", "slime" }))
            throw new InvalidDataException("优先级必须包含金币、史莱姆和普通狩猎各一次。");
        if (!new[] { "least", "fire", "water", "wind", "light", "dark" }.Contains(Hunt.StoneElement))
            throw new InvalidDataException("圣石属性必须为库存最少或指定属性。");
        if (Equipment.RefineInstance == null || (Equipment.RefineInstance.Length > 0 &&
            (!long.TryParse(Equipment.RefineInstance, out long id) || id <= 0 || !Equipment.RefineInstance.All(char.IsAsciiDigit))))
            throw new InvalidDataException("指定精炼装备必须为有效的库存编号。");
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class HuntPreferences
{
    [JsonRequired]
    public bool Enabled { get; set; } = true;
    [JsonRequired]
    public int OrdinaryChapter { get; set; } = 9;
    [JsonRequired]
    public bool FarmGold { get; set; } = true;
    [JsonRequired]
    public bool FarmSlime
    {
        get; set;
    }
    [JsonRequired]
    public string[] Priority { get; set; } = ["gold", "slime", "ordinary"];
    [JsonRequired]
    public bool StonesEnabled { get; set; } = true;
    [JsonRequired]
    public string StoneElement { get; set; } = "least";
    [JsonRequired]
    public int TorchLimit { get; set; } = 60;
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class EquipmentPreferences
{
    [JsonRequired]
    public bool Enabled { get; set; } = true;
    [JsonRequired]
    public bool KeepFiveStarSr { get; set; } = true;
    [JsonRequired]
    public int EnhanceLevel { get; set; } = 7;
    [JsonRequired]
    public bool CraftFallback { get; set; } = true;
    [JsonRequired]
    public bool RefineEnabled { get; set; } = true;
    [JsonRequired]
    public string RefineInstance { get; set; } = "";
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TaskPreferences
{
    [JsonRequired]
    public bool Pass { get; set; } = true;
    [JsonRequired]
    public bool Dispatch { get; set; } = true;
    [JsonRequired]
    public bool Goddess { get; set; } = true;
    [JsonRequired]
    public bool SquareRanking { get; set; } = true;
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StagePreferences
{
    [JsonRequired] public bool Guild { get; set; } = true;
    [JsonRequired] public bool Room { get; set; } = true;
    [JsonRequired] public bool CafeteriaIncome { get; set; } = true;
    [JsonRequired] public bool CafeteriaGuests { get; set; } = true;
    [JsonRequired] public bool FreeDraws { get; set; } = true;
    [JsonRequired] public bool DailyRewards { get; set; } = true;
    [JsonRequired] public bool WeeklyRewards { get; set; } = true;
    [JsonRequired] public bool Mail { get; set; } = true;
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MirrorPreferences
{
    [JsonRequired] public bool Enabled { get; set; } = true;
    [JsonRequired] public int Multiplier { get; set; } = 40;
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class WeeklyPreferences
{
    [JsonRequired] public bool Sichuan { get; set; } = true;
    [JsonRequired] public bool Book { get; set; } = true;
    [JsonRequired] public bool EquipmentCraft { get; set; } = true;
    [JsonRequired]
    public bool Mainline
    {
        get; set;
    }
    [JsonRequired]
    public bool Steal
    {
        get; set;
    }
    [JsonRequired]
    public bool Npc
    {
        get; set;
    }
    [JsonRequired]
    public bool NpcHunting
    {
        get; set;
    }
    [JsonRequired]
    public bool WalkCollect
    {
        get; set;
    }
    [JsonRequired]
    public bool Fishing
    {
        get; set;
    }
    [JsonRequired]
    public bool RoomLikes
    {
        get; set;
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class FriendshipPreferences
{
    [JsonRequired] public bool Enabled { get; set; } = true; [JsonRequired]
    public bool Quick
    {
        get; set;
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MonsterHuntPreferences
{
    [JsonRequired]
    public bool Enabled
    {
        get; set;
    }
}
public sealed class DailyPreferenceStore(string root)
{
    public string PathFor(string account)
    {
        if (!DailyProfiles.ValidKey(account))
            throw new InvalidDataException("账号标识无效。");
        return Path.Combine(root, "preferences", account, "daily.json");
    }
    public DailyPreferences Read(string account)
    {
        string path = PathFor(account);
        if (!File.Exists(path))
            return new();
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using var reader = new StreamReader(file);
        return DailyPreferences.Parse(reader.ReadToEnd());
    }
    public void Save(string account, DailyPreferences value)
    {
        value.Validate();
        // A corrupt existing file must be repaired deliberately, never reset implicitly.
        _ = Read(account);
        DailyJson.Write(PathFor(account), value);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TradePreferences
{
    [JsonRequired]
    public bool Enabled
    {
        get; set;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class EventBattlePreferences
{
    [JsonRequired]
    public bool Enabled
    {
        get; set;
    }
    [JsonRequired] public bool Challenge { get; set; } = true;
    [JsonRequired] public int SearchSeconds { get; set; } = 15;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class EventRewardPreferences
{
    [JsonRequired] public bool Enabled { get; set; } = true;
    [JsonRequired] public bool Missions { get; set; } = true;
    [JsonRequired] public bool Quiz { get; set; } = true;
    [JsonRequired] public bool Dice { get; set; } = true;
    [JsonRequired] public bool Puzzle { get; set; } = true;
    [JsonRequired] public bool Roulette { get; set; } = true;
    [JsonRequired] public bool Exchange { get; set; } = true;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TacticsPreferences
{
    [JsonRequired]
    public bool Enabled
    {
        get; set;
    }
    [JsonRequired] public int SearchSeconds { get; set; } = 45;
}
