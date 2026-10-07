using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dustweave;

internal static class UpdateDeltaCases
{
    public static async Task Run(string root, List<string> cases)
    {
        void Check(string name, bool pass) { if (!pass) throw new Exception(name); cases.Add(name); }
        void Reject(string name, Action action) { try { action(); } catch (InvalidDataException) { cases.Add(name); return; } throw new Exception(name); }
        string folder = Path.Combine(root, "delta"); Directory.CreateDirectory(folder);
        byte[] original = new byte[2 * 1024 * 1024]; new Random(417).NextBytes(original);
        byte[] next = original[..33333].Concat(Encoding.UTF8.GetBytes("new bytes shift the remaining file")).Concat(original[33333..]).ToArray();
        next[122333] ^= 1;
        string Zip(string name, string version, Dictionary<string, byte[]> files)
        {
            string path = Path.Combine(folder, name);
            files["update-package.json"] = JsonSerializer.SerializeToUtf8Bytes(new DailyUpdatePackage(version, "Lite", files.Keys.Append("update-package.json").Distinct().Order().ToArray()), DailyJson.Options);
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var file in files) { using var stream = archive.CreateEntry(file.Key).Open(); stream.Write(file.Value); }
            return path;
        }
        var prior = new Dictionary<string, byte[]> { ["Dustweave.exe"] = original, ["data/unchanged.json"] = Encoding.UTF8.GetBytes("{}"), ["docs/obsolete.md"] = [1] };
        var target = new Dictionary<string, byte[]> { ["Dustweave.exe"] = next, ["data/unchanged.json"] = Encoding.UTF8.GetBytes("{}"), ["docs/new.md"] = Encoding.UTF8.GetBytes("new file"), ["docs/empty.md"] = [] };
        string oldZip = Zip("old.zip", "0.9.1", prior), newZip = Zip("new.zip", "0.9.3", target);
        string installed = Path.Combine(folder, "installed"); ZipFile.ExtractToDirectory(oldZip, installed);
        var delta = DailyUpdateDeltaPackage.Create(oldZip, newZip, Path.Combine(folder, "out"));
        var asset = new DailyUpdateAsset("Lite", "Dustweave-0.9.3-Lite-win-x64.zip", new FileInfo(newZip).Length, DailyUpdateTransaction.Hash(newZip));
        var notes = new Dictionary<string, string[]> { ["zh-CN"] = ["更新"], ["zh-TW"] = ["更新"], ["en-US"] = ["Update"] };
        var release = new DailyUpdateRelease("0.9.3", null, 1, notes, [asset], [delta]);
        string patch = Path.Combine(folder, "out", delta.FileName);
        Check("content-defined patch reuses shifted binary regions", delta.Bytes < asset.Bytes / 10);
        var manifest = DailyUpdateDeltaPackage.Read(patch, release, asset, delta);
        Check("delta requires matching version and flavor", DailyUpdateDeltaPackage.CanApply(installed, manifest) &&
            !DailyUpdateDeltaPackage.CanApply(installed, manifest with { Flavor = "Portable" }) &&
            !DailyUpdateDeltaPackage.CanApply(installed, manifest with { FromVersion = "0.9.2" }));
        string staging = Path.Combine(folder, "staging");
        string[] names = DailyUpdateDeltaPackage.Extract(patch, installed, staging, release, asset, delta);
        Check("delta reconstructs every target byte including new and empty files", target.All(f => File.ReadAllBytes(Path.Combine(staging, f.Key)).SequenceEqual(f.Value)));
        Check("deleted files do not survive into the new inventory", !names.Contains("docs/obsolete.md"));
        File.WriteAllText(Path.Combine(installed, "user.keep"), "keep");
        var transaction = DailyUpdateTransaction.Prepare(staging, installed, Path.Combine(folder, "backup"), names);
        transaction = DailyUpdateTransaction.Apply(transaction);
        Check("delta uses the normal replacement transaction and preserves user files", !File.Exists(Path.Combine(installed, "docs/obsolete.md")) && File.ReadAllText(Path.Combine(installed, "user.keep")) == "keep");
        DailyUpdateTransaction.Restore(transaction);
        Check("delta rollback restores original files and retired files", File.ReadAllBytes(Path.Combine(installed, "Dustweave.exe")).SequenceEqual(original) && File.Exists(Path.Combine(installed, "docs/obsolete.md")) && !File.Exists(Path.Combine(installed, "docs/new.md")));
        Reject("tampered delta digest is rejected", () => DailyUpdateDeltaPackage.Read(patch, release, asset, delta with { Sha256 = new string('0', 64) }));
        Reject("delta cannot target a different full archive", () => DailyUpdateDeltaPackage.Read(patch, release, asset with { Sha256 = new string('0', 64) }, delta));
        using (var change = new FileStream(Path.Combine(installed, "Dustweave.exe"), FileMode.Open, FileAccess.Write)) change.WriteByte(9);
        Check("version label alone never authorizes base bytes", !DailyUpdateDeltaPackage.CanApply(installed, manifest));
        Reject("modified base is rejected before reconstruction", () => DailyUpdateDeltaPackage.Extract(patch, installed, Path.Combine(folder, "bad-stage"), release, asset, delta));
        File.WriteAllBytes(Path.Combine(installed, "Dustweave.exe"), original);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var trust = new DailyUpdateTrust(new() { ["test"] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) }, [new("cn", "https://updates.test/updates.json", "https://updates.test")]);
        var feed = new DailyUpdateFeed(2, "Dustweave", "stable", [release]);
        var signed = DailyUpdateSignatures.Sign(feed, "test", key);
        var verified = new DailyVerifiedUpdate(feed, signed, "cn");
        var transport = new DailyUpdateTransport(trust);
        int fullDownloads = 0, deltaDownloads = 0; bool corrupt = false, cancel = false, unavailable = false, resumed = false;
        using var client = new HttpClient(new Handler(request =>
        {
            if (cancel) throw new OperationCanceledException();
            bool isDelta = request.RequestUri!.AbsolutePath.EndsWith(".delta.zip");
            if (isDelta) deltaDownloads++; else fullDownloads++;
            if (isDelta && unavailable) return new(HttpStatusCode.NotFound);
            byte[] bytes = File.ReadAllBytes(isDelta ? patch : newZip);
            if (isDelta && corrupt) bytes[0] ^= 1;
            int offset = (int)(request.Headers.Range?.Ranges.Single().From ?? 0);
            resumed |= offset > 0;
            var response = new HttpResponseMessage(offset > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(bytes[offset..]) };
            if (offset > 0) response.Content.Headers.ContentRange = new(offset, bytes.Length - 1, bytes.Length);
            return response;
        }));
        string cacheRoot = Path.Combine(folder, "network");
        string ready = await transport.DownloadAsync(client, verified, "0.9.3", "Lite", cacheRoot, null, default, installed);
        Check("valid delta skips full package download and intermediate versions", deltaDownloads == 1 && fullDownloads == 0 && DailyJson.TryRead<DailyUpdateDownload>(Path.Combine(ready, "download-choice.json"))?.DeltaFileName == delta.FileName);
        await transport.DownloadAsync(client, verified, "0.9.3", "Lite", cacheRoot, null, default, installed);
        Check("cached delta avoids repeat network transfers", deltaDownloads == 1 && fullDownloads == 0);
        string resumeRoot = Path.Combine(folder, "resume"), partial = Path.Combine(resumeRoot, "updates/0.9.3-Lite", delta.FileName + ".partial");
        Directory.CreateDirectory(Path.GetDirectoryName(partial)!); File.WriteAllBytes(partial, File.ReadAllBytes(patch)[..100]);
        await transport.DownloadAsync(client, verified, "0.9.3", "Lite", resumeRoot, null, default, installed);
        Check("delta partial download resumes with Range", resumed);
        corrupt = true;
        ready = await transport.DownloadAsync(client, verified, "0.9.3", "Lite", Path.Combine(folder, "corrupt"), null, default, installed);
        Check("corrupt delta automatically falls back to full package", fullDownloads == 1 && File.Exists(Path.Combine(ready, "package.zip")) && DailyJson.TryRead<DailyUpdateDownload>(Path.Combine(ready, "download-choice.json"))?.DeltaFileName == null);
        corrupt = false; unavailable = true;
        await transport.DownloadAsync(client, verified, "0.9.3", "Lite", Path.Combine(folder, "missing-patch"), null, default, installed);
        Check("missing server patch automatically falls back", fullDownloads == 2);
        unavailable = false; File.WriteAllBytes(Path.Combine(installed, "Dustweave.exe"), [0]);
        await transport.DownloadAsync(client, verified, "0.9.3", "Lite", Path.Combine(folder, "changed-base"), null, default, installed);
        Check("changed local installation falls back without user intervention", fullDownloads == 3);
        File.WriteAllBytes(Path.Combine(installed, "Dustweave.exe"), original);
        int requests = fullDownloads + deltaDownloads;
        cancel = true; using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await transport.DownloadAsync(client, verified, "0.9.3", "Lite", Path.Combine(folder, "canceled"), null, canceled.Token, installed); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { Check("cancelled delta does not initiate full fallback", fullDownloads + deltaDownloads == requests); }
        Reject("delta path traversal in feed is rejected", () => DailyUpdates.Validate(feed with { Releases = [release with { Deltas = [delta with { FileName = "../bad.zip" }] }] }));
        Reject("duplicate base/flavor deltas are rejected", () => DailyUpdates.Validate(feed with { Releases = [release with { Deltas = [delta, delta] }] }));
        cancel = false;
        var futureFeed = feed with { Releases = [release with { Deltas = [delta with { Algorithm = "future-v2" }] }] };
        var futureSigned = DailyUpdateSignatures.Sign(futureFeed, "test", key);
        int beforeFuture = deltaDownloads;
        await transport.DownloadAsync(client, new(futureFeed, futureSigned, "cn"), "0.9.3", "Lite", Path.Combine(folder, "future"), null, default, installed);
        Check("unknown future delta format retains full-package upgrade", deltaDownloads == beforeFuture && fullDownloads == 4);
        // Authenticated but malformed package fixtures exercise the parser independently of hashes.
        string malformed = Path.Combine(folder, "malformed.zip"); File.Copy(patch, malformed);
        void ReplaceManifest(DailyDeltaManifest value)
        {
            using var zip = ZipFile.Open(malformed, ZipArchiveMode.Update);
            zip.GetEntry("delta.json")!.Delete();
            using var stream = zip.CreateEntry("delta.json").Open(); JsonSerializer.Serialize(stream, value, DailyJson.Options);
        }
        ReplaceManifest(manifest with { Files = manifest.Files.Select((f,i) => i == 0 ? f with { Name = "../outside" } : f).ToArray() });
        var malformedDelta = delta with { Bytes = new FileInfo(malformed).Length, Sha256 = DailyUpdateTransaction.Hash(malformed) };
        Reject("authenticated traversal in delta manifest is rejected", () => DailyUpdateDeltaPackage.Read(malformed, release, asset, malformedDelta));
        ReplaceManifest(manifest);
        using (var zip = ZipFile.Open(malformed, ZipArchiveMode.Update))
        {
            var member = manifest.Files.First(f => f.Mode == "patch").Payload!;
            zip.GetEntry(member)!.Delete(); using var stream = zip.CreateEntry(member).Open();
            using var compressed = new BrotliStream(stream, CompressionLevel.Optimal);
            using var writer = new BinaryWriter(compressed);
            writer.Write(0x31574444); writer.Write((byte)1); writer.Write(-1L); writer.Write(10); writer.Write((byte)255);
        }
        malformedDelta = delta with { Bytes = new FileInfo(malformed).Length, Sha256 = DailyUpdateTransaction.Hash(malformed) };
        Reject("out-of-range COPY instruction cannot read arbitrary data", () => DailyUpdateDeltaPackage.Extract(malformed, installed, Path.Combine(folder, "malformed-stage"), release, asset, malformedDelta));
        Check("all delta checks leave original installation intact", File.ReadAllBytes(Path.Combine(installed, "Dustweave.exe")).SequenceEqual(original));
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); }
    }
}
