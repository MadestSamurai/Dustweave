using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
namespace Dustweave;

public sealed record DailyUpdateNetworkState(string? Source = null, string? HighestVersion = null);
public sealed record DailyUpdateAttempt(string Source, string Operation, string? Error);
public sealed class DailyUpdateTransport(DailyUpdateTrust trust)
{
    public static DailyUpdateTransport Production() => new(DailyUpdateTrust.Production());
    public static string FileName(DailyUpdateRelease release, DailyUpdateAsset asset) => $"Dustweave-{release.Version}-{asset.Flavor}-win-x64.zip";
    public static Uri PackageUri(DailyUpdateSource source, DailyUpdateRelease release, DailyUpdateAsset asset) =>
        Https(source.PackageBaseUrl.TrimEnd('/') + "/v" + release.Version + "/" + FileName(release, asset));
    private static Uri Https(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.UserInfo == "" && uri.Fragment == "" ? uri : throw new InvalidDataException("updates.invalid_feed");

    public async Task<DailyVerifiedUpdate> FetchAsync(HttpClient client, string root, CancellationToken token)
    {
        var state = DailyJson.TryRead<DailyUpdateNetworkState>(Path.Combine(root, "updates", "network.json")) ?? new();
        var attempts = new List<DailyUpdateAttempt>();
        // Only small manifests are probed. Compare versions so a stale mirror cannot win a race.
        var results = await Task.WhenAll(trust.Sources.Select(async source =>
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var response = await client.GetAsync(Https(source.FeedUrl), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var memory = new MemoryStream();
                await CopyBounded(input, memory, DailyUpdateSignatures.MaxEnvelopeBytes, 0, null, timeout.Token);
                var signed = JsonSerializer.Deserialize<DailySignedUpdate>(memory.ToArray(), DailyJson.Options) ?? throw new InvalidDataException("updates.invalid_feed");
                return (Update: new DailyVerifiedUpdate(DailyUpdateSignatures.Verify(signed, trust), signed, source.Id), Error: (string?)null, source.Id);
            }
            catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or OperationCanceledException or JsonException)
            { return (Update: (DailyVerifiedUpdate?)null, Error: e.Message, source.Id); }
        }));
        token.ThrowIfCancellationRequested();
        foreach (var r in results) attempts.Add(new(r.Id, "manifest", r.Error));
        DailyJson.Write(Path.Combine(root, "updates", "network-check.json"), new { AtUtc = DateTimeOffset.UtcNow, Attempts = attempts });
        var valid = results.Where(r => r.Update != null).Select(r => r.Update!).ToArray();
        if (valid.Length == 0) throw new InvalidDataException(results.Any(r => r.Error == "updates.signature_failed") ? "updates.signature_failed" : "updates.unavailable");
        // The same release/flavor must have the same bytes on every signed mirror.
        foreach (var group in valid.SelectMany(v => v.Feed.Releases).GroupBy(r => r.Version))
            if (group.Select(r => JsonSerializer.Serialize(r.Assets.OrderBy(a => a.Flavor), DailyJson.Options)).Distinct().Count() != 1)
                throw new InvalidDataException("updates.mirror_conflict");
        var best = valid.OrderByDescending(v => DailyUpdates.VersionOf(v.Feed.Releases[0].Version))
            .ThenBy(v => v.SourceId == state.Source ? 0 : 1).First();
        string latest = best.Feed.Releases[0].Version;
        if (state.HighestVersion != null && DailyUpdates.VersionOf(latest) < DailyUpdates.VersionOf(state.HighestVersion))
            throw new InvalidDataException("updates.stale_feed");
        DailyJson.Write(Path.Combine(root, "updates", "network.json"), state with { HighestVersion = latest });
        return best;
    }

    public async Task<string> DownloadAsync(HttpClient client, DailyVerifiedUpdate verified, string version, string flavor,
        string root, IProgress<int>? progress, CancellationToken token)
    {
        var feed = DailyUpdateSignatures.Verify(verified.Signed, trust);
        var release = feed.Releases.Single(r => r.Version == version);
        var asset = release.Assets.Single(a => a.Flavor == flavor);
        string directory = Path.Combine(root, "updates", version + "-" + flavor);
        DailyUpdates.EnsureNoLinks(directory); Directory.CreateDirectory(directory);
        string zip = Path.Combine(directory, "package.zip"), partial = zip + ".partial";
        // A per-package file lease also excludes another app instance while downloading.
        using var lease = new FileStream(Path.Combine(directory, "download.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        DailyJson.Write(Path.Combine(directory, "signed-feed.json"), verified.Signed);
        if (File.Exists(zip) && await Task.Run(() => DailyUpdates.Verify(zip, asset), token)) return directory;
        var state = DailyJson.TryRead<DailyUpdateNetworkState>(Path.Combine(root, "updates", "network.json")) ?? new();
        var attempts = new List<DailyUpdateAttempt>();
        foreach (var source in trust.Sources.OrderBy(s => s.Id == state.Source ? 0 : s.Id == verified.SourceId ? 1 : 2))
        {
            // One retry permits resuming transient same-source failures even with one configured source.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                    if (offset > asset.Bytes) { File.Delete(partial); offset = 0; }
                    if (offset == asset.Bytes && await Task.Run(() => DailyUpdates.Verify(partial, asset), token))
                    { File.Move(partial, zip, true); return directory; }
                    if (offset == asset.Bytes) { File.Delete(partial); offset = 0; }
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(20));
                    using var request = new HttpRequestMessage(HttpMethod.Get, PackageUri(source, release, asset));
                    if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
                    using var headers = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token); headers.CancelAfter(TimeSpan.FromSeconds(15));
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token);
                    headers.CancelAfter(Timeout.InfiniteTimeSpan);
                    response.EnsureSuccessStatusCode();
                    bool resume = response.StatusCode == HttpStatusCode.PartialContent;
                    if (resume && (response.Content.Headers.ContentRange is not { Unit: "bytes" } range ||
                        range.From != offset || range.To != asset.Bytes - 1 || range.Length != asset.Bytes))
                        throw new InvalidDataException("updates.hash_failed");
                    if (!resume) offset = 0; // Servers may ignore Range; restart rather than append.
                    if (response.Content.Headers.ContentLength is long length && length != asset.Bytes - offset) throw new InvalidDataException("updates.hash_failed");
                    await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
                    await using (var output = new FileStream(partial, offset == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None, 65536, true))
                    {
                        await CopyBounded(input, output, asset.Bytes, offset, progress, timeout.Token);
                        output.Flush(true);
                    }
                    if (!await Task.Run(() => DailyUpdates.Verify(partial, asset), token)) throw new InvalidDataException("updates.hash_failed");
                    File.Move(partial, zip, true);
                    DailyJson.Write(Path.Combine(root, "updates", "network.json"), state with { Source = source.Id });
                    attempts.Add(new(source.Id, "download", null));
                    DailyJson.Write(Path.Combine(directory, "download-result.json"), attempts);
                    return directory;
                }
                catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
                {
                    if (token.IsCancellationRequested) throw;
                    attempts.Add(new(source.Id, "download", e.Message));
                    if (e is InvalidDataException && File.Exists(partial)) File.Delete(partial);
                }
            }
        }
        DailyJson.Write(Path.Combine(directory, "download-result.json"), attempts);
        throw new InvalidDataException(attempts.Any(a => a.Error == "updates.hash_failed") ? "updates.hash_failed" : "updates.unavailable");
    }

    private static async Task CopyBounded(Stream input, Stream output, long limit, long total, IProgress<int>? progress, CancellationToken token)
    {
        byte[] buffer = new byte[65536]; int lastPercent = -1;
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token); idle.CancelAfter(TimeSpan.FromSeconds(20));
            int count = await input.ReadAsync(buffer, idle.Token);
            if (count == 0) break;
            total += count; if (total > limit) throw new InvalidDataException("updates.download_too_large");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            int percent = (int)(total * 100 / limit);
            if (percent != lastPercent) { lastPercent = percent; progress?.Report(percent); }
        }
    }
}
