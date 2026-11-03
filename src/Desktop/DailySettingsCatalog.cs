namespace Dustweave.Desktop;

// Resource keys identify settings sections; display copy and language never take
// part in categorization. A settings section may be a sub-operation of a task.
internal static class DailySettingsCatalog
{
    internal static string Category(string titleKey) => titleKey switch
    {
        "prefs.general" or "prefs.trade_purchase" or "stage.territory_saffron" => "resources",
        "prefs.hunt" or "stage.stones" or "prefs.mirror" or "stage.tactics" or "prefs.event_battle" or "stage.salvation" => "battle",
        "stage.weekly_mainline" or "stage.weekly_npc" or "stage.weekly_steal" or "stage.weekly_book"
            or "stage.weekly_equipment" or "stage.weekly_room_likes" or "stage.weekly_fishing"
            or "stage.weekly_sichuan" or "stage.weekly_rewards" => "weekly",
        _ => "daily"
    };
}
