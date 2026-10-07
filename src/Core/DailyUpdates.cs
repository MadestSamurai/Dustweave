using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dustweave;

public sealed record DailyReleaseNote(string Version, string Date, Dictionary<string, string[]> Notes);
public sealed record DailyUpdateAsset(string Flavor, string FileName, long Bytes, string Sha256);
public sealed record DailyUpdateDelta(string Flavor, string FromVersion, string FileName, long Bytes, string Sha256, string Algorithm);
public sealed record DailyUpdateRelease(string Version, string? PreviousVersion, int Layout, Dictionary<string, string[]> Notes, DailyUpdateAsset[] Assets, DailyUpdateDelta[]? Deltas = null);
public sealed record DailyUpdateFeed(int Schema, string Product, string Channel, DailyUpdateRelease[] Releases);
public sealed record DailyUpdatePackage(string Version, string Flavor, string[]? Files = null);
public sealed record DailyUpdateJob(string Directory, string Target, int ParentPid, long ParentStartTicks, DailyUpdateRelease Release, DailyUpdateAsset Asset, string Nonce = "", string OriginalSha256 = "", string? DeltaFileName = null);

public static class DailyUpdates
{
    public const string FeedUrl = "https://bd2.madsam.work/updates/dustweave/updates.json";
    public const long MaxArchive = 600L * 1024 * 1024;
    public static Version VersionOf(string value) => Version.TryParse(value, out var version) && version.Build >= 0 && version.Revision < 0
        ? version : throw new InvalidDataException("updates.invalid_feed");
    public static void Validate(DailyUpdateFeed feed)
    {
        if (feed.Schema != 2 || feed.Product != "Dustweave" || feed.Channel != "stable" || feed.Releases.Length is < 1 or > 300)
            throw new InvalidDataException("updates.invalid_feed");
        var versions = new HashSet<Version>();
        foreach (var release in feed.Releases)
        {
            var version = VersionOf(release.Version);
            if (!versions.Add(version) || release.Layout != 1 || release.Assets.Length is < 1 or > 2 || release.Assets.Select(a => a.Flavor).Distinct().Count() != release.Assets.Length)
                throw new InvalidDataException("updates.invalid_feed");
            if (release.PreviousVersion != null && VersionOf(release.PreviousVersion) >= version) throw new InvalidDataException("updates.invalid_feed");
            if (new[] { "zh-CN", "zh-TW", "en-US" }.Any(c => !release.Notes.TryGetValue(c, out var notes) || notes.Length is < 1 or > 100 || notes.Any(n => string.IsNullOrWhiteSpace(n) || n.Length > 4000))) throw new InvalidDataException("updates.invalid_feed");
            if (release.Notes.Values.Select(n => n.Length).Distinct().Count() != 1) throw new InvalidDataException("updates.invalid_feed");
            if ((release.Deltas?.Length ?? 0) > 12 ||
                (release.Deltas ?? []).Select(d => (d.Flavor, d.FromVersion)).Distinct().Count() != (release.Deltas?.Length ?? 0))
                throw new InvalidDataException("updates.invalid_feed");
            foreach (var delta in release.Deltas ?? [])
                if (!release.Assets.Any(a => a.Flavor == delta.Flavor) || VersionOf(delta.FromVersion) >= version ||
                    delta.FileName != DailyUpdateDeltaPackage.Name(release.Version, delta.Flavor, delta.FromVersion) ||
                    string.IsNullOrWhiteSpace(delta.Algorithm) || delta.Algorithm.Length > 64 || delta.Bytes <= 0 || delta.Bytes > MaxArchive ||
                    delta.Sha256.Length != 64 || !delta.Sha256.All(Uri.IsHexDigit))
                    throw new InvalidDataException("updates.invalid_feed");
            foreach (var asset in release.Assets)
            {
                if (asset.Flavor is not ("Portable" or "Lite") || asset.Bytes <= 0 || asset.Bytes > MaxArchive || asset.Sha256.Length != 64 || !asset.Sha256.All(Uri.IsHexDigit))
                    throw new InvalidDataException("updates.invalid_feed");
                var expected = $"Dustweave-{release.Version}-{asset.Flavor}-win-x64.zip";
                if (asset.FileName != expected) throw new InvalidDataException("updates.invalid_feed");
            }
        }
        var sorted = feed.Releases.OrderByDescending(r => VersionOf(r.Version)).ToArray();
        if (!feed.Releases.Select(r => r.Version).SequenceEqual(sorted.Select(r => r.Version))) throw new InvalidDataException("updates.invalid_feed");
        for (int i = 0; i < sorted.Length - 1; i++)
            if (sorted[i].PreviousVersion != sorted[i + 1].Version) throw new InvalidDataException("updates.broken_chain");
    }
    public static DailyUpdateRelease? Latest(DailyUpdateFeed feed, string current)
    {
        Validate(feed);
        return feed.Releases.OrderByDescending(r => VersionOf(r.Version)).FirstOrDefault(r => VersionOf(r.Version) > VersionOf(current));
    }
    public static HttpClient Client()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Dustweave-Updater/1.0");
        return client;
    }
    public static bool Verify(string path, DailyUpdateAsset asset)
    {
        using var file = File.OpenRead(path);
        return file.Length == asset.Bytes && Convert.ToHexString(SHA256.HashData(file)).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    public static bool AllowedFile(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.Contains(':') || name.StartsWith('/') || name.Split('/').Any(s => s is "" or "." or ".." || s.EndsWith('.') || s.EndsWith(' '))) return false;
        return name is "Dustweave.exe" or "utility-host.json" or "tools.json" or "update-package.json" or "README.md" or "README.en.md" or "THIRD_PARTY_NOTICES.md"
            || name.StartsWith("data/", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)
            || name.StartsWith("flows/", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)
            || name.StartsWith("connection/specs/", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)
            || name.StartsWith("licenses/", StringComparison.Ordinal)
            || name.StartsWith("docs/", StringComparison.Ordinal) && name.EndsWith(".md", StringComparison.Ordinal);
    }
    public static string[] Extract(string zipPath, string staging, DailyUpdateRelease release, DailyUpdateAsset asset)
    {
        if (!Verify(zipPath, asset)) throw new InvalidDataException("updates.hash_failed");
        using var zip = ZipFile.OpenRead(zipPath);
        var files = zip.Entries.Where(e => !e.FullName.EndsWith('/')).ToArray();
        if (files.Length is < 2 or > 5000 || files.Sum(e => e.Length) > 2L * 1024 * 1024 * 1024 || files.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length || files.Any(e => !AllowedFile(e.FullName) || ((e.ExternalAttributes >> 16) & 0xF000) == 0xA000))
            throw new InvalidDataException("updates.invalid_package");
        var descriptor = zip.GetEntry("update-package.json") ?? throw new InvalidDataException("updates.invalid_package");
        using (var stream = descriptor.Open())
        {
            var metadata = JsonSerializer.Deserialize<DailyUpdatePackage>(stream, DailyJson.Options);
            if (metadata?.Version != release.Version || metadata.Flavor != asset.Flavor || !files.Any(e => e.FullName == "Dustweave.exe")) throw new InvalidDataException("updates.invalid_package");
        }
        EnsureNoLinks(staging);
        foreach (var entry in files)
        {
            string target = Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            EnsureNoLinks(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
        return files.Select(e => e.FullName).ToArray();
    }
    public static void EnsureNoLinks(string path)
    {
        for (string? p = Path.GetFullPath(path); p != null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("updates.linked_folder");
    }
}
