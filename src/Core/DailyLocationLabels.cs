using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Dustweave.DailyData;
namespace Dustweave;

// Location IDs stay in diagnostics. The timeline receives a compact multilingual
// projection of names, independent of the language selected during execution.
public static class DailyLocationLabels
{
    public static Dictionary<string, DailyDisplayName> Build(JsonObject[] packs, JsonObject[] maps, JsonObject[] names)
    {
        var texts = names.ToDictionary(r => N(r["id"]), DailyDisplayName.From);
        var result = new Dictionary<string, DailyDisplayName>();
        foreach (var p in packs) if (texts.TryGetValue(N(p["packNameTextId"]), out var name)) result["p" + N(p["id"])] = name;
        foreach (var m in maps) if (texts.TryGetValue(N(m["mapNameTextId"]), out var name)) result["m" + N(m["id"])] = name;
        return result;
    }
    public static Dictionary<string, DailyDisplayName> Read(JsonElement item)
    {
        foreach (string field in new[] { "result", "progress" })
            if (item.TryGetProperty(field, out var r) && r.ValueKind == JsonValueKind.Object && r.TryGetProperty("location_labels", out var names))
                try { return names.Deserialize<Dictionary<string, DailyDisplayName>>() ?? []; } catch (JsonException) { }
        return [];
    }
    public static string Format(string text, IReadOnlyDictionary<string, DailyDisplayName> names, string language)
    {
        if (names.Count == 0) return text;
        // Both the original and the translated notice templates are supported.
        string Label(string key, string fallback) => names.TryGetValue(key, out var name) && name.In(language).Length > 0 ? name.In(language) : fallback;
        text = Regex.Replace(text, @"第\s*(\d+)\s*章|[Cc]hapter\s+(\d+)", m => "卡带 " + (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value));
        text = Regex.Replace(text, @"(?:卡带|卡帶|[Cc]artridges?)\s*(\d+)(?:\s*·?\s*(?:地图|地圖|[Mm]ap)\s*(\d+))?", m =>
            Label("p" + m.Groups[1].Value, (language == "en-US" ? "Cartridge " : language == "zh-TW" ? "卡帶 " : "卡带 ") + m.Groups[1].Value)
                + (m.Groups[2].Success ? " · " + Label("m" + m.Groups[2].Value, (language == "en-US" ? "Map " : language == "zh-TW" ? "地圖 " : "地图 ") + m.Groups[2].Value) : ""));
        text = Regex.Replace(text, @"(?:地图|地圖|[Mm]ap)\s*(\d+)", m => Label("m" + m.Groups[1].Value, m.Groups[0].Value));
        return text;
    }
}
