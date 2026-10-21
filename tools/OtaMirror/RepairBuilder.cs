using System.IO.Compression;
using System.Security.Cryptography;
using Dustweave;
using Dustweave.Ota;

static class RepairBuilder
{
    public static void Build(string packages, string output, string trustPath)
    {
        var trust = Rules.Read<Trust>(File.ReadAllBytes(trustPath));
        var feedBytes = File.ReadAllBytes(Path.Combine(packages, "updates.json"));
        var feed = Rules.VerifyFeed(feedBytes, new("", "", trust.Keys, 2));
        var release = feed.Releases[0];
        Directory.CreateDirectory(output);
        string objects = Path.Combine(output, "repair", "objects"), metadata = Path.Combine(output, "repair", "v" + release.Version);
        Rules.NoLinks(objects); Rules.NoLinks(metadata); Directory.CreateDirectory(objects); Directory.CreateDirectory(metadata);
        foreach (var asset in release.Assets)
        {
            string zipPath = Path.Combine(packages, asset.FileName);
            Rules.CheckArchive(zipPath, new(asset.FileName, asset.Bytes, asset.Sha256.ToLowerInvariant()));
            using var zip = ZipFile.OpenRead(zipPath);
            var entries = zip.Entries.Where(e => !e.FullName.EndsWith('/')).ToArray();
            Rules.Need(entries.Length is > 0 and <= 5000 && entries.Sum(e => e.Length) <= 2L * 1024 * 1024 * 1024 &&
                entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == entries.Length &&
                entries.All(e => DailyUpdateRepairManifest.AllowedFile(e.FullName) && ((e.ExternalAttributes >> 16) & 0xF000) != 0xA000),
                "Unsafe repair source archive");
            var files = new List<DailyRepairFile>();
            foreach (var entry in entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                using var input = entry.Open();
                using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var blocks = new List<DailyRepairBlock>();
                long remaining = entry.Length;
                while (remaining > 0)
                {
                    int count = (int)Math.Min(remaining, DailyUpdateRepairManifest.BlockBytes);
                    byte[] data = new byte[count]; input.ReadExactly(data); digest.AppendData(data);
                    string hash = Rules.Hash(data), path = Path.Combine(objects, hash + ".bin");
                    if (File.Exists(path)) Rules.Need(new FileInfo(path).Length == count && Rules.HashFile(path) == hash, "Existing repair object differs");
                    else Rules.Atomic(path, data);
                    blocks.Add(new(hash, count)); remaining -= count;
                }
                Rules.Need(input.ReadByte() == -1, "Archive entry exceeds declared length");
                files.Add(new(entry.FullName, entry.Length, Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant(), blocks.ToArray()));
            }
            var manifest = new DailyRepairManifest(1, "Dustweave", release.Version, asset.Flavor, release.Layout,
                release.RuntimeVersion!, asset.Sha256.ToLowerInvariant(), files.ToArray());
            DailyUpdateRepairManifest.Validate(manifest);
            Rules.Atomic(Path.Combine(metadata, DailyUpdateRepairManifest.Name(release.Version, asset.Flavor) + ".unsigned"), Rules.Bytes(manifest));
            Console.WriteLine($"Prepared {asset.Flavor}: {files.Count} signed-inventory candidates");
        }
        Rules.Atomic(Path.Combine(output, "updates.json"), feedBytes);
        Console.WriteLine($"Repair objects: {Directory.GetFiles(objects).Length}, {Directory.GetFiles(objects).Sum(p => new FileInfo(p).Length)} bytes");
    }
    public static Blob[] Inventory(string directory, Config config)
    {
        var feed = Rules.VerifyFeed(File.ReadAllBytes(Path.Combine(directory, "updates.json")), config);
        var release = feed.Releases[0];
        var blocks = new Dictionary<string, Blob>();
        foreach (var asset in release.Assets)
        {
            string path = Path.Combine(directory, "repair", "v" + release.Version, DailyUpdateRepairManifest.Name(release.Version, asset.Flavor));
            var manifest = DailyUpdateRepairManifest.Verify(File.ReadAllBytes(path), config.Keys);
            Rules.Need(manifest.Version == release.Version && manifest.Flavor == asset.Flavor &&
                manifest.Layout == release.Layout && manifest.RuntimeVersion == release.RuntimeVersion &&
                manifest.ArchiveSha256.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase), "Repair inventory differs from signed release");
            foreach (var block in manifest.Files.SelectMany(f => f.Blocks))
            {
                string relative = "repair/objects/" + DailyUpdateRepairManifest.ObjectName(block);
                blocks[relative] = new(relative, block.Bytes, block.Sha256.ToLowerInvariant());
            }
        }
        return blocks.Values.OrderBy(b => b.FileName, StringComparer.Ordinal).ToArray();
    }
}
