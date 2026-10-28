using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

internal static class DailyHazardTravelPolicy
{
    internal static bool Hazardous(JsonObject row, long pack = 0) => B(row["EnvironmentalHazard"])
        // Nightmare Winter also combines patrol cameras and moving damage traps.
        // Keep this observed policy when an older component lacks trap metadata.
        || pack == 2002 && row["MapType"] != null && N(row["MapType"]) == 1;

    internal static bool SafeHub(JsonObject row) => row["MapType"] != null && N(row["MapType"]) == 0
        && !Hazardous(row) && !B(row["Hunting"]) && B(row["HasWaypoint"]);
}

public sealed partial class DailyFieldRoute
{
    private static JsonObject TravelMap(JsonObject e, long id)
    {
        // Talent callers may carry a narrow observation, or an older component
        // without map metadata. That must not disable unrelated ordinary skills.
        JsonObject? travel = null;
        try { travel = FieldRows(e, "travel").FirstOrDefault()?["$self"] as JsonObject; }
        catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException) { }
        if (travel != null && (N(travel["Pack"]) != N(Map(e)["packId"]) || N(travel["Map"]) != N(Map(e)["id"]))) travel = null;
        var row = Rows(travel?["Maps"] ?? new JsonArray()).FirstOrDefault(r => N(r["Id"]) == id)?.DeepClone().AsObject() ?? O(("Id", id));
        if (N(Map(e)["id"]) == id && Map(e)["mapType"] != null) row["MapType"] = Copy(Map(e)["mapType"]);
        row["EnvironmentalHazard"] = DailyHazardTravelPolicy.Hazardous(row, N(Map(e)["packId"]));
        return row;
    }
    internal static bool Hazardous(JsonObject e, long? id = null) => DailyHazardTravelPolicy.Hazardous(TravelMap(e, id ?? N(Map(e)["id"])));
    private static bool SafeHub(JsonObject e) => DailyHazardTravelPolicy.SafeHub(TravelMap(e, N(Map(e)["id"])));
    private static bool SafeStealthPreparation(JsonObject e, long destination) => SafeHub(e) && Hazardous(e, destination)
        && B(TravelMap(e, destination)["HasWaypoint"]) && AtWaypoint(e);

    private async Task ReachSafeHub(JsonObject e)
    {
        if (SafeHub(e)) return;
        var quick = await W.Evidence("route.quick");
        Identity(e, quick);
        var state = DailyQuickTravel.State(quick);
        if (N(state["Pack"]) != N(Map(e)["packId"]) || N(state["Map"]) != N(Map(e)["id"]))
            throw new DailyTravelBlocked("危险地图的快捷移动资料尚未就绪；未步行或原地重复隐身。");
        var targets = DailyQuickTravel.Targets(quick).Where(t => !B(t["AtTarget"]) && DailyHazardTravelPolicy.SafeHub(TravelMap(e, N(t["Map"]))))
            .OrderBy(t => N(t["Map"])).ThenBy(t => N(t["Npc"])).ToArray();
        foreach (var target in targets)
        {
            bool arrived = await DailyQuickTravel.TryMove(W, N(target["Npc"]), N(target["Map"]));
            await FieldReady(settle: 0);
            var after = await Evidence();
            Identity(e, after);
            Require(N(Map(after)["packId"]) == N(Map(e)["packId"]), "安全区中转期间卡带改变，已停止原路线。");
            if (SafeHub(after))
            {
                W.Driver.Diagnostics.Event("hazard_safe_hub", O(("from", Map(e)["id"]), ("hub", Map(after)["id"]), ("npc", target["Npc"])));
                return;
            }
            if (arrived || N(Map(after)["id"]) != N(Map(e)["id"])) break;
        }
        throw new DailyTravelBlocked("本图存在会解除隐身的伤害机关，但没有可用的安全区快捷入口；已保留进度，不改走危险路线。");
    }

    private sealed class HazardStealthExpired() : Exception;
    private async Task<string?> TeleportViaSafeHub(long destination, long? waypoint, bool recovery)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await FieldReady(settle: 0);
            var before = await Evidence();
            await ReachSafeHub(before);
            var safe = await Evidence();
            Require(SafeHub(safe), "未到达安全区，未使用隐身或步行。");
            if (N(Map(safe)["id"]) == destination) return null;
            // Use the safe area's physical portal. No summon or exploration fallback
            // is allowed to send us back into the dangerous map on foot.
            var port = FieldRows(safe, "waypoint").FirstOrDefault(p => B(p[WaypointNear]))
                ?? Reachable(Travel(safe), FieldRows(safe, "waypoint")).FirstOrDefault();
            if (port == null) throw new DailyTravelBlocked("安全区传送阵暂不可达；未进入危险地图。");
            if (!B(port[WaypointNear])) await Walk(I(port["instance"]));
            var ready = await Evidence();
            Require(SafeHub(ready) && AtWaypoint(ready), "安全区传送阵位置已变化，重新规划路线。");
            bool hazard = Hazardous(ready, destination);
            if (hazard)
            {
                await Skill(17, 12, hazardDestination: destination);
                ready = await Evidence();
                if (!SafeStealthPreparation(ready, destination) || Remaining(ready, 17) <= 12)
                    throw new DailyTravelBlocked("安全区内未能准备好隐身；已停留在安全区，未进入危险地图。");
            }
            try
            {
                var result = await TeleportDirect(destination, waypoint, force: true, recovery: recovery, nativeOnly: true);
                if (!hazard || Remaining(await Evidence(), 17) > 5) return result;
            }
            catch (HazardStealthExpired)
            {
                await W.Step("WayPointUI", back: true, absent: "WayPointUI", reason: "隐身不足，留在安全区重新准备");
            }
        }
        await ReachSafeHub(await Evidence());
        throw new DailyTravelBlocked("隐身在传送过程中失效，已返回安全区；未在伤害机关旁反复施放。");
    }

    private async Task<bool> PrepareHazardCollection(JsonObject e)
    {
        if (!Hazardous(e) || AtWaypoint(e) && Remaining(e, 17) > 5) return false;
        // A cartridge can reopen anywhere, including directly on a traffic lane.
        // Re-enter at its portal before research/absorption, even on the same map.
        await TeleportViaSafeHub(N(Map(e)["id"]), null, recovery: true);
        return true;
    }
}
