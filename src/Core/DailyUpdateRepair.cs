using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dustweave;

public sealed record DailyRepairProblem(string Name, bool Missing, long Bytes);
public sealed record DailyRepairProgress(string Stage, int Completed, int Total, string? File = null);
public sealed record DailyRepairPlan(DailyVerifiedUpdate Verified, DailyRepairManifest Manifest, string Directory,
    string InstallRoot, DailyRepairProblem[] Problems, long DownloadBytes);

public sealed class DailyUpdateRepair(DailyUpdateTrust trust)
{
    public static DailyUpdateRepair Production() => new(DailyUpdateTrust.Production());
    public static void Bind(DailyRepairManifest manifest, DailyUpdateRelease release, DailyUpdateAsset asset)
    {
        if (manifest.Version != release.Version || manifest.Flavor != asset.Flavor ||
            manifest.Layout != release.Layout || manifest.RuntimeVersion != release.RuntimeVersion ||
            !manifest.ArchiveSha256.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("updates.repair_manifest");
    }
    public static string ManifestPath(string directory) => Path.Combine(directory, "repair-manifest.json");
    public static string Staging(string directory) => Path.Combine(directory, "repair-staging");
    public static DailyRepairManifest ReadVerified(string directory, DailyUpdateRelease release, DailyUpdateAsset asset)
    {
        string path = ManifestPath(directory); DailyUpdates.EnsureNoLinks(path);
        if (new FileInfo(path).Length > DailyUpdateRepairManifest.MaxEnvelopeBytes) throw new InvalidDataException("updates.repair_manifest");
        var manifest = DailyUpdateRepairManifest.Verify(File.ReadAllBytes(path), DailyUpdateTrust.Production().Keys);
        Bind(manifest, release, asset);
        return manifest;
    }
    public async Task<DailyRepairPlan> InspectAsync(HttpClient client, DailyVerifiedUpdate verified, string version,
        string flavor, string installRoot, string root, IProgress<DailyRepairProgress>? progress, CancellationToken token)
    {
        using var storage = DailyUpdateHost.AcquireStorage(root);
        var feed = DailyUpdateSignatures.Verify(verified.Signed, trust);
        var release = feed.Releases.SingleOrDefault(r => r.Version == version) ?? throw new InvalidDataException("updates.repair_unavailable");
        var asset = release.Assets.Single(a => a.Flavor == flavor);
        if (release.Layout != DailyUpdates.CurrentLayout) throw new InvalidDataException("updates.manual_layout");
        string directory = Path.Combine(root, "updates", version + "-" + flavor);
        DailyUpdates.EnsureNoLinks(directory); DailyUpdates.EnsureNoLinks(installRoot); Directory.CreateDirectory(directory);
        using var lease = new FileStream(Path.Combine(directory, "download.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        progress?.Report(new("checking", 0, 1));
        DailyRepairManifest? manifest = null;
        byte[]? envelope = null;
        // Versioned inventories are immutable and authenticated against this
        // release's archive hash. A verified cached copy survives network loss.
        string cachedManifest = ManifestPath(directory);
        if (File.Exists(cachedManifest))
        {
            try
            {
                DailyUpdates.EnsureNoLinks(cachedManifest);
                if (new FileInfo(cachedManifest).Length > DailyUpdateRepairManifest.MaxEnvelopeBytes) throw new InvalidDataException("updates.repair_manifest");
                envelope = File.ReadAllBytes(cachedManifest);
                manifest = DailyUpdateRepairManifest.Verify(envelope, trust.Keys); Bind(manifest, release, asset);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { manifest = null; envelope = null; }
        }
        foreach (var source in trust.Sources.OrderBy(s => s.Id == verified.SourceId ? 0 : 1))
        {
            if (manifest != null) break;
            token.ThrowIfCancellationRequested();
            try
            {
                var uri = new Uri(source.PackageBaseUrl.TrimEnd('/') + "/repair/v" + version + "/" + DailyUpdateRepairManifest.Name(version, flavor));
                if (uri.Scheme != "https" || uri.UserInfo != "" || uri.Fragment != "") throw new InvalidDataException("updates.repair_manifest");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                response.EnsureSuccessStatusCode();
                using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var output = new MemoryStream();
                byte[] buffer = new byte[65536];
                int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    if (output.Length + read > DailyUpdateRepairManifest.MaxEnvelopeBytes) throw new InvalidDataException("updates.repair_manifest");
                    output.Write(buffer, 0, read);
                }
                envelope = output.ToArray();
                manifest = DailyUpdateRepairManifest.Verify(envelope, trust.Keys);
                Bind(manifest, release, asset);
                break;
            }
            catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException)
            { token.ThrowIfCancellationRequested(); }
        }
        if (manifest == null || envelope == null) throw new InvalidDataException("updates.repair_unavailable");
        DailyUpdateJson.Write(Path.Combine(directory, "signed-feed.json"), verified.Signed);
        File.WriteAllBytes(ManifestPath(directory), envelope);
        return await Task.Run(() => Inspect(verified, manifest, directory, installRoot, progress, token), token);
    }
    public static DailyRepairPlan Inspect(DailyVerifiedUpdate verified, DailyRepairManifest manifest, string directory,
        string installRoot, IProgress<DailyRepairProgress>? progress = null, CancellationToken token = default)
    {
        DailyUpdateRepairManifest.Validate(manifest);
        var problems = new List<DailyRepairProblem>();
        var needed = new Dictionary<string, DailyRepairBlock>(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (var file in manifest.Files)
        {
            token.ThrowIfCancellationRequested();
            string path = Path.Combine(installRoot, file.Name); DailyUpdates.EnsureNoLinks(path);
            if (!Valid(path, file.Bytes, file.Sha256))
            {
                problems.Add(new(file.Name, !File.Exists(path), file.Bytes));
                using var input = File.Exists(path) ? File.OpenRead(path) : null;
                long offset = 0;
                foreach (var block in file.Blocks)
                {
                    token.ThrowIfCancellationRequested();
                    if (!Matches(input, offset, block) && !Valid(ObjectPath(directory, block), block.Bytes, block.Sha256))
                        needed[block.Sha256] = block;
                    offset += block.Bytes;
                }
            }
            progress?.Report(new("checking", ++count, manifest.Files.Length, file.Name));
        }
        return new(verified, manifest, directory, Path.GetFullPath(installRoot), problems.ToArray(), needed.Values.Sum(b => (long)b.Bytes));
    }
    public async Task PrepareAsync(HttpClient client, DailyRepairPlan plan, string root,
        IProgress<DailyRepairProgress>? progress, CancellationToken token)
    {
        using var storage = DailyUpdateHost.AcquireStorage(root);
        using var lease = new FileStream(Path.Combine(plan.Directory, "download.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        // The plan is only a preview. Recheck every byte during staging, including
        // blocks that were healthy earlier; no local hash list authorizes a write.
        var release = DailyUpdateSignatures.Verify(plan.Verified.Signed, trust).Releases.Single(r => r.Version == plan.Manifest.Version);
        var signed = DailyUpdateRepairManifest.Verify(File.ReadAllBytes(ManifestPath(plan.Directory)), trust.Keys);
        Bind(signed, release, release.Assets.Single(a => a.Flavor == signed.Flavor));
        var transport = new DailyUpdateTransport(trust);
        string staging = Staging(plan.Directory); DailyUpdates.EnsureNoLinks(staging); Directory.CreateDirectory(staging);
        string objects = Path.Combine(plan.Directory, "repair-objects"); DailyUpdates.EnsureNoLinks(objects); Directory.CreateDirectory(objects);
        int count = 0;
        foreach (var file in signed.Files)
        {
            token.ThrowIfCancellationRequested();
            string local = Path.Combine(plan.InstallRoot, file.Name), destination = Path.Combine(staging, file.Name);
            if (Valid(local, file.Bytes, file.Sha256)) { count++; continue; }
            if (!Valid(destination, file.Bytes, file.Sha256))
            {
                DailyUpdates.EnsureNoLinks(destination); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string temporary = destination + ".repair-partial"; DailyUpdates.EnsureNoLinks(temporary);
                try
                {
                    using (var input = File.Exists(local) ? File.OpenRead(local) : null)
                    using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        long offset = 0;
                        foreach (var block in file.Blocks)
                        {
                            token.ThrowIfCancellationRequested();
                            byte[]? reusable = ReadBlock(input, offset, block);
                            if (reusable != null) await output.WriteAsync(reusable, token);
                            else
                            {
                                string cached = ObjectPath(plan.Directory, block);
                                await transport.DownloadPayload(client, plan.Verified, signed.Version, root, plan.Directory,
                                    DailyUpdateRepairManifest.ObjectName(block), cached, block.Bytes, block.Sha256, null, token,
                                    "repair/objects/" + DailyUpdateRepairManifest.ObjectName(block));
                                using var blockStream = File.OpenRead(cached);
                                await blockStream.CopyToAsync(output, token);
                            }
                            offset += block.Bytes;
                        }
                        output.Flush(true);
                    }
                    if (!Valid(temporary, file.Bytes, file.Sha256)) throw new InvalidDataException("updates.hash_failed");
                    File.Move(temporary, destination, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            progress?.Report(new("preparing", ++count, signed.Files.Length, file.Name));
        }
        VerifyStaging(signed, plan.InstallRoot, staging, out _);
        DailyUpdateJson.Write(Path.Combine(plan.Directory, "repair-ready.json"), new { signed.Version, signed.Flavor, atUtc = DateTimeOffset.UtcNow });
    }
    public static string[] VerifyStaging(DailyRepairManifest manifest, string installRoot, string staging,
        out Dictionary<string, string> retained)
    {
        retained = new(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            string local = Path.Combine(installRoot, file.Name);
            if (Valid(local, file.Bytes, file.Sha256)) retained.Add(file.Name, file.Sha256);
            else if (!Valid(Path.Combine(staging, file.Name), file.Bytes, file.Sha256))
                throw new InvalidDataException("updates.repair_changed");
        }
        return manifest.Files.Select(f => f.Name).ToArray();
    }
    public static string VerifiedHelper(DailyRepairManifest manifest, string installRoot, string staging)
    {
        var helper = manifest.Files.Single(f => f.Name == "app/support/Dustweave.Updater.exe");
        foreach (var path in new[] { Path.Combine(staging, helper.Name), Path.Combine(installRoot, helper.Name) })
            if (Valid(path, helper.Bytes, helper.Sha256)) return path;
        throw new InvalidDataException("updates.repair_changed");
    }
    public static bool Valid(string path, long bytes, string hash)
    {
        DailyUpdates.EnsureNoLinks(path);
        return File.Exists(path) && DailyUpdateDeltaPackage.VerifyFile(path, bytes, hash);
    }
    private static string ObjectPath(string directory, DailyRepairBlock block) => Path.Combine(directory, "repair-objects", DailyUpdateRepairManifest.ObjectName(block));
    private static bool Matches(Stream? stream, long offset, DailyRepairBlock block) => ReadBlock(stream, offset, block) != null;
    private static byte[]? ReadBlock(Stream? stream, long offset, DailyRepairBlock block)
    {
        if (stream == null || stream.Length < offset + block.Bytes) return null;
        byte[] buffer = new byte[block.Bytes]; stream.Position = offset; stream.ReadExactly(buffer);
        return DailyUpdateRepairManifest.Hash(buffer).Equals(block.Sha256, StringComparison.OrdinalIgnoreCase) ? buffer : null;
    }
}
