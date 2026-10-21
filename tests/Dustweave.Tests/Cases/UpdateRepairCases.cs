using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Dustweave;

internal static class UpdateRepairCases
{
    public static async Task Run(string root, List<string> cases)
    {
        void Check(string name, bool condition) { if (!condition) throw new Exception(name); cases.Add(name); }
        void Reject(string name, Action action) { try { action(); } catch (InvalidDataException) { cases.Add(name); return; } throw new Exception(name); }
        string folder = Path.Combine(root, "repair"), installed = Path.Combine(folder, "installation");
        Directory.CreateDirectory(installed);
        var data = new Dictionary<string, byte[]>
        {
            ["Dustweave.exe"] = [1, 2, 3],
            ["update-package.json"] = JsonSerializer.SerializeToUtf8Bytes(new DailyUpdatePackage("1.4.13", "Lite",
                ["Dustweave.exe", "app/obsolete.dll", "accounts.json"], 2, "8.0.30")),
            ["app/support/Dustweave.Updater.exe"] = [7, 8, 9],
            ["app/large.dll"] = new byte[DailyUpdateRepairManifest.BlockBytes * 2 + 7777],
            ["app/missing.dll"] = new byte[40000],
            ["app/empty.dll"] = []
        };
        new Random(1289).NextBytes(data["app/large.dll"]);
        new Random(941).NextBytes(data["app/missing.dll"]);
        var blocks = new Dictionary<string, byte[]>();
        var files = data.Select(pair =>
        {
            var parts = new List<DailyRepairBlock>();
            for (int offset = 0; offset < pair.Value.Length; offset += DailyUpdateRepairManifest.BlockBytes)
            {
                byte[] block = pair.Value.Skip(offset).Take(DailyUpdateRepairManifest.BlockBytes).ToArray();
                string hash = DailyUpdateRepairManifest.Hash(block); blocks[hash + ".bin"] = block;
                parts.Add(new(hash, block.Length));
            }
            return new DailyRepairFile(pair.Key, pair.Value.Length, DailyUpdateRepairManifest.Hash(pair.Value), parts.ToArray());
        }).ToArray();
        var notes = new Dictionary<string, string[]> { ["zh-CN"] = ["修复"], ["zh-TW"] = ["修復"], ["en-US"] = ["Repair"] };
        var asset = new DailyUpdateAsset("Lite", "Dustweave-1.4.13-Lite-win-x64.zip", 10000000, new('a', 64));
        var release = new DailyUpdateRelease("1.4.13", null, 2, notes, [asset], [], "8.0.30");
        var feed = new DailyUpdateFeed(3, "Dustweave", "stable", [release]);
        var manifest = new DailyRepairManifest(1, "Dustweave", "1.4.13", "Lite", 2, "8.0.30", asset.Sha256, files);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var trust = new DailyUpdateTrust(new() { ["test"] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) },
            [new("test", "https://repair.example/feed", "https://repair.example/files") { OriginFallback = true }]);
        var signed = DailyUpdateSignatures.Sign(feed, "test", key);
        var verified = new DailyVerifiedUpdate(feed, signed, "test");
        var envelope = DailyUpdateRepairManifest.Sign(manifest, "test", key);
        byte[] envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        Check("repair inventory signature verifies", DailyUpdateRepairManifest.Verify(envelopeBytes, trust.Keys).Files.Length == files.Length);
        Reject("release feed signatures cannot authorize repair", () => DailyUpdateRepairManifest.Verify(JsonSerializer.SerializeToUtf8Bytes(signed), trust.Keys));
        Reject("repair cannot target another edition", () => DailyUpdateRepair.Bind(manifest with { Flavor = "Portable" }, release, asset));
        Reject("repair cannot claim a different full archive", () => DailyUpdateRepair.Bind(manifest with { ArchiveSha256 = new('b', 64) }, release, asset));
        Reject("repair refuses user plugin paths", () => DailyUpdateRepairManifest.Validate(manifest with { Files = [.. files, files[0] with { Name = "app/plugins/user.dll" }] }));
        Reject("repair refuses traversal", () => DailyUpdateRepairManifest.Validate(manifest with { Files = [.. files, files[0] with { Name = "app/../outside.dll" }] }));
        Reject("repair refuses duplicate case aliases", () => DailyUpdateRepairManifest.Validate(manifest with { Files = [.. files, files[0] with { Name = "dustweave.EXE" }] }));
        Reject("repair refuses invalid block sizes", () => DailyUpdateRepairManifest.Validate(manifest with { Files = files.Select(f => f.Name == "app/large.dll" ? f with { Blocks = [new(new('a', 64), 1)] } : f).ToArray() }));
        Reject("repair signature cannot be changed", () => DailyUpdateRepairManifest.Verify(JsonSerializer.SerializeToUtf8Bytes(envelope with { Signature = Convert.ToBase64String(new byte[64]) }), trust.Keys));
        foreach (string name in new[] { "app/CON.dll", "app/hello?.dll", "app/data/../../login.json", "C:/outside.dll", "app/foo.dll:stream" })
            Check("repair path rejects " + name, !DailyUpdates.AllowedFile(name));
        foreach (var pair in data)
        {
            string path = Path.Combine(installed, pair.Key); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, pair.Value);
        }
        File.WriteAllText(Path.Combine(installed, "accounts.json"), "private-account");
        Directory.CreateDirectory(Path.Combine(installed, "plugins"));
        File.WriteAllText(Path.Combine(installed, "plugins", "private.dll"), "private-plugin");
        File.WriteAllText(Path.Combine(installed, "app", "obsolete.dll"), "unknown-owned-file");
        var handler = new Handler(envelopeBytes, blocks);
        using var client = new HttpClient(handler);
        var service = new DailyUpdateRepair(trust);
        var healthy = await service.InspectAsync(client, verified, manifest.Version, manifest.Flavor, installed, folder, null, default);
        Check("healthy install requires no file payload download", healthy.Problems.Length == 0 && healthy.DownloadBytes == 0 && handler.BlockRequests.Count == 0);
        string largePath = Path.Combine(installed, "app", "large.dll");
        var damaged = data["app/large.dll"].ToArray(); damaged[DailyUpdateRepairManifest.BlockBytes + 50] ^= 0xff; File.WriteAllBytes(largePath, damaged);
        File.Delete(Path.Combine(installed, "app", "missing.dll"));
        File.Delete(Path.Combine(installed, "app", "empty.dll"));
        var plan = await service.InspectAsync(client, verified, manifest.Version, manifest.Flavor, installed, folder, null, default);
        Check("same-size corruption and missing files are found", plan.Problems.Length == 3 && plan.Problems.Count(p => p.Missing) == 2);
        Check("large file repair downloads only one damaged block", plan.DownloadBytes == DailyUpdateRepairManifest.BlockBytes + 40000);
        var badBlock = files.Single(f => f.Name == "app/large.dll").Blocks[1];
        string objectDirectory = Path.Combine(plan.Directory, "repair-objects"); Directory.CreateDirectory(objectDirectory);
        File.WriteAllBytes(Path.Combine(objectDirectory, badBlock.Sha256 + ".bin.partial"), blocks[badBlock.Sha256 + ".bin"].Take(1234).ToArray());
        handler.BadFirstHash = badBlock.Sha256;
        await service.PrepareAsync(client, plan, folder, null, default);
        Check("repair resumes cached block and retries corrupt CDN through origin", handler.SawRange && handler.SawOrigin);
        Check("preparing a repair never changes installation", File.ReadAllBytes(largePath).SequenceEqual(damaged) && !File.Exists(Path.Combine(installed, "app", "missing.dll")));
        string staging = DailyUpdateRepair.Staging(plan.Directory);
        Check("staged large file reconstructed with local healthy blocks", DailyUpdateRepair.Valid(Path.Combine(staging, "app/large.dll"), files[3].Bytes, files[3].Sha256));
        Check("empty missing files require no network object", !handler.BlockRequests.Contains(DailyUpdateRepairManifest.Hash([]) + ".bin"));
        var names = DailyUpdateRepair.VerifyStaging(manifest, installed, staging, out var retained);
        var journal = DailyUpdateTransaction.Prepare(staging, installed, Path.Combine(folder, "backup"), names, retained, removeObsolete: false);
        Check("repair journal contains only damaged signed files", journal.Files.Length == 3);
        try { DailyUpdateTransaction.Apply(journal, i => { if (i == 1) throw new IOException("Simulated power loss"); }); } catch (IOException) { }
        journal = DailyUpdateTransaction.Read(journal.Backup, installed);
        journal = DailyUpdateTransaction.Restore(journal);
        Check("interrupted repair restores exact original corruption and missing-file state", File.ReadAllBytes(largePath).SequenceEqual(damaged) && !File.Exists(Path.Combine(installed, "app", "missing.dll")));
        journal = DailyUpdateTransaction.Prepare(staging, installed, Path.Combine(folder, "backup2"), names, retained, removeObsolete: false);
        journal = DailyUpdateTransaction.Apply(journal);
        var after = DailyUpdateRepair.Inspect(verified, manifest, plan.Directory, installed);
        Check("applied repair matches every signed file", after.Problems.Length == 0);
        Check("repair preserves account plugin and unknown files despite corrupt metadata", File.ReadAllText(Path.Combine(installed, "accounts.json")) == "private-account" &&
            File.ReadAllText(Path.Combine(installed, "plugins", "private.dll")) == "private-plugin" &&
            File.ReadAllText(Path.Combine(installed, "app", "obsolete.dll")) == "unknown-owned-file");
        File.WriteAllBytes(largePath, damaged);
        File.WriteAllBytes(Path.Combine(staging, "app/large.dll"), [99]);
        Reject("changed staging is rejected before replacement", () => DailyUpdateRepair.VerifyStaging(manifest, installed, staging, out _));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await service.InspectAsync(client, verified, manifest.Version, manifest.Flavor, installed, folder, null, cancelled.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { cases.Add("repair inspection respects cancellation"); }
    }
    private sealed class Handler(byte[] envelope, Dictionary<string, byte[]> blocks) : HttpMessageHandler
    {
        public List<string> BlockRequests = new();
        public string? BadFirstHash;
        public bool SawRange, SawOrigin;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.EndsWith("-repair.json")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(envelope) });
            string name = Path.GetFileName(uri.AbsolutePath); BlockRequests.Add(name);
            byte[] data = blocks[name];
            long offset = request.Headers.Range?.Ranges.First().From ?? 0;
            SawRange |= offset > 0; SawOrigin |= uri.Query == "?origin=1";
            var bytes = data.Skip((int)offset).ToArray();
            if (name == BadFirstHash + ".bin" && uri.Query != "?origin=1" && bytes.Length > 0) bytes[0] ^= 0xff;
            var response = new HttpResponseMessage(offset > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            if (offset > 0) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, data.Length - 1, data.Length);
            return Task.FromResult(response);
        }
    }
}
