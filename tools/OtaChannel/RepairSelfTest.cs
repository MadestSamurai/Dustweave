using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Dustweave;

namespace Dustweave.Ota;

static class RepairSelfTest
{
    public static async Task Run(string folder, List<string> checks)
    {
        void Pass(string name, bool value = true) { Rules.Need(value, name); checks.Add(name); }
        async Task Reject(string name, Func<Task> action) { try { await action(); } catch (Exception error) when (error is InvalidDataException or IOException) { checks.Add(name); return; } throw new Exception("Accepted: " + name); }
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keys = new Dictionary<string, string> { ["test"] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) };
        Envelope Sign<T>(T value, string domain)
        {
            byte[] payload = Rules.Bytes(value);
            return new(1, "test", "ECDSA-P256-SHA256", Convert.ToBase64String(payload), Convert.ToBase64String(key.SignData(
                Encoding.UTF8.GetBytes(domain).Concat(payload).ToArray(), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
        }
        var notes = new Dictionary<string, string[]> { ["zh-CN"] = ["修复"], ["zh-TW"] = ["修復"], ["en-US"] = ["Repair"] };
        var release = new Release("1.4.13", null, 2, notes, new[] { "Lite", "Portable" }.Select(f => new Asset(f, $"Dustweave-1.4.13-{f}-win-x64.zip", 4, Rules.Hash([1, 2, 3, 4]))).ToArray(), [], "8.0.30");
        byte[] feed = Rules.Bytes(Sign(new Feed(3, "Dustweave", "stable", [release]), ""));
        byte[] block = [4, 2, 8], hashBytes = SHA256.HashData(block);
        string hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        var files = new[] { "Dustweave.exe", "update-package.json", "app/support/Dustweave.Updater.exe" }
            .Select(n => new DailyRepairFile(n, block.Length, hash, [new(hash, block.Length)])).ToArray();
        byte[][] manifests = release.Assets.Select(a => Rules.Bytes(DailyUpdateRepairManifest.Sign(
            new(1, "Dustweave", release.Version, a.Flavor, 2, "8.0.30", a.Sha256, files), "test", key))).ToArray();
        Config Setup(string name)
        {
            string path = Path.Combine(folder, "repair-" + name);
            var c = new Config(Path.Combine(path, "state"), Path.Combine(path, "public"), keys, 2);
            Directory.CreateDirectory(c.State); Directory.CreateDirectory(c.Public); File.WriteAllBytes(Path.Combine(c.Public, "updates.json"), feed);
            return c;
        }
        RepairRequest Request() => new(1, "Dustweave", release.Version, Rules.Hash(feed), Rules.Hash(feed), Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), manifests.Select(Rules.Hash).ToArray(), [new(hash + ".bin", block.Length, hash)]);
        RepairHeader Header(RepairRequest request) => new(Sign(request, RepairChannel.Domain), Convert.ToBase64String(feed), manifests.Select(Convert.ToBase64String).ToArray());
        MemoryStream Frame(RepairHeader header, byte[]? body = null)
        {
            byte[] bytes = Rules.Bytes(header), length = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            var stream = new MemoryStream(); stream.Write(length); stream.Write(bytes); stream.Write(body ?? block); stream.Position = 0; return stream;
        }
        var config = Setup("normal"); var request = Request(); var header = Header(request);
        Pass("repair restricted command recognized", Channel.ParseCommand("dustweave-ota repair") == "dustweave-ota repair");
        Pass("repair index is a separate read-only command", Channel.ParseCommand("dustweave-ota repair-index") == "dustweave-ota repair-index");
        Pass("repair publication validates both editions", RepairChannel.Validate(header, config, DateTimeOffset.UtcNow).Manifests.Length == 2);
        await Reject("repair rejects ordinary publish signature domain", () => { RepairChannel.Validate(header with { Request = Sign(request, Rules.Domain) }, config, DateTimeOffset.UtcNow); return Task.CompletedTask; });
        await Reject("repair rejects stale authorization", () => { RepairChannel.Validate(Header(request with { IssuedUtc = DateTimeOffset.UtcNow.AddHours(-1) }), config, DateTimeOffset.UtcNow); return Task.CompletedTask; });
        await Reject("repair refuses unknown payload files", () => { RepairChannel.Validate(Header(request with { Files = [new("../arbitrary", 3, hash)] }), config, DateTimeOffset.UtcNow); return Task.CompletedTask; });
        await Reject("repair cannot alter original OTA lane", () => RepairChannel.Submit(config with { Layout = 1 }, Frame(header)));
        await Reject("repair refuses changed current release", () => RepairChannel.Submit(config, Frame(Header(Request() with { ExpectedFeedSha256 = new('b', 64) }))));
        var incomplete = Setup("incomplete");
        await Reject("repair rejects interrupted body", () => RepairChannel.Submit(incomplete, Frame(Header(Request()), [1])));
        Pass("interrupted repair publication exposes no manifest", !Directory.Exists(Path.Combine(incomplete.Public, "repair", "v1.4.13")));
        await Reject("repair refuses corrupt bytes", () => RepairChannel.Submit(config, Frame(Header(Request()), [9, 9, 9])));
        await RepairChannel.Submit(config, Frame(header));
        Pass("repair publishes both signed manifests", Directory.GetFiles(Path.Combine(config.Public, "repair", "v1.4.13")).Length == 2);
        Pass("repair publication keeps original signed feed identical", Rules.HashFile(Path.Combine(config.Public, "updates.json")) == Rules.Hash(feed));
        Pass("repair index lists deduplicated content", Rules.Bytes(RepairChannel.Index(config)).Length > 20);
        await Reject("repair nonce cannot be replayed", () => RepairChannel.Submit(config, Frame(header)));
        var reuse = Request() with { Files = [] };
        await RepairChannel.Submit(config, Frame(Header(reuse), []));
        Pass("later repair publication reuses verified origin blocks");
        Pass("repair status returns durable receipt", System.Text.Json.JsonSerializer.Serialize(Channel.Status(config, reuse.Nonce)).Contains("completed"));
        File.WriteAllBytes(Path.Combine(config.Public, "repair", "objects", hash + ".bin"), [9, 9, 9]);
        await Reject("omitted existing block still reverified", () => RepairChannel.Submit(config, Frame(Header(Request() with { Files = [] }), [])));
        await RepairChannel.Submit(config, Frame(Header(Request())));
        Pass("signed resend repairs a corrupt origin object", Rules.HashFile(Path.Combine(config.Public, "repair", "objects", hash + ".bin")) == hash);
        var stopped = Request();
        string pendingHeader = Path.Combine(config.State, "repairs", stopped.Nonce, "header.json");
        Directory.CreateDirectory(Path.GetDirectoryName(pendingHeader)!);
        Rules.Atomic(pendingHeader, Rules.Bytes(Header(stopped)));
        Pass("active repair submission reports receiving", System.Text.Json.JsonSerializer.Serialize(Channel.Status(config, stopped.Nonce)).Contains("receiving"));
        File.SetLastWriteTimeUtc(pendingHeader, DateTime.UtcNow.AddMinutes(-25));
        Pass("lost repair process does not report receiving forever", System.Text.Json.JsonSerializer.Serialize(Channel.Status(config, stopped.Nonce)).Contains("needs_attention"));
    }
}
