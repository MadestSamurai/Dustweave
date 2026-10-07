using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
namespace Dustweave;

public sealed record DailyUpdateDownload(string? DeltaFileName);
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
            if (group.Select(r => JsonSerializer.Serialize(new { Assets = r.Assets.OrderBy(a => a.Flavor), Deltas = (r.Deltas ?? []).OrderBy(d => d.Flavor).ThenBy(d => d.FromVersion) }, DailyJson.Options)).Distinct().Count() != 1)
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
        string root, IProgress<int>? progress, CancellationToken token, string? installedDirectory = null, bool allowDelta = true)
    {
        var feed = DailyUpdateSignatures.Verify(verified.Signed, trust);
        var release = feed.Releases.Single(r => r.Version == version);
        var asset = release.Assets.Single(a => a.Flavor == flavor);
        string directory = Path.Combine(root, "updates", version + "-" + flavor);
        DailyUpdates.EnsureNoLinks(directory); Directory.CreateDirectory(directory);
        using var lease = new FileStream(Path.Combine(directory, "download.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        DailyJson.Write(Path.Combine(directory, "signed-feed.json"), verified.Signed);
        string choice = Path.Combine(directory, "download-choice.json");
        string full = Path.Combine(directory, "package.zip");
        if (File.Exists(full) && await Task.Run(() => DailyUpdates.Verify(full, asset), token))
        { DailyJson.Write(choice, new DailyUpdateDownload(null)); return directory; }
        var installed = installedDirectory == null ? null : DailyJson.TryRead<DailyUpdatePackage>(Path.Combine(installedDirectory, "update-package.json"));
        // A direct patch from the exact installed version avoids downloading intermediate releases.
        // Only use deltas with a meaningful size advantage; never substitute Portable for Lite.
        var delta = allowDelta ? release.Deltas?.Where(d => d.Algorithm == DailyUpdateDeltaPackage.Algorithm && d.Flavor == flavor && d.FromVersion == installed?.Version &&
            installed.Flavor == flavor && d.Bytes < asset.Bytes * .8).OrderBy(d => d.Bytes).FirstOrDefault() : null;
        if (delta != null && installedDirectory != null)
        {
            try
            {
                string deltaPath = Path.Combine(directory, delta.FileName);
                await DownloadPayload(client, verified, version, root, directory, delta.FileName, deltaPath,
                    delta.Bytes, delta.Sha256, progress, token);
                // Dry reconstruction checks all output files before offering a restart. It never
                // writes into the installation; the independent installer repeats verification.
                await Task.Run(() =>
                {
                    string staging = Path.Combine(directory, "delta-check-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(staging);
                    try { DailyUpdateDeltaPackage.Extract(deltaPath, installedDirectory, staging, release, asset, delta, token); }
                    finally { Directory.Delete(staging, true); }
                }, token);
                DailyJson.Write(choice, new DailyUpdateDownload(delta.FileName));
                return directory;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or OperationCanceledException)
            {
                token.ThrowIfCancellationRequested();
                DailyJson.Write(Path.Combine(directory, "delta-fallback.json"), new { atUtc = DateTimeOffset.UtcNow, error = error.ToString() });
                // A missing/mismatched local base, unavailable patch or failed reconstruction falls
                // back within this check, instead of breaking automation or requiring another click.
            }
        }
        await DownloadPayload(client, verified, version, root, directory, asset.FileName, full,
            asset.Bytes, asset.Sha256, progress, token);
        DailyJson.Write(choice, new DailyUpdateDownload(null));
        return directory;
    }

    private async Task DownloadPayload(HttpClient client, DailyVerifiedUpdate verified, string version, string root,
        string directory, string fileName, string destination, long bytes, string hash, IProgress<int>? progress, CancellationToken token)
    {
        DailyUpdates.EnsureNoLinks(destination);
        if (File.Exists(destination) && await Task.Run(() => DailyUpdateDeltaPackage.VerifyFile(destination, bytes, hash), token)) return;
        string partial = destination + ".partial";
        DailyUpdates.EnsureNoLinks(partial);
        var state = DailyJson.TryRead<DailyUpdateNetworkState>(Path.Combine(root, "updates", "network.json")) ?? new();
        var attempts = new List<DailyUpdateAttempt>();
        foreach (var source in trust.Sources.OrderBy(s => s.Id == state.Source ? 0 : s.Id == verified.SourceId ? 1 : 2))
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                    if (offset > bytes) { File.Delete(partial); offset = 0; }
                    if (offset == bytes && await Task.Run(() => DailyUpdateDeltaPackage.VerifyFile(partial, bytes, hash), token))
                    { File.Move(partial, destination, true); return; }
                    if (offset == bytes) { File.Delete(partial); offset = 0; }
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(20));
                    using var request = new HttpRequestMessage(HttpMethod.Get, Https(source.PackageBaseUrl.TrimEnd('/') + "/v" + version + "/" + fileName));
                    if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
                    using var headers = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token); headers.CancelAfter(TimeSpan.FromSeconds(15));
                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token);
                    headers.CancelAfter(Timeout.InfiniteTimeSpan);
                    response.EnsureSuccessStatusCode();
                    bool resume = response.StatusCode == HttpStatusCode.PartialContent;
                    if (resume && (response.Content.Headers.ContentRange is not { Unit: "bytes" } range ||
                        range.From != offset || range.To != bytes - 1 || range.Length != bytes))
                        throw new InvalidDataException("updates.hash_failed");
                    if (!resume) offset = 0;
                    if (response.Content.Headers.ContentLength is long length && length != bytes - offset) throw new InvalidDataException("updates.hash_failed");
                    await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
                    await using (var output = new FileStream(partial, offset == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None, 65536, true))
                    {
                        await CopyBounded(input, output, bytes, offset, progress, timeout.Token);
                        output.Flush(true);
                    }
                    if (!await Task.Run(() => DailyUpdateDeltaPackage.VerifyFile(partial, bytes, hash), token)) throw new InvalidDataException("updates.hash_failed");
                    File.Move(partial, destination, true);
                    DailyJson.Write(Path.Combine(root, "updates", "network.json"), state with { Source = source.Id });
                    attempts.Add(new(source.Id, fileName.EndsWith(".delta.zip") ? "delta" : "download", null));
                    DailyJson.Write(Path.Combine(directory, "download-result.json"), attempts);
                    return;
                }
                catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
                {
                    if (token.IsCancellationRequested) throw;
                    attempts.Add(new(source.Id, fileName.EndsWith(".delta.zip") ? "delta" : "download", e.Message));
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
