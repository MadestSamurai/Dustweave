using System.Text.RegularExpressions;
namespace Dustweave;

/// <summary>Only our WER dumps. Neither system databases nor another application's dumps are disposable.</summary>
public static class DailyCrashStorage
{
    static readonly Regex Name = new(@"^(?:Dustweave|BD2DailyAssistant)(?:\.Updater)?\.exe\.[0-9]+\.dmp$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static bool Own(string path) => Name.IsMatch(Path.GetFileName(path));
    public static DailyCacheUnit[] Plan(string folder, IEnumerable<DailyStorageItem> files, DateTime now, int days = 7)
    {
        folder = Path.GetFullPath(folder);
        return files.Where(f => Path.GetDirectoryName(f.Path)!.Equals(folder, StringComparison.OrdinalIgnoreCase) && Own(f.Path))
            .OrderByDescending(f => f.Written).Select((f, i) => (File: f, Index: i))
            .Where(v => v.File.Written < now.AddDays(-Math.Clamp(days, 2, 7)) || v.Index >= 2 && v.File.Written < now.AddDays(-1))
            .Select(v => new DailyCacheUnit(v.File.Path, [v.File])).ToArray();
    }
}
