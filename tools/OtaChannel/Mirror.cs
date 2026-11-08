using System.Net;
using System.Security.Cryptography;

namespace Dustweave.Ota;

sealed record MirrorReceipt(string Version, string BaseUrl, string FeedSha256, Blob[] Files);
sealed record MirrorHealth(string State, DateTimeOffset CheckedUtc, string? Version, string? Error = null);
sealed record MirrorProgress(string Version, string BaseUrl, string FeedSha256, Blob[] Files, DateTimeOffset UpdatedUtc);
sealed record MirrorFailure(string Version, string FirstStage, string FirstError, DateTimeOffset FirstUtc,
    string LastStage, string LastError, DateTimeOffset LastUtc, int Count, DateTimeOffset? RecoveredUtc = null);

// The origin owns the signed index. Layout 3 serves payloads only through verified
// object-storage routes; reachability observations never turn it into a file server.
static class Mirror
{
    public static string Root(Config c) => Path.Combine(c.Public, ".mirror");
    public static string Enabled(Config c) => Path.Combine(Root(c), "enabled");
    static string HealthPath(Config c) => Path.Combine(Root(c), c.Layout == 3 && c.Capable ? "health-capable.json" : "health.json");
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
        return new(BaseUrl(c) + "/" + ObjectVersion(c, version) + "/" + blob.FileName);
    }
    // The unreleased full 1.5.17 candidate already occupied its staging prefix.
    // Keep those immutable objects separate from the final transition release.
    internal static string ObjectVersion(Config c, string version) => c.Layout == 3 && version == "1.5.17" ? "v1.5.17-transition" : "v" + version;
    static Blob[] Inventory(Feed feed) => Rules.Patches(feed).Select(d => new Blob(d.FileName, d.Bytes, d.Sha256.ToLowerInvariant()))
        .OrderBy(b => b.FileName, StringComparer.Ordinal).ToArray();
    static string Routes(Config c, string version) => Path.Combine(Root(c), "routes", "v" + DailyVersion.Parse(version));
    static string Receipt(Config c, string version) => Path.Combine(Root(c), "verified", "v" + DailyVersion.Parse(version) + ".json");
    static string Progress(Config c, string version) => Path.Combine(Root(c), "progress", "v" + DailyVersion.Parse(version) + ".json");
    static string Failure(Config c, string version) => Path.Combine(Root(c), "failures", "v" + DailyVersion.Parse(version) + ".json");
    static bool Expected(Exception e) => e is IOException or InvalidDataException or HttpRequestException or OperationCanceledException
        or System.Text.Json.JsonException or CryptographicException or FormatException or ArgumentException or KeyNotFoundException;
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
        Rules.Atomic(HealthPath(c), Rules.Bytes(health));
    }
    public static void Disable(Config c, string? version, string error)
    {
        Rules.NoLinks(Enabled(c));
        if (File.Exists(Enabled(c))) File.Delete(Enabled(c));
        Record(c, new(c.Layout == 3 ? "unprepared" : "origin", DateTimeOffset.UtcNow, version, error));
    }
    static void RecordFailure(Config c, string version, string stage, Exception error)
    {
        string path = Failure(c, version); Rules.NoLinks(path);
        DirectoryOf(Path.GetDirectoryName(path)!);
        var now = DateTimeOffset.UtcNow;
        var previous = File.Exists(path) ? Rules.Read<MirrorFailure>(File.ReadAllBytes(path)) : null;
        var failure = previous == null ? new MirrorFailure(version, stage, error.Message, now, stage, error.Message, now, 1)
            : previous with { LastStage = stage, LastError = error.Message, LastUtc = now, Count = previous.Count + 1, RecoveredUtc = null };
        Rules.Atomic(path, Rules.Bytes(failure));
    }
    static void Recovered(Config c, string version)
    {
        string path = Failure(c, version); Rules.NoLinks(path);
        if (File.Exists(path))
        {
            var previous = Rules.Read<MirrorFailure>(File.ReadAllBytes(path));
            if (previous.RecoveredUtc == null) Rules.Atomic(path, Rules.Bytes(previous with { RecoveredUtc = DateTimeOffset.UtcNow }));
        }
    }
    public static object Status(Config c)
    {
        if (c.MirrorBaseUrl == null) return new { configured = false };
        var path = HealthPath(c);
        Rules.NoLinks(path); Rules.NoLinks(Enabled(c));
        var health = File.Exists(path) ? Rules.Read<MirrorHealth>(File.ReadAllBytes(path)) : null;
        string version = Rules.VerifyFeed(File.ReadAllBytes(Rules.FeedPath(c)), c).Releases[0].Version;
        // Old publishers shared this record across two different signed indexes.
        // Never attach a transition warning to the current application's status.
        if (health?.Version != version) health = null;
        string? failurePath = Failure(c, version);
        string? progressPath = Progress(c, version);
        if (failurePath != null) Rules.NoLinks(failurePath);
        if (progressPath != null) Rules.NoLinks(progressPath);
        var progress = progressPath != null && File.Exists(progressPath) ? Rules.Read<MirrorProgress>(File.ReadAllBytes(progressPath)) : null;
        return new { configured = true, active = c.Layout == 3 ? CurrentPrepared(c) : File.Exists(Enabled(c)),
            originFallback = c.Layout != 3, baseUrl = BaseUrl(c), health,
            failure = failurePath != null && File.Exists(failurePath) ? Rules.Read<MirrorFailure>(File.ReadAllBytes(failurePath)) : null,
            preparation = progress == null ? null : new { progress.Version, progress.FeedSha256, verifiedFiles = progress.Files?.Length ?? 0, progress.UpdatedUtc } };
    }
    static bool Transient(Exception e, CancellationToken token) => !token.IsCancellationRequested &&
        (e is OperationCanceledException or IOException || e is HttpRequestException request &&
        (request.StatusCode == null || request.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout));
    static async Task VerifyWithRetry(Config c, string version, Blob blob, HttpClient client, bool full, CancellationToken token)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { await VerifyRemote(c, version, blob, client, full, token); return; }
            catch (Exception e) when (attempt < 3 && Transient(e, token))
            { await Task.Delay(TimeSpan.FromSeconds(attempt), token); }
            catch (OperationCanceledException e)
            { throw new OperationCanceledException($"Mirror {(full ? "GET" : c.Layout == 3 ? "range GET" : "HEAD")} timed out or canceled: {blob.FileName} (attempt {attempt})", e, token); }
        }
    }
    static async Task VerifyRemote(Config c, string version, Blob blob, HttpClient client, bool full, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(full ? TimeSpan.FromMinutes(3) : TimeSpan.FromSeconds(20));
        bool range = !full && c.Layout == 3;
        int sampleBytes = (int)Math.Min(blob.Bytes, 65536);
        using var request = new HttpRequestMessage(full || range ? HttpMethod.Get : HttpMethod.Head, FileUri(c, version, blob));
        if (range) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, sampleBytes - 1);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if ((int)response.StatusCode is 408 or 429 or 500 or 502 or 503 or 504)
            throw new HttpRequestException($"Mirror HTTP {(int)response.StatusCode}: {blob.FileName}", null, response.StatusCode);
        if (range)
        {
            var received = response.Content.Headers.ContentRange;
            Rules.Need(response.StatusCode == HttpStatusCode.PartialContent
                ? received?.Unit == "bytes" && received.From == 0 && received.To == sampleBytes - 1 &&
                    received.Length == blob.Bytes && response.Content.Headers.ContentLength == sampleBytes
                : response.StatusCode == HttpStatusCode.OK && response.Content.Headers.ContentLength == blob.Bytes,
                "Mirror range response differs: " + blob.FileName);
            // Exercise the client download path, not the CDN's separate HEAD path.
            // Full SHA-256 verification remains mandatory before publication. A
            // health check transfers at most 64 KiB, even when Range is ignored.
            using var remote = await response.Content.ReadAsStreamAsync(timeout.Token);
            string localPath = Path.Combine(c.Public, "v" + version, blob.FileName);
            Rules.NoLinks(localPath);
            using var local = File.OpenRead(localPath);
            byte[] actual = new byte[sampleBytes], expected = new byte[sampleBytes];
            await remote.ReadExactlyAsync(actual, timeout.Token);
            await local.ReadExactlyAsync(expected, timeout.Token);
            Rules.Need(actual.AsSpan().SequenceEqual(expected), "Mirror range bytes differ: " + blob.FileName);
            return;
        }
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
    public static async Task Prepare(Config c, byte[] signedFeed, HttpClient client, CancellationToken token = default, bool resume = false)
    {
        string baseUrl = BaseUrl(c);
        var feed = Rules.VerifyFeed(signedFeed, c);
        string version = feed.Releases[0].Version;
        Blob[] files = Inventory(feed);
        Rules.Need(files.Length > 0 || c.Layout == 3 || feed.Releases[0].InstallerProtocol == 2, "No signed patches to mirror");
        string digest = Rules.Hash(signedFeed), progressPath = Progress(c, version);
        Rules.NoLinks(progressPath);
        var completed = new List<Blob>();
        if (resume && File.Exists(progressPath))
        {
            // Trusted local receipts only: exact feed, endpoint, version and signed blob identity.
            // A different publication or malformed checkpoint never supplies cached validation.
            MirrorProgress? saved = null;
            try { saved = Rules.Read<MirrorProgress>(File.ReadAllBytes(progressPath)); }
            catch (System.Text.Json.JsonException) { }
            if (saved?.Version == version && saved.BaseUrl == baseUrl && saved.FeedSha256 == digest && saved.Files != null &&
                saved.Files.Distinct().Count() == saved.Files.Length && saved.Files.All(files.Contains)) completed.AddRange(saved.Files);
        }
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            Rules.CheckArchive(Path.Combine(c.Public, "v" + version, file.FileName), file);
            if (completed.Contains(file)) continue;
            try { await VerifyWithRetry(c, version, file, client, true, token); }
            catch (OperationCanceledException e)
            { throw new OperationCanceledException($"Mirror body verification timed out or canceled for {file.FileName}: {e.Message}", e, token); }
            catch (Exception e) when (Expected(e))
            { throw new InvalidDataException($"Mirror body verification failed for {file.FileName}: {e.Message}", e); }
            completed.Add(file);
            DirectoryOf(Path.GetDirectoryName(progressPath)!);
            Rules.Atomic(progressPath, Rules.Bytes(new MirrorProgress(version, baseUrl, digest, completed.ToArray(), DateTimeOffset.UtcNow)));
        }
        // Neither routes nor the global switch exist until every remote body passes.
        DirectoryOf(Root(c)); DirectoryOf(Path.Combine(Root(c), "routes")); DirectoryOf(Path.Combine(Root(c), "verified"));
        string routes = Routes(c, version);
        var receipt = new MirrorReceipt(version, baseUrl, digest, files);
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
    static void CheckPrepared(Config c, byte[] feedBytes, Feed feed)
    {
        string version = feed.Releases[0].Version, receiptPath = Receipt(c, version); Rules.NoLinks(receiptPath);
        var receipt = Rules.Read<MirrorReceipt>(File.ReadAllBytes(receiptPath));
        var files = Inventory(feed);
        Rules.Need((files.Length > 0 || c.Layout == 3 || feed.Releases[0].InstallerProtocol == 2) && receipt.Version == version &&
            receipt.BaseUrl == BaseUrl(c) && receipt.FeedSha256 == Rules.Hash(feedBytes) && receipt.Files != null && receipt.Files.SequenceEqual(files),
            "Mirror not prepared for the current signed feed");
        foreach (var file in files)
        {
            string route = Path.Combine(Routes(c, version), file.FileName); Rules.NoLinks(route);
            Rules.Need(File.ReadAllText(route) == file.Sha256, "Missing verified mirror route");
        }
    }
    static bool CurrentPrepared(Config c)
    {
        try
        {
            var bytes = File.ReadAllBytes(Rules.FeedPath(c));
            CheckPrepared(c, bytes, Rules.VerifyFeed(bytes, c));
            return true;
        }
        catch (Exception e) when (Expected(e)) { return false; }
    }
    public static void PrepareBeforePublication(Config c, byte[] signedFeed, HttpClient client)
    {
        if (c.Layout != 3 || c.MirrorBaseUrl == null) return;
        var feed = Rules.VerifyFeed(signedFeed, c);
        try { CheckPrepared(c, signedFeed, feed); return; }
        catch (Exception e) when (Expected(e)) { }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        // Keep the previous live feed if verification fails. Completed immutable
        // bodies remain resumable; no user is advertised an unprepared new patch.
        Prepare(c, signedFeed, client, timeout.Token, resume: true).GetAwaiter().GetResult();
    }
    public static async Task<bool> Probe(Config c, HttpClient client, CancellationToken token = default)
    {
        string? version = null;
        try
        {
            var feedBytes = File.ReadAllBytes(Rules.FeedPath(c));
            var feed = Rules.VerifyFeed(feedBytes, c);
            version = feed.Releases[0].Version;
            var files = Inventory(feed);
            CheckPrepared(c, feedBytes, feed);
            await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token },
                async (file, cancellation) => await VerifyWithRetry(c, version, file, client, false, cancellation));
            // A publication that changed while probing must not inherit old health.
            Rules.Need(Rules.HashFile(Rules.FeedPath(c)) == Rules.Hash(feedBytes), "Feed changed during mirror health check");
            Rules.Atomic(Enabled(c), Rules.Bytes(new { version, checkedUtc = DateTimeOffset.UtcNow }));
            Record(c, new("cdn", DateTimeOffset.UtcNow, version));
            Recovered(c, version);
            return true;
        }
        catch (Exception e) when (Expected(e))
        {
            if (version != null) RecordFailure(c, version, "probe", e);
            if (c.Layout == 3 && CurrentPrepared(c))
                Record(c, new("cdn-degraded", DateTimeOffset.UtcNow, version, e.Message));
            else Disable(c, version, e.Message);
            return false;
        }
    }
    // Used by the timer as well as explicit maintenance. A bounded attempt saves
    // completed bodies and the next timer invocation resumes the same signed set.
    public static async Task<bool> Maintain(Config c, HttpClient client, CancellationToken token = default)
    {
        if (c.MirrorBaseUrl == null) return false;
        string lockPath = Path.Combine(c.State, "publish.lock"); Rules.NoLinks(lockPath);
        FileStream gate;
        try { gate = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return false; } // A publisher/maintenance run already owns this lane.
        using (gate) return await Recover(c, File.ReadAllBytes(Rules.FeedPath(c)), client, token);
    }
    public static async Task<bool> MaintainConfiguredFeeds(Config c, HttpClient client, CancellationToken token = default)
    {
        if (c.Layout != 3 || c.Capable) return await Maintain(c, client, token);
        var capable = Rules.Capability(c);
        string path = Rules.FeedPath(capable, writing: true); Rules.NoLinks(path);
        if (!File.Exists(path)) return await Maintain(c, client, token);
        // Separate locks and health records: a busy/failing transition lane must
        // not prevent the current lane from being observed or recovered.
        Rules.NoLinks(capable.State);
        Directory.CreateDirectory(capable.State);
        var results = await Task.WhenAll(Maintain(c, client, token), Maintain(capable, client, token));
        return results.All(ok => ok);
    }
    static async Task<bool> Recover(Config c, byte[] signedFeed, HttpClient client, CancellationToken token)
    {
        string? version = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(4));
        try
        {
            var feed = Rules.VerifyFeed(signedFeed, c); version = feed.Releases[0].Version;
            bool ready;
            try { CheckPrepared(c, signedFeed, feed); ready = true; }
            catch (Exception e) when (Expected(e)) { ready = false; }
            if (!ready)
            {
                Disable(c, version, c.Layout == 3 ? "Preparing verified mirror delivery; unverified patches are unavailable" :
                    "Preparing verified mirror delivery; origin remains available");
                await Prepare(c, signedFeed, client, timeout.Token, resume: true);
            }
            Rules.Need(Rules.HashFile(Rules.FeedPath(c)) == Rules.Hash(signedFeed), "Feed changed during mirror preparation");
            return await Probe(c, client, timeout.Token);
        }
        catch (Exception e) when (Expected(e))
        { if (version != null) RecordFailure(c, version, "prepare", e); Disable(c, version, e.Message); return false; }
    }
    public static void TryPrepareAndProbe(Config c, byte[] feed, HttpClient? suppliedClient = null)
    {
        if (c.MirrorBaseUrl == null) return;
        using var ownedClient = suppliedClient == null ? Client() : null;
        // Channel.Worker already holds the same publication lock.
        Recover(c, feed, suppliedClient ?? ownedClient!, CancellationToken.None).GetAwaiter().GetResult();
    }
}
