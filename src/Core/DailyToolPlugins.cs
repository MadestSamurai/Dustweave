namespace Dustweave;

/// <summary>Tool selections are independent of the existing daily extension.
/// Each tool ID reuses the immutable package store and verified activation.</summary>
public sealed class DailyToolPlugins(string dataRoot)
{
    public string Root => Path.Combine(Path.GetFullPath(dataRoot), "tool-plugins");
    public DailyPluginStore Store(string id)
    {
        if (!DailyPlugin.ValidId(id)) throw new InvalidDataException("plugins.invalid_manifest");
        return new(Path.Combine(Root, id));
    }
    public IReadOnlyList<(DailyPluginSelection Selection, DailyInstalledPlugin Record, DailyPluginInfo Info)> Installed()
    {
        var result = new List<(DailyPluginSelection, DailyInstalledPlugin, DailyPluginInfo)>();
        if (!Directory.Exists(Root)) return result;
        DailyPluginStore.NoLinks(Root);
        foreach (string directory in Directory.EnumerateDirectories(Root))
        {
            string id = Path.GetFileName(directory);
            if (!DailyPlugin.ValidId(id)) continue;
            try
            {
                var store = Store(id); var state = store.Read();
                foreach (var record in state.Installed)
                {
                    var info = store.Inspect(record);
                    if (!info.Available || info.IsTool && info.Id == id) result.Add((state, record, info));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        }
        return result;
    }
    public IReadOnlyList<DailyPluginInfo> Enabled() => Installed().Where(row => row.Selection.Active == row.Record.Fingerprint && row.Info.Available).Select(row => row.Info).ToArray();
}
