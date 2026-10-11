namespace Dustweave;

public sealed record DailyTaskPreset(string Id, string Name, string[] Tasks);
public sealed record DailyTaskPresetData(int Schema, DailyTaskPreset[] Presets, Dictionary<string, string> Accounts);

// Presets contain selection only. Settings and server completion remain per account.
public sealed class DailyTaskPresetStore(string root)
{
    private string PathName => Path.Combine(root, "task-presets.json");
    public DailyTaskPresetData Read()
    {
        var value = File.Exists(PathName) ? DailyJson.TryRead<DailyTaskPresetData>(PathName)
            ?? throw new InvalidDataException("presets.invalid") : new(1, [], new());
        if (value.Schema != 1 || value.Presets == null || value.Accounts == null || value.Presets.Length > 100 ||
            value.Presets.Any(p => p == null || !Guid.TryParseExact(p.Id, "N", out _) || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 60 ||
                p.Tasks == null || p.Tasks.Length == 0 || p.Tasks.Distinct().Count() != p.Tasks.Length || p.Tasks.Any(t => !DailyStageCatalog.All.Any(s => s.Id == t))) ||
            value.Presets.Select(p => p.Id).Distinct().Count() != value.Presets.Length ||
            value.Accounts.Any(p => !DailyProfiles.ValidKey(p.Key) || !value.Presets.Any(v => v.Id == p.Value)))
            throw new InvalidDataException("presets.invalid");
        return value;
    }
    public DailyTaskPreset Save(string name, IEnumerable<string> tasks, string? id = null)
    {
        var data = Read(); name = name.Trim();
        if (name.Length is < 1 or > 60 || data.Presets.Any(p => p.Id != id && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("presets.name_invalid");
        var wanted = tasks.ToHashSet(StringComparer.Ordinal);
        if (wanted.Count == 0 || wanted.Any(t => !DailyStageCatalog.Selectable.Any(s => s.Id == t))) throw new InvalidDataException("presets.empty");
        if (id != null && !data.Presets.Any(p => p.Id == id)) throw new InvalidDataException("presets.missing");
        if (id == null && data.Presets.Length >= 100) throw new InvalidDataException("presets.limit");
        var preset = new DailyTaskPreset(id ?? Guid.NewGuid().ToString("N"), name, DailyStageCatalog.Selectable.Where(s => wanted.Contains(s.Id)).Select(s => s.Id).ToArray());
        DailyJson.Write(PathName, data with { Presets = data.Presets.Where(p => p.Id != preset.Id).Append(preset).ToArray() });
        return preset;
    }
    public void Assign(string account, string? id)
    {
        var data = Read();
        if (!DailyProfiles.ValidKey(account) || !string.IsNullOrEmpty(id) && !data.Presets.Any(p => p.Id == id)) throw new InvalidDataException("presets.missing");
        data.Accounts.Remove(account);
        if (!string.IsNullOrEmpty(id)) data.Accounts.Add(account, id);
        DailyJson.Write(PathName, data);
    }
    public void Delete(string id)
    {
        var data = Read();
        // Never silently broaden an unattended account's task scope on deletion.
        if (data.Accounts.ContainsValue(id)) throw new InvalidDataException("presets.in_use");
        DailyJson.Write(PathName, data with { Presets = data.Presets.Where(p => p.Id != id).ToArray() });
    }
    public QueuePlanRequest? Selection(string account, DailyPreferences preferences)
    {
        var data = Read();
        if (!data.Accounts.TryGetValue(account, out var id)) return null;
        var wanted = data.Presets.Single(p => p.Id == id).Tasks.ToHashSet();
        var tasks = DailyStageCatalog.Selectable.Where(s => wanted.Contains(s.Id) && s.Enabled(preferences)).Select(s => s.Id).ToArray();
        if (tasks.Length == 0) throw new InvalidDataException("presets.none_enabled");
        return new(account, tasks);
    }
}

public sealed record DailyDesktopBehavior(string CloseAction = "ask", bool RunOnOpen = false)
{
    public static DailyDesktopBehavior Read(string root)
    {
        string path = Path.Combine(root, "desktop-behavior.json");
        var value = File.Exists(path) ? DailyJson.TryRead<DailyDesktopBehavior>(path) ?? throw new InvalidDataException("desktop.invalid") : new();
        value.Validate(); return value;
    }
    private void Validate() { if (CloseAction is not ("ask" or "tray" or "exit")) throw new InvalidDataException("desktop.invalid"); }
    public void Save(string root) { Validate(); DailyJson.Write(Path.Combine(root, "desktop-behavior.json"), this); }
    public bool ShouldRun(bool scheduled, bool updated, bool sandbox, bool guided, bool busy, bool directed = false) => RunOnOpen && !scheduled && !updated && !sandbox && !guided && !busy && !directed;
}
