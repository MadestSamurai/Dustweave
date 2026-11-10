using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
namespace Dustweave;

/// <summary>Portable policy, never a captured inventory or an executable order list.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DailyTradePreset(
    [property: JsonRequired] string Format,
    [property: JsonRequired] int Schema,
    [property: JsonRequired] TradePreferences Trade,
    [property: JsonRequired] JsonObject Budget)
{
    public const int MaxBytes = 1024 * 1024;
    private const string FormatName = "Dustweave.TradePreset";

    public static DailyTradePreset Capture(string root, string account) => Parse(JsonSerializer.Serialize(
        new DailyTradePreset(FormatName, 1, new DailyPreferenceStore(root).Read(account).Trade,
            DailyTradePlan.ReadSettings(root, account)), DailyJson.Options));

    public static DailyTradePreset Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaxBytes) throw new InvalidDataException("tradepreset.invalid");
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return Parse(reader.ReadToEnd());
    }

    public static DailyTradePreset Parse(string text)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new InvalidDataException();
            var result = JsonSerializer.Deserialize<DailyTradePreset>(text, DailyJson.Options);
            if (result == null || result.Format != FormatName || result.Schema != 1 || result.Trade == null || result.Budget == null
                || result.Trade.StrategyRevision != 1) throw new InvalidDataException();
            // No missing switches may silently acquire a consuming default during import.
            var node = JsonNode.Parse(text)!.AsObject();
            var fields = JsonSerializer.SerializeToNode(new TradePreferences())!.AsObject().Select(p => p.Key).Order();
            if (!node["Trade"]!.AsObject().Select(p => p.Key).Order().SequenceEqual(fields)) throw new InvalidDataException();
            var budgetFields = DailyTradeOptimizer.Preferences().Select(p => p.Key).Order();
            if (!result.Budget.Select(p => p.Key).Order().SequenceEqual(budgetFields)) throw new InvalidDataException();
            foreach (var value in new[] { result.Budget["reserve_gold"], result.Budget["max_spend"] }
                .Concat((result.Budget["reserve_items"] as JsonObject ?? throw new InvalidDataException()).Select(p => p.Value)).Where(v => v != null))
                if (value is not JsonValue scalar || !scalar.TryGetValue<long>(out _)) throw new InvalidDataException();
            new DailyPreferences { Trade = result.Trade }.Validate();
            return result with { Budget = DailyTradeOptimizer.Preferences(result.Budget) };
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
        { throw new InvalidDataException("tradepreset.invalid", e); }
    }

    public void Export(string path) => DailyJson.Write(path, Parse(JsonSerializer.Serialize(this, DailyJson.Options)));

    public void Apply(string root, string account)
    {
        var validated = Parse(JsonSerializer.Serialize(this, DailyJson.Options));
        var store = new DailyPreferenceStore(root);
        var preferences = store.Read(account);
        preferences.Trade = validated.Trade;
        preferences.Validate();
        string path = DailyTradePlan.SettingsPath(root, account);
        bool existed = File.Exists(path);
        var previous = DailyTradePlan.ReadSettings(root, account);
        // Write the independent budget first; if the account save fails, restore it.
        // Existing execution journals and inventory are never copied or reset.
        DailyJson.Write(path, validated.Budget);
        try { store.Save(account, preferences); }
        catch
        {
            if (existed) DailyJson.Write(path, previous);
            else File.Delete(path);
            throw;
        }
    }
}
