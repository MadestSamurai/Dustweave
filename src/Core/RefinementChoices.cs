using System.Text.Json;
namespace Dustweave;

public sealed record RefinementChoice(string Instance, string Label);
public sealed class RefinementChoices
{
    public int Schema
    {
        get; set;
    }
    public string AccountKey { get; set; } = "";
    public string CapturedUtc { get; set; } = "";
    public bool Complete
    {
        get; set;
    }
    public List<RefinementChoice> Items { get; set; } = [];
    public static RefinementChoices? Read(string root, string account)
    {
        string path = Path.Combine(Path.GetDirectoryName(new DailyPreferenceStore(root).PathFor(account))!, "equipment-choices.json");
        if (!File.Exists(path))
            return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var value = JsonSerializer.Deserialize<RefinementChoices>(stream, DailyJson.Options);
        if (value == null || value.Schema != 1 || value.AccountKey != account || !value.Complete || !DateTimeOffset.TryParse(value.CapturedUtc, out _) || value.Items == null || value.Items.Any(r => r == null || string.IsNullOrEmpty(r.Instance) || !long.TryParse(r.Instance, out long id) || id <= 0 || !r.Instance.All(char.IsAsciiDigit) || string.IsNullOrWhiteSpace(r.Label)) || value.Items.Select(r => r.Instance).Distinct().Count() != value.Items.Count)
            throw new InvalidDataException("精炼库存不完整或不属于当前账号，请重新读取。");
        return value;
    }
}
