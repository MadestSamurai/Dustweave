using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Dustweave;

internal static class UpdateInventoryCases
{
    public static async Task Run(string root, List<string> cases)
    {
        void Check(string name, bool ok) { if (!ok) throw new Exception(name); cases.Add(name); }
        void Reject(string name, Action action) { try { action(); } catch (InvalidDataException) { cases.Add(name); return; } throw new Exception(name); }
        var notes = new Dictionary<string, string[]> { ["zh-CN"] = ["更新"], ["zh-TW"] = ["更新"], ["en-US"] = ["Update"] };
        string folder = Path.Combine(root, "inventory-upgrade"); Directory.CreateDirectory(folder);
        var oldData = new Dictionary<string, byte[]> { ["Dustweave.exe"] = [1], ["app/Dustweave.Core.dll"] = [2], ["app/coreclr.dll"] = [3],
            ["app/support/Dustweave.Updater.exe"] = [4], ["app/old.dll"] = [5] };
        var newData = new Dictionary<string, byte[]> { ["Dustweave.exe"] = [10], ["app/Dustweave.Core.dll"] = [20], ["app/coreclr.dll"] = [30],
            ["app/support/Dustweave.Updater.exe"] = [40], ["app/new.dll"] = [50] };
        byte[] stable = new byte[2 * 1024 * 1024 + 10]; new Random(9021).NextBytes(stable);
        oldData["app/stable.dll"] = stable; newData["app/stable.dll"] = stable;
        string Zip(string version, int protocol, string runtime, Dictionary<string, byte[]> data)
        {
            data["update-package.json"] = JsonSerializer.SerializeToUtf8Bytes(new DailyUpdatePackage(version, "Lite", data.Keys.Append("update-package.json").Distinct().ToArray(),
                2, runtime, ["Dustweave.exe", "app/coreclr.dll"], protocol), DailyUpdateJson.Options);
            string path = Path.Combine(folder, version + ".zip");
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var pair in data) { using var entry = zip.CreateEntry(pair.Key).Open(); entry.Write(pair.Value); }
            return path;
        }
        string beforeZip = Zip(DailyUpdateInventory.BridgeVersion, 1, "8.0.30", oldData), nextZip = Zip("1.4.15", 2, "8.0.31", newData);
        DailyUpdateRelease Release(string version, string? previous, string zip, int protocol, string runtime) => new(version, previous, 2, notes,
            [new("Lite", $"Dustweave-{version}-Lite-win-x64.zip", new FileInfo(zip).Length, DailyUpdateTransaction.Hash(zip))], [], runtime, protocol);
        var before = Release(DailyUpdateInventory.BridgeVersion, null, beforeZip, 1, "8.0.30");
        var after = Release("1.4.15", before.Version, nextZip, 2, "8.0.31");
        var feed = new DailyUpdateFeed(4, "Dustweave", "stable", [after, before]);
        var oldPackage = JsonSerializer.Deserialize<DailyUpdatePackage>(oldData["update-package.json"], DailyUpdateJson.Options)!;
        Check("inventory upgrade allows authenticated runtime and bootstrap replacement", DailyUpdates.ManualUpdateReason(feed, after, oldPackage) == null);
        Reject("legacy feed cannot activate a protocol-2 install", () => DailyUpdates.Validate(feed with { Schema = 3 }));
        Reject("an original old client cannot skip the capability bridge", () => DailyUpdateInventory.Validate(feed, after, "1.4.14", "Lite"));
        Reject("inventory upgrade cannot switch editions", () => DailyUpdateInventory.Validate(feed, after, before.Version, "Portable"));
        Reject("inventory upgrade cannot reinstall or downgrade", () => DailyUpdateInventory.Validate(feed, after, after.Version, "Lite"));
        var job = new DailyUpdateJob(folder, "Dustweave.exe", 0, 0, after, after.Assets[0]) { InventoryUpgrade = true, FromVersion = before.Version };
        DailyUpdateInventory.ValidateSource(job, feed, oldPackage);
        Reject("repair cannot masquerade as a capability upgrade", () => DailyUpdateInventory.ValidateJob(job with { Repair = true }, feed));
        Reject("changed source edition is rejected before installation", () => DailyUpdateInventory.ValidateSource(job, feed, oldPackage with { Flavor = "Portable" }));
        Reject("missing source descriptor is not guessed", () => DailyUpdateInventory.ValidateSource(job, feed, null));
        DailyUpdateInventory.ValidateJob(job, feed);
        Check("post-install acknowledgement validates intent without old descriptor", true);
        Check("bridge pauses desktop, scheduled and selected-account startup", new[] { Array.Empty<string>(), new[]{"--updated","nonce"}, new[]{"--scheduled"}, new[]{"--run-selected"} }.All(DailyUpdateBridge.IsInteractiveEntry));
        Check("bridge keeps isolated offline validation commands available", !DailyUpdateBridge.IsInteractiveEntry(["--smoke", folder]) && !DailyUpdateBridge.IsInteractiveEntry(["--identity", folder]));
        string installed = Path.Combine(folder, "installed"); ZipFile.ExtractToDirectory(beforeZip, installed);
        File.WriteAllText(Path.Combine(installed, "accounts.json"), "keep"); File.WriteAllText(Path.Combine(installed, "app", "foreign.dll"), "keep");
        // A mutable descriptor cannot authorize deleting an unrelated file.
        var delta = DailyUpdateDeltaPackage.Create(beforeZip, nextZip, Path.Combine(folder, "deltas"));
        Check("protocol-2 delta encodes startup and runtime changes", delta.Algorithm == DailyUpdateDeltaPackage.InventoryAlgorithm);
        after = after with { Deltas = [delta] }; feed = feed with { Releases = [after, before] };
        string patch = Path.Combine(folder, "deltas", delta.FileName), stage = Path.Combine(folder, "stage");
        DailyUpdateDeltaPackage.Extract(patch, installed, stage, after, after.Assets[0], delta, copyUnchanged: false);
        Check("startup/runtime changes reconstruct byte-exactly", File.ReadAllBytes(Path.Combine(stage, "Dustweave.exe")).SequenceEqual(newData["Dustweave.exe"]) &&
            File.ReadAllBytes(Path.Combine(stage, "app/coreclr.dll")).SequenceEqual(newData["app/coreclr.dll"]));
        Reject("new patch is rejected by legacy installer protocol", () => DailyUpdateDeltaPackage.Read(patch, after with { InstallerProtocol = 1 }, after.Assets[0], delta));
        var metadata = DailyUpdateDeltaPackage.Read(patch, after, after.Assets[0], delta);
        var retained = metadata.Files.Where(f => f.Mode == "copy").ToDictionary(f => f.Name, f => f.Sha256);
        for (int fault = 1; fault <= metadata.Files.Length - retained.Count + 1; fault++)
        {
            var journal = DailyUpdateTransaction.Prepare(stage, installed, Path.Combine(folder, "backup-" + fault), metadata.Files.Select(f => f.Name).ToArray(), retained,
                installRecoveryEntryFirst: true, previousFiles: oldData.Keys.ToArray());
            Check("recovery entry is first before CLR replacement at fault " + fault, journal.Files[0].Name == "Dustweave.exe");
            try { DailyUpdateTransaction.Apply(journal, i => { if (i == fault) throw new IOException("power loss"); }); } catch (IOException) { }
            var interrupted = DailyUpdateTransaction.Read(journal.Backup, installed); DailyUpdateTransaction.Restore(interrupted);
            Check("every interrupted replacement restores source bytes at fault " + fault, oldData.All(p => File.ReadAllBytes(Path.Combine(installed, p.Key)).SequenceEqual(p.Value)) && !File.Exists(Path.Combine(installed, "app/new.dll")));
        }
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var trust = new DailyUpdateTrust(new() { ["test"] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) }, [new("test", "https://updates.test/feed", "https://updates.test")]);
        var signed = DailyUpdateSignatures.Sign(feed, "test", key); var verified = new DailyVerifiedUpdate(feed, signed, "test");
        var objects = new Dictionary<string, byte[]>();
        DailyRepairManifest Manifest(DailyUpdateRelease release, Dictionary<string, byte[]> data)
        {
            var files = data.Select(p =>
            {
                var blocks = new List<DailyRepairBlock>();
                for (int i = 0; i < p.Value.Length; i += DailyUpdateRepairManifest.BlockBytes)
                {
                    byte[] block = p.Value.Skip(i).Take(DailyUpdateRepairManifest.BlockBytes).ToArray(); string hash = DailyUpdateRepairManifest.Hash(block);
                    objects["/repair/objects/" + hash + ".bin"] = block; blocks.Add(new(hash, block.Length));
                }
                return new DailyRepairFile(p.Key, p.Value.Length, DailyUpdateRepairManifest.Hash(p.Value), blocks.ToArray());
            }).ToArray();
            return new(1, "Dustweave", release.Version, "Lite", 2, release.RuntimeVersion!, release.Assets[0].Sha256, files);
        }
        var oldManifest = Manifest(before, oldData); var nextManifest = Manifest(after, newData);
        foreach (var m in new[] { oldManifest, nextManifest }) objects["/repair/v" + m.Version + "/" + DailyUpdateRepairManifest.Name(m.Version, m.Flavor)] = JsonSerializer.SerializeToUtf8Bytes(DailyUpdateRepairManifest.Sign(m, "test", key));
        // Corrupt a file before the update. The bad delta base must fall through
        // automatically to target blocks, preserving all unrelated data.
        File.WriteAllBytes(Path.Combine(installed, "app/Dustweave.Core.dll"), [99]);
        using var handler = new InventoryServer(objects); using var client = new HttpClient(handler);
        var plan = await DailyUpdateInventory.PrepareAsync(trust, client, verified, after, before.Version, "Lite", installed, Path.Combine(folder, "download"), null, default);
        var names = DailyUpdateRepair.VerifyStaging(nextManifest, installed, DailyUpdateRepair.Staging(plan.Directory), out var keep);
        var final = DailyUpdateTransaction.Prepare(DailyUpdateRepair.Staging(plan.Directory), installed, Path.Combine(folder, "final-backup"), names, keep,
            installRecoveryEntryFirst: true, previousFiles: oldManifest.Files.Select(f => f.Name).ToArray());
        DailyUpdateTransaction.Apply(final);
        Check("damaged installation upgrades without an extra approval", newData.All(p => File.ReadAllBytes(Path.Combine(installed, p.Key)).SequenceEqual(p.Value)));
        Check("only authenticated obsolete files are deleted", !File.Exists(Path.Combine(installed, "app/old.dll")) && File.ReadAllText(Path.Combine(installed, "app/foreign.dll")) == "keep" && File.ReadAllText(Path.Combine(installed, "accounts.json")) == "keep");
        Check("unchanged runtime-sized blocks never redownload", nextManifest.Files.Single(f => f.Name == "app/stable.dll").Blocks.All(b => !handler.Requests.Contains("/repair/objects/" + b.Sha256 + ".bin")));
        Check("complete archives are never required for repair fallback", handler.Requests.All(p => !p.EndsWith("-win-x64.zip")));
    }
    private sealed class InventoryServer(Dictionary<string, byte[]> objects) : HttpMessageHandler
    {
        internal List<string> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string path = request.RequestUri!.AbsolutePath; Requests.Add(path);
            return Task.FromResult(objects.TryGetValue(path, out var bytes) ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) } : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
