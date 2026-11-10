using System.Security.Cryptography;

namespace Dustweave.Ota;

static class CurrentChannelSelfTest
{
    internal static void Run(string folder, Dictionary<string, string> keys, ECDsa signer)
    {
        void Need(bool value) => Rules.Need(value, "Current-channel regression");
        void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Unsafe current-channel operation accepted"); }
        var root = new Config(Path.Combine(folder, "current-policy", "state"), Path.Combine(folder, "current-policy", "public"), keys, 3);
        Directory.CreateDirectory(root.Public); var old = Rules.Capability(root); var current = Rules.CurrentChannel(root);
        File.WriteAllText(Rules.FeedPath(old, true), "initial compare-and-swap fixture");
        Need(Rules.FeedPath(current) == Rules.FeedPath(old, true));
        Need(Rules.FeedPath(current, true).EndsWith("releases-v3.json") && current.State != old.State);
        Reject(() => Rules.CurrentChannel(old)); Reject(() => Rules.Capability(current));
        foreach (string command in new[] { "dustweave-ota current status", "dustweave-ota current submit", "dustweave-ota current status " + new string('a', 32) }) Need(Channel.ParseCommand(command) == command);
        foreach (string command in new[] { "dustweave-ota current capable status", "dustweave-ota capable current submit", "dustweave-ota current current status", "dustweave-ota current shell" }) Reject(() => Channel.ParseCommand(command));
        Need(Mirror.ObjectVersion(current, "1.6.10") == "v1.6.10-retirement" && Mirror.ObjectVersion(current, "1.7.0") == "v1.7.0");
        var notes = new Dictionary<string, string[]> { ["zh-CN"] = ["test"], ["zh-TW"] = ["test"], ["en-US"] = ["test"] };
        Release Release(string version, string? previous) => new(version, previous, 3, notes,
            new[] { "Lite", "Portable" }.Select(f => new Asset(f, $"Dustweave-{version}-{f}-win-x64.zip", 100, new string('a', 64))).ToArray(), [], "8.0.33");
        var baseline = Release("1.6.9", null); var next = Release("1.7.0", baseline.Version);
        byte[] Sign(Feed feed)
        {
            byte[] bytes = Rules.Bytes(feed);
            return Rules.Bytes(new Envelope(1, "test", "ECDSA-P256-SHA256", Convert.ToBase64String(bytes), Convert.ToBase64String(signer.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))));
        }
        byte[] signed = Sign(new(3, "Dustweave", "stable", [next, baseline]));
        File.WriteAllBytes(Rules.FeedPath(current, true), signed);
        Need(Rules.VerifyFeed(signed, current).Releases[0].Version == "1.7.0");
        Need(File.ReadAllText(Rules.FeedPath(old, true)) == "initial compare-and-swap fixture");
        Need(Rules.FeedPath(current) == Rules.FeedPath(current, true));
        File.Delete(Rules.FeedPath(current, true));
        Reject(() => Channel.CheckPreparedInventory(old, new(3, "Dustweave", "stable", [Release("1.6.10", "1.6.9"), baseline])));
        File.WriteAllBytes(Rules.FeedPath(current, true), signed);
        Channel.CheckPreparedInventory(old, new(3, "Dustweave", "stable", [Release("1.6.10", "1.6.9"), baseline]));
    }
}
