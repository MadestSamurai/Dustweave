using System.Buffers.Binary;
using System.Security.Cryptography;
using Dustweave;

namespace Dustweave.Ota;

sealed record RepairRequest(int Schema, string Product, string Version, string ExpectedFeedSha256, string FeedSha256,
    string Nonce, DateTimeOffset IssuedUtc, DateTimeOffset ExpiresUtc, string[] ManifestHashes, Blob[] Files);
sealed record RepairHeader(Envelope Request, string Feed, string[] Manifests);

static class RepairChannel
{
    public const string Domain = "Dustweave-Repair-Publish-v1\n";
    public static object Index(Config config)
    {
        Rules.Need(config.Layout == 2, "Repair is limited to v2");
        string path = Path.Combine(config.Public, "repair", "objects"); Rules.NoLinks(path);
        var files = new List<object>();
        if (Directory.Exists(path))
            foreach (string file in Directory.EnumerateFiles(path, "*.bin").Take(100001))
            {
                Rules.Need(files.Count < 100000, "Repair index exceeds retention limit");
                Rules.NoLinks(file);
                if (Rules.IsHash(Path.GetFileNameWithoutExtension(file)))
                    files.Add(new { FileName = Path.GetFileName(file), Bytes = new FileInfo(file).Length });
            }
        return new { files };
    }
    public static (RepairRequest Request, DailyRepairManifest[] Manifests, Dictionary<string, Blob> Blocks) Validate(RepairHeader header, Config config, DateTimeOffset now)
    {
        Rules.Need(config.Layout == 2, "Repair publication is limited to v2");
        var request = Rules.Read<RepairRequest>(Rules.Verify(header.Request, config, Domain));
        Rules.Need(request.Schema == 1 && request.Product == "Dustweave" && Rules.IsNonce(request.Nonce) &&
            Rules.IsHash(request.ExpectedFeedSha256) && Rules.IsHash(request.FeedSha256), "Invalid repair publication identity");
        Rules.Need(request.IssuedUtc <= now.AddSeconds(60) && request.IssuedUtc >= now.AddMinutes(-15) &&
            request.ExpiresUtc > now && request.ExpiresUtc <= request.IssuedUtc.AddMinutes(15), "Expired repair publication");
        byte[] feedBytes = Convert.FromBase64String(header.Feed);
        Rules.Need(Rules.Hash(feedBytes) == request.FeedSha256, "Repair feed changed");
        var feed = Rules.VerifyFeed(feedBytes, config);
        var release = feed.Releases[0];
        Rules.Need(request.Version == release.Version && header.Manifests.Length == 2 && request.ManifestHashes.Length == 2, "Both repair editions required");
        var manifests = new List<DailyRepairManifest>();
        var blocks = new Dictionary<string, Blob>(StringComparer.Ordinal);
        for (int i = 0; i < header.Manifests.Length; i++)
        {
            byte[] bytes = Convert.FromBase64String(header.Manifests[i]);
            Rules.Need(Rules.Hash(bytes) == request.ManifestHashes[i], "Repair envelope changed");
            var manifest = DailyUpdateRepairManifest.Verify(bytes, config.Keys);
            var asset = release.Assets.Single(a => a.Flavor == manifest.Flavor);
            Rules.Need(manifest.Version == release.Version && manifest.Layout == release.Layout &&
                manifest.RuntimeVersion == release.RuntimeVersion && manifest.ArchiveSha256.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase),
                "Repair manifest is not bound to release archive");
            manifests.Add(manifest);
            foreach (var block in manifest.Files.SelectMany(f => f.Blocks))
            {
                string name = DailyUpdateRepairManifest.ObjectName(block);
                if (blocks.TryGetValue(name, out var existing)) Rules.Need(existing.Bytes == block.Bytes, "Conflicting repair block");
                blocks[name] = new(name, block.Bytes, block.Sha256.ToLowerInvariant());
            }
        }
        Rules.Need(manifests.Select(m => m.Flavor).Order().SequenceEqual(new[] { "Lite", "Portable" }), "Duplicate repair edition");
        Rules.Need(request.Files.Length <= blocks.Count && request.Files.Select(f => f.FileName).Distinct().Count() == request.Files.Length &&
            request.Files.All(f => blocks.TryGetValue(f.FileName, out var expected) && f == expected), "Unexpected repair payload");
        return (request, manifests.ToArray(), blocks);
    }
    public static async Task<object> Submit(Config config, Stream input, HttpClient? verificationClient = null, TimeSpan? mirrorBudget = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15)); var token = timeout.Token;
        byte[] prefix = new byte[4]; await input.ReadExactlyAsync(prefix, token);
        int length = BinaryPrimitives.ReadInt32BigEndian(prefix);
        Rules.Need(length is > 0 and <= 12 * 1024 * 1024, "Invalid repair header");
        byte[] bytes = new byte[length]; await input.ReadExactlyAsync(bytes, token);
        var header = Rules.Read<RepairHeader>(bytes);
        var (request, manifests, blocks) = Validate(header, config, DateTimeOffset.UtcNow);
        string liveFeed = Rules.FeedPath(config);
        using var gate = new FileStream(Path.Combine(config.State, "publish.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Rules.Need(Rules.HashFile(liveFeed) == request.ExpectedFeedSha256, "Current release changed");
        var current = Rules.VerifyFeed(File.ReadAllBytes(liveFeed), config);
        Rules.Need(DailyVersion.Parse(request.Version) >= DailyVersion.Parse(current.Releases[0].Version), "Repair release cannot go backwards");
        // Public history is immutable even when adding repair metadata separately.
        if (current.Releases.Any(r => r.Version == request.Version))
        {
            var proposed = Rules.VerifyFeed(Convert.FromBase64String(header.Feed), config).Releases[0];
            Rules.Need(Rules.Hash(Rules.Bytes(current.Releases.Single(r => r.Version == request.Version))) == Rules.Hash(Rules.Bytes(proposed)), "Existing release changed");
        }
        string claim = Path.Combine(config.State, "claims", request.Nonce);
        Rules.NoLinks(claim); Directory.CreateDirectory(Path.GetDirectoryName(claim)!);
        using (var stream = new FileStream(claim, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
        string folder = Path.Combine(config.State, "repairs", request.Nonce);
        Rules.NoLinks(folder); Directory.CreateDirectory(folder);
        Rules.Atomic(Path.Combine(folder, "header.json"), bytes);
        string objects = Path.Combine(config.Public, "repair", "objects");
        Rules.NoLinks(objects); Directory.CreateDirectory(objects);
        string mirrorRoutes = Path.Combine(config.Public, ".mirror", "routes", "repair", "objects");
        Rules.NoLinks(mirrorRoutes); Directory.CreateDirectory(mirrorRoutes);
        try
        {
            foreach (var blob in request.Files)
            {
                string path = Path.Combine(folder, blob.FileName);
                using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[65536]; long remaining = blob.Bytes;
                    while (remaining > 0)
                    {
                        int read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token);
                        if (read == 0) throw new EndOfStreamException("Interrupted repair upload");
                        await output.WriteAsync(buffer.AsMemory(0, read), token); remaining -= read;
                    }
                    output.Flush(true);
                }
                Rules.CheckArchive(path, blob);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }
            Rules.Need(await input.ReadAsync(new byte[1], token) == 0, "Unexpected repair payload");
            foreach (var blob in blocks.Values)
            {
                string destination = Path.Combine(objects, blob.FileName), staged = Path.Combine(folder, blob.FileName);
                Rules.NoLinks(destination);
                bool valid = File.Exists(destination) && new FileInfo(destination).Length == blob.Bytes && Rules.HashFile(destination) == blob.Sha256;
                if (valid) { if (File.Exists(staged)) File.Delete(staged); }
                else { Rules.CheckArchive(staged, blob); File.Move(staged, destination, true); }
            }
            // No manifest is exposed until every origin block is durable. CDN is
            // optional: a failed verification leaves this object on the origin.
            if (config.MirrorBaseUrl != null)
            {
                using var ownedClient = verificationClient == null ? Mirror.Client() : null;
                var client = verificationClient ?? ownedClient!;
                // Origin integrity is mandatory and already verified above. An
                // unavailable optional mirror must not consume the publication's
                // entire deadline, especially for origin-only repair releases.
                using var optionalMirror = CancellationTokenSource.CreateLinkedTokenSource(token);
                optionalMirror.CancelAfter(mirrorBudget ?? TimeSpan.FromSeconds(30));
                try
                {
                    await Parallel.ForEachAsync(blocks.Values, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = optionalMirror.Token }, async (blob, ct) =>
                    {
                        string marker = Path.Combine(mirrorRoutes, blob.FileName);
                        try
                        {
                            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct); bounded.CancelAfter(TimeSpan.FromSeconds(45));
                            using var response = await client.GetAsync(Mirror.BaseUrl(config) + "/repair/objects/" + blob.FileName, HttpCompletionOption.ResponseHeadersRead, bounded.Token);
                            response.EnsureSuccessStatusCode();
                            Rules.Need(response.Content.Headers.ContentLength == blob.Bytes, "CDN repair block length differs");
                            using var stream = await response.Content.ReadAsStreamAsync(bounded.Token);
                            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                            byte[] buffer = new byte[65536]; long total = 0; int read;
                            while ((read = await stream.ReadAsync(buffer, bounded.Token)) > 0)
                            { total += read; Rules.Need(total <= blob.Bytes, "CDN repair block too large"); digest.AppendData(buffer, 0, read); }
                            Rules.Need(total == blob.Bytes && Convert.ToHexString(digest.GetHashAndReset()).Equals(blob.Sha256, StringComparison.OrdinalIgnoreCase), "CDN repair block hash differs");
                            Rules.Atomic(marker, Rules.Bytes(blob));
                        }
                        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
                        { if (File.Exists(marker)) File.Delete(marker); }
                    });
                }
                catch (OperationCanceledException) when (optionalMirror.IsCancellationRequested && !token.IsCancellationRequested)
                {
                    // Only bodies fully verified above receive redirect markers.
                    // Unverified content remains available from the verified origin.
                }
            }
            Rules.Need(Rules.HashFile(liveFeed) == request.ExpectedFeedSha256, "Current release changed");
            string metadata = Path.Combine(config.Public, "repair", "v" + request.Version); Rules.NoLinks(metadata); Directory.CreateDirectory(metadata);
            for (int i = 0; i < manifests.Length; i++)
            {
                string target = Path.Combine(metadata, DailyUpdateRepairManifest.Name(request.Version, manifests[i].Flavor));
                Rules.NoLinks(target);
                if (File.Exists(target)) Rules.Need(Rules.HashFile(target) == Rules.Hash(Convert.FromBase64String(header.Manifests[i])), "Immutable repair manifest differs");
            }
            for (int i = 0; i < manifests.Length; i++)
            {
                string target = Path.Combine(metadata, DailyUpdateRepairManifest.Name(request.Version, manifests[i].Flavor));
                byte[] envelope = Convert.FromBase64String(header.Manifests[i]);
                if (File.Exists(target)) Rules.Need(Rules.HashFile(target) == Rules.Hash(envelope), "Immutable repair manifest differs");
                else Rules.Atomic(target, envelope);
            }
            var result = new { status = "completed", version = request.Version, nonce = request.Nonce, objects = blocks.Count,
                cloudObjects = blocks.Keys.Count(n => File.Exists(Path.Combine(mirrorRoutes, n))), feedUnchanged = true, atUtc = DateTimeOffset.UtcNow };
            Rules.Atomic(Path.Combine(folder, "result.json"), Rules.Bytes(result));
            return result;
        }
        catch (Exception error)
        {
            Rules.Atomic(Path.Combine(folder, "result.json"), Rules.Bytes(new { status = "failed", error = error.Message, atUtc = DateTimeOffset.UtcNow }));
            throw;
        }
    }
}
