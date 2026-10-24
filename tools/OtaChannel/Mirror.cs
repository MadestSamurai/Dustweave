using System.Net;
using System.Security.Cryptography;

namespace Dustweave.Ota;

sealed record MirrorReceipt(string Version, string BaseUrl, string FeedSha256, Blob[] Files);
sealed record MirrorHealth(string State, DateTimeOffset CheckedUtc, string? Version, string? Error = null);

// The origin remains authoritative. Only files already bound to a trusted feed
// may redirect; a failed preparation or probe retains the local download path.
static class Mirror
{
    public static string Root(Config c) => Path.Combine(c.Public, ".mirror");
    public static string Enabled(Config c) => Path.Combine(Root(c), "enabled");
    public static HttpClient Client() => new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
    { Timeout = Timeout.InfiniteTimeSpan };

    public static string BaseUrl(Config c)
    {
        Rules.Need(c.Layout is 2 or 3 && Uri.TryCreate(c.MirrorBaseUrl, UriKind.Absolute, out _), "Mirror requires a supported layout and an HTTPS address");
        var uri = new Uri(c.MirrorBaseUrl!);
        Rules.Need(uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo == "" && uri.Query == "" && uri.Fragment == "" &&
            System.Text.RegularExpressions.Regex.IsMatch(uri.Host, @"^[a-z0-9-]+\.cdn\.7caiyun\.com$") &&
            uri.AbsolutePath.TrimEnd('/') == (c.Layout == 3 ? "/dustweave/ota/all-in-one" : "/dustweave/ota/v2"), "Unexpected mirror host or prefix");
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }
    public static Uri FileUri(Config c, string version, Blob blob)
    {
        _ = DailyVersion.Parse(version);
        Rules.Need(System.Text.RegularExpressions.Regex.IsMatch(blob.FileName,
            @"^Dustweave-[0-9A-Za-z.-]+-(?:Lite|Portable)-from-[0-9A-Za-z.-]+-win-x64\.delta\.zip$") &&
            blob.FileName.StartsWith("Dustweave-" + version + "-", StringComparison.Ordinal) &&
            blob.Bytes > 0 && blob.Bytes <= (c.Capable ? 629145600 : 10485760) && Rules.IsHash(blob.Sha256), "Invalid mirror payload");
        return new(BaseUrl(c) + "/v" + version + "/" + blob.FileName);
    }
    static Blob[] Inventory(Feed feed) => Rules.Patches(feed).Select(d => new Blob(d.FileName, d.Bytes, d.Sha256.ToLowerInvariant()))
        .OrderBy(b => b.FileName, StringComparer.Ordinal).ToArray();
    static string Routes(Config c, string version) => Path.Combine(Root(c), "routes", "v" + DailyVersion.Parse(version));
    static string Receipt(Config c, string version) => Path.Combine(Root(c), "verified", "v" + DailyVersion.Parse(version) + ".json");
    static void DirectoryOf(string path)
    {
        Rules.NoLinks(path);
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
    static void Record(Config c, MirrorHealth health)
    {
        DirectoryOf(Root(c));
        Rules.Atomic(Path.Combine(Root(c), "health.json"), Rules.Bytes(health));
    }
    public static void Disable(Config c, string? version, string error)
    {
        Rules.NoLinks(Enabled(c));
        if (File.Exists(Enabled(c))) File.Delete(Enabled(c));
        Record(c, new("origin", DateTimeOffset.UtcNow, version, error));
    }
    public static object Status(Config c)
    {
        if (c.MirrorBaseUrl == null) return new { configured = false };
        var path = Path.Combine(Root(c), "health.json");
        Rules.NoLinks(path); Rules.NoLinks(Enabled(c));
        return new { configured = true, active = File.Exists(Enabled(c)), baseUrl = BaseUrl(c),
            health = File.Exists(path) ? Rules.Read<MirrorHealth>(File.ReadAllBytes(path)) : null };
    }
    static async Task VerifyRemote(Config c, string version, Blob blob, HttpClient client, bool full, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(full ? TimeSpan.FromMinutes(3) : TimeSpan.FromSeconds(6));
        using var request = new HttpRequestMessage(full ? HttpMethod.Get : HttpMethod.Head, FileUri(c, version, blob));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        Rules.Need(response.StatusCode == HttpStatusCode.OK && response.Content.Headers.ContentLength == blob.Bytes,
            "Mirror response status or length differs: " + blob.FileName);
        if (!full) return;
        using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long length = 0; byte[] buffer = new byte[65536];
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            idle.CancelAfter(TimeSpan.FromSeconds(20));
            int read = await input.ReadAsync(buffer, idle.Token);
            if (read == 0) break;
            length += read;
            Rules.Need(length <= blob.Bytes, "Mirror response exceeds signed length");
            hash.AppendData(buffer, 0, read);
        }
        Rules.Need(length == blob.Bytes &&
            Convert.ToHexString(hash.GetHashAndReset()).Equals(blob.Sha256, StringComparison.OrdinalIgnoreCase),
            "Mirror SHA-256 differs: " + blob.FileName);
    }
    public static async Task Prepare(Config c, byte[] signedFeed, HttpClient client, CancellationToken token = default)
    {
        string baseUrl = BaseUrl(c);
        var feed = Rules.VerifyFeed(signedFeed, c);
        string version = feed.Releases[0].Version;
        Blob[] files = Inventory(feed);
        Rules.Need(files.Length > 0 || c.Layout == 3 || feed.Releases[0].InstallerProtocol == 2, "No signed patches to mirror");
        foreach (var file in files)
        {
            Rules.CheckArchive(Path.Combine(c.Public, "v" + version, file.FileName), file);
            await VerifyRemote(c, version, file, client, true, token);
        }
        // Neither routes nor the global switch exist until every remote body passes.
        DirectoryOf(Root(c)); DirectoryOf(Path.Combine(Root(c), "routes")); DirectoryOf(Path.Combine(Root(c), "verified"));
        string routes = Routes(c, version);
        var receipt = new MirrorReceipt(version, baseUrl, Rules.Hash(signedFeed), files);
        if (Directory.Exists(routes))
        {
            Rules.NoLinks(routes);
            Rules.Need(Directory.GetFiles(routes).Select(Path.GetFileName).Order(StringComparer.Ordinal)
                .SequenceEqual(files.Select(f => f.FileName)), "Existing mirror route inventory differs");
            foreach (var file in files)
                Rules.Need(File.ReadAllText(Path.Combine(routes, file.FileName)) == file.Sha256, "Existing mirror route changed");
        }
        else
        {
            string stage = routes + ".pending-" + Guid.NewGuid().ToString("N");
            DirectoryOf(stage);
            foreach (var file in files) Rules.Atomic(Path.Combine(stage, file.FileName), System.Text.Encoding.ASCII.GetBytes(file.Sha256));
            Directory.Move(stage, routes);
        }
        Rules.Atomic(Receipt(c, version), Rules.Bytes(receipt));
        // Health publication checks the live feed separately. Preparing a future
        // release never overwrites the currently active version's health status.
    }
    public static async Task<bool> Probe(Config c, HttpClient client, CancellationToken token = default)
    {
        string? version = null;
        try
        {
            string baseUrl = BaseUrl(c);
            var feedBytes = File.ReadAllBytes(Rules.FeedPath(c));
            var feed = Rules.VerifyFeed(feedBytes, c);
            version = feed.Releases[0].Version;
            string receiptPath = Receipt(c, version); Rules.NoLinks(receiptPath);
            var receipt = Rules.Read<MirrorReceipt>(File.ReadAllBytes(receiptPath));
            var files = Inventory(feed);
            Rules.Need((files.Length > 0 || c.Layout == 3 || feed.Releases[0].InstallerProtocol == 2) && receipt.Version == version && receipt.BaseUrl == baseUrl &&
                receipt.FeedSha256 == Rules.Hash(feedBytes) && receipt.Files.SequenceEqual(files), "Mirror not prepared for the current signed feed");
            foreach (var file in files)
            {
                string route = Path.Combine(Routes(c, version), file.FileName); Rules.NoLinks(route);
                Rules.Need(File.ReadAllText(route) == file.Sha256, "Missing verified mirror route");
            }
            await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token },
                async (file, cancellation) => await VerifyRemote(c, version, file, client, false, cancellation));
            // A publication that changed while probing must not inherit old health.
            Rules.Need(Rules.HashFile(Rules.FeedPath(c)) == receipt.FeedSha256, "Feed changed during mirror health check");
            Rules.Atomic(Enabled(c), Rules.Bytes(new { version, checkedUtc = DateTimeOffset.UtcNow }));
            Record(c, new("cdn", DateTimeOffset.UtcNow, version));
            return true;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or HttpRequestException or OperationCanceledException or System.Text.Json.JsonException
            or CryptographicException or FormatException or ArgumentException or KeyNotFoundException)
        { Disable(c, version, e.Message); return false; }
    }
    public static void TryPrepareAndProbe(Config c, byte[] feed)
    {
        if (c.MirrorBaseUrl == null) return;
        using var client = Client();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        try { Prepare(c, feed, client, timeout.Token).GetAwaiter().GetResult(); Probe(c, client, timeout.Token).GetAwaiter().GetResult(); }
        catch (Exception e) when (e is IOException or InvalidDataException or HttpRequestException or OperationCanceledException or System.Text.Json.JsonException
            or CryptographicException or FormatException or ArgumentException or KeyNotFoundException)
        { Disable(c, null, e.Message); }
    }
}
