using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dustweave;

public sealed record DailyDeltaFile(string Name, long Bytes, string Sha256, string Mode,
    long BaseBytes = 0, string? BaseSha256 = null, string? Payload = null);
public sealed record DailyDeltaManifest(int Schema, string FromVersion, string Version, string Flavor,
    string TargetArchiveSha256, DailyDeltaFile[] Files);

// Content-defined chunks resynchronize after insertions; SHA-256 identifies reusable
// chunks. Neither weak boundary hashes nor installed version labels authorize content.
public static class DailyUpdateDeltaPackage
{
    public const string Algorithm = "dustweave-cdc-v1";
    private const int MinChunk = 2048, MaxChunk = 32768;
    private const long MaxExpanded = 2L * 1024 * 1024 * 1024;
    private static readonly ulong[] Gear = Enumerable.Range(0, 256).Select(i =>
        BitConverter.ToUInt64(SHA256.HashData(BitConverter.GetBytes(i)), 0)).ToArray();

    public static string Name(string version, string flavor, string from) =>
        $"Dustweave-{version}-{flavor}-from-{from}-win-x64.delta.zip";
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static bool Digest(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string PathOf(string root, string name)
    {
        if (!DailyUpdates.AllowedFile(name)) throw new InvalidDataException("updates.invalid_package");
        var path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
        DailyUpdates.EnsureNoLinks(path); return path;
    }
    private static byte[] ReadEntry(ZipArchiveEntry entry, long limit = DailyUpdates.MaxArchive)
    {
        if (entry.Length > limit) throw new InvalidDataException("updates.invalid_package");
        using var input = entry.Open(); using var output = new MemoryStream();
        var buffer = new byte[65536]; int count;
        while ((count = input.Read(buffer)) > 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("updates.invalid_package");
            output.Write(buffer, 0, count);
        }
        if (output.Length != entry.Length) throw new InvalidDataException("updates.invalid_package");
        return output.ToArray();
    }
    private static IEnumerable<(int Start, int Length)> Chunks(byte[] data)
    {
        int start = 0; ulong gear = 0;
        for (int i = 0; i < data.Length; i++)
        {
            gear = unchecked((gear << 1) + Gear[data[i]]);
            int size = i - start + 1;
            if (size >= MaxChunk || size >= MinChunk && (gear & 8191) == 0)
            { yield return (start, size); start = i + 1; gear = 0; }
        }
        if (start < data.Length) yield return (start, data.Length - start);
    }
    private static void WritePatch(Stream output, byte[] previous, byte[] current)
    {
        var index = new Dictionary<string, (int Start, int Length)>(StringComparer.Ordinal);
        foreach (var chunk in Chunks(previous))
            index.TryAdd(Convert.ToHexString(SHA256.HashData(previous.AsSpan(chunk.Start, chunk.Length))), chunk);
        using var compressed = new BrotliStream(output, CompressionLevel.Optimal, true);
        using var writer = new BinaryWriter(compressed, Encoding.UTF8, true);
        writer.Write(0x31574444); // DDW1
        foreach (var chunk in Chunks(current))
        {
            var data = current.AsSpan(chunk.Start, chunk.Length);
            if (index.TryGetValue(Convert.ToHexString(SHA256.HashData(data)), out var old) && data.SequenceEqual(previous.AsSpan(old.Start, old.Length)))
            { writer.Write((byte)1); writer.Write((long)old.Start); writer.Write(old.Length); }
            else { writer.Write((byte)0); writer.Write(chunk.Length); writer.Write(data); }
        }
        writer.Write((byte)255);
    }
    private static void ApplyPatch(Stream patch, Stream previous, Stream output, long size, CancellationToken token)
    {
        using var decoded = new BrotliStream(patch, CompressionMode.Decompress, true);
        using var reader = new BinaryReader(decoded, Encoding.UTF8, true);
        if (reader.ReadInt32() != 0x31574444) throw new InvalidDataException("updates.invalid_package");
        byte[] buffer = new byte[MaxChunk]; long written = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            byte kind = reader.ReadByte();
            if (kind == 255) break;
            if (kind > 1) throw new InvalidDataException("updates.invalid_package");
            long offset = kind == 1 ? reader.ReadInt64() : 0;
            int count = reader.ReadInt32();
            if (count <= 0 || count > MaxChunk || count > size - written ||
                kind == 1 && (offset < 0 || offset > previous.Length - count))
                throw new InvalidDataException("updates.invalid_package");
            if (kind == 1) { previous.Position = offset; previous.ReadExactly(buffer.AsSpan(0, count)); }
            else decoded.ReadExactly(buffer.AsSpan(0, count));
            output.Write(buffer, 0, count); written += count;
        }
        if (written != size || decoded.ReadByte() != -1) throw new InvalidDataException("updates.invalid_package");
    }
    public static bool VerifyFile(string path, long bytes, string hash) =>
        File.Exists(path) && new FileInfo(path).Length == bytes && Same(DailyUpdateTransaction.Hash(path), hash);

    // Publishing entry. The caller signs the returned descriptor only after verifying
    // the previous archive against its signed feed. Every rebuilt target is checked here.
    public static DailyUpdateDelta Create(string previousZip, string targetZip, string outputDirectory)
    {
        using var oldZip = ZipFile.OpenRead(previousZip); using var nextZip = ZipFile.OpenRead(targetZip);
        DailyUpdatePackage Metadata(ZipArchive zip) => JsonSerializer.Deserialize<DailyUpdatePackage>(
            ReadEntry(zip.GetEntry("update-package.json") ?? throw new InvalidDataException("Missing descriptor"), 1024 * 1024), DailyJson.Options)!;
        var before = Metadata(oldZip); var after = Metadata(nextZip);
        if (before.Flavor != after.Flavor || DailyUpdates.VersionOf(before.Version) >= DailyUpdates.VersionOf(after.Version))
            throw new InvalidDataException("updates.invalid_package");
        var entries = nextZip.Entries.Where(e => !e.FullName.EndsWith('/')).ToArray();
        if (entries.Length > 5000 || entries.Sum(e => e.Length) > MaxExpanded ||
            entries.Any(e => !DailyUpdates.AllowedFile(e.FullName)) ||
            entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length ||
            after.Files == null || !after.Files.Order().SequenceEqual(entries.Select(e => e.FullName).Order()))
            throw new InvalidDataException("updates.invalid_package");
        Directory.CreateDirectory(outputDirectory);
        string name = Name(after.Version, after.Flavor, before.Version), result = Path.Combine(outputDirectory, name);
        using (var file = new FileStream(result, FileMode.CreateNew, FileAccess.Write))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            var files = new List<DailyDeltaFile>();
            foreach (var entry in entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                byte[] next = ReadEntry(entry);
                var oldEntry = oldZip.GetEntry(entry.FullName);
                byte[] old = oldEntry == null ? [] : ReadEntry(oldEntry);
                string hash = Hash(next), baseHash = Hash(old);
                if (oldEntry != null && Same(hash, baseHash))
                { files.Add(new(entry.FullName, next.Length, hash, "copy", old.Length, baseHash)); continue; }
                string payload = $"payload/{files.Count}.br";
                using var patch = new MemoryStream(); WritePatch(patch, old, next);
                // Reconstruct and compare byte-for-byte before publishing, even for empty/new files.
                patch.Position = 0; using var check = new MemoryStream(); using var baseStream = new MemoryStream(old);
                ApplyPatch(patch, baseStream, check, next.Length, default);
                if (!check.ToArray().AsSpan().SequenceEqual(next)) throw new InvalidDataException("Delta generation failed");
                var member = zip.CreateEntry(payload, CompressionLevel.NoCompression);
                using (var stream = member.Open()) { patch.Position = 0; patch.CopyTo(stream); }
                files.Add(new(entry.FullName, next.Length, hash, "patch", old.Length, oldEntry == null ? null : baseHash, payload));
            }
            var manifest = new DailyDeltaManifest(1, before.Version, after.Version, after.Flavor, DailyUpdateTransaction.Hash(targetZip), files.ToArray());
            using var metadata = zip.CreateEntry("delta.json", CompressionLevel.Optimal).Open();
            JsonSerializer.Serialize(metadata, manifest, DailyJson.Options);
        }
        return new(after.Flavor, before.Version, name, new FileInfo(result).Length, DailyUpdateTransaction.Hash(result), Algorithm);
    }
    public static DailyDeltaManifest Read(string path, DailyUpdateRelease release, DailyUpdateAsset asset, DailyUpdateDelta delta)
    {
        if (delta.Algorithm != Algorithm || delta.Flavor != asset.Flavor) throw new InvalidDataException("updates.invalid_package");
        if (!VerifyFile(path, delta.Bytes, delta.Sha256)) throw new InvalidDataException("updates.hash_failed");
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("delta.json") ?? throw new InvalidDataException("updates.invalid_package");
        var manifest = JsonSerializer.Deserialize<DailyDeltaManifest>(ReadEntry(entry, 4 * 1024 * 1024), DailyJson.Options)
            ?? throw new InvalidDataException("updates.invalid_package");
        if (manifest.Schema != 1 || manifest.Version != release.Version || manifest.FromVersion != delta.FromVersion ||
            manifest.Flavor != asset.Flavor || !Same(manifest.TargetArchiveSha256, asset.Sha256) ||
            manifest.Files.Length is < 2 or > 5000 || manifest.Files.Sum(f => f.Bytes) > MaxExpanded ||
            manifest.Files.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Length ||
            !manifest.Files.Any(f => f.Name == "Dustweave.exe") || !manifest.Files.Any(f => f.Name == "update-package.json"))
            throw new InvalidDataException("updates.invalid_package");
        foreach (var f in manifest.Files)
        {
            if (!DailyUpdates.AllowedFile(f.Name) || f.Bytes < 0 || f.Bytes > DailyUpdates.MaxArchive || !Digest(f.Sha256) ||
                f.BaseBytes < 0 || f.BaseBytes > DailyUpdates.MaxArchive || f.BaseSha256 != null && !Digest(f.BaseSha256) ||
                f.Mode is not ("copy" or "patch") || f.Mode == "copy" && (f.Payload != null || f.BaseBytes != f.Bytes || !Same(f.BaseSha256, f.Sha256)) ||
                f.Mode == "patch" && (f.Payload == null || !System.Text.RegularExpressions.Regex.IsMatch(f.Payload, @"^payload/[0-9]+\.br$")) ||
                f.BaseSha256 == null && f.BaseBytes != 0)
                throw new InvalidDataException("updates.invalid_package");
        }
        var expected = manifest.Files.Where(f => f.Payload != null).Select(f => f.Payload!).Append("delta.json").Order().ToArray();
        if (!zip.Entries.Select(e => e.FullName).Order().SequenceEqual(expected) ||
            zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count ||
            zip.Entries.Sum(e => e.Length) > DailyUpdates.MaxArchive ||
            zip.Entries.Any(e => ((e.ExternalAttributes >> 16) & 0xF000) == 0xA000))
            throw new InvalidDataException("updates.invalid_package");
        return manifest;
    }
    public static bool CanApply(string installed, DailyDeltaManifest manifest, CancellationToken token = default)
    {
        DailyUpdates.EnsureNoLinks(installed);
        var package = DailyJson.TryRead<DailyUpdatePackage>(PathOf(installed, "update-package.json"));
        if (package?.Version != manifest.FromVersion || package.Flavor != manifest.Flavor) return false;
        foreach (var file in manifest.Files)
        {
            token.ThrowIfCancellationRequested();
            if (file.BaseSha256 != null && !VerifyFile(PathOf(installed, file.Name), file.BaseBytes, file.BaseSha256)) return false;
        }
        return true;
    }
    public static string[] Extract(string path, string installed, string staging, DailyUpdateRelease release,
        DailyUpdateAsset asset, DailyUpdateDelta delta, CancellationToken token = default)
    {
        var manifest = Read(path, release, asset, delta);
        if (!CanApply(installed, manifest, token)) throw new InvalidDataException("updates.delta_base_changed");
        DailyUpdates.EnsureNoLinks(staging);
        using var zip = ZipFile.OpenRead(path);
        foreach (var file in manifest.Files)
        {
            token.ThrowIfCancellationRequested();
            string target = PathOf(staging, file.Name), source = PathOf(installed, file.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (Stream previous = file.BaseSha256 == null ? new MemoryStream() : File.OpenRead(source))
            using (var output = new FileStream(target, FileMode.Create, FileAccess.Write))
            {
                if (file.Mode == "copy") previous.CopyTo(output);
                else
                {
                    using var patch = zip.GetEntry(file.Payload!)!.Open();
                    ApplyPatch(patch, previous, output, file.Bytes, token);
                }
                output.Flush(true);
            }
            if (!VerifyFile(target, file.Bytes, file.Sha256)) throw new InvalidDataException("updates.hash_failed");
        }
        var metadata = DailyJson.TryRead<DailyUpdatePackage>(PathOf(staging, "update-package.json"));
        string[] names = manifest.Files.Select(f => f.Name).ToArray();
        if (metadata?.Version != release.Version || metadata.Flavor != asset.Flavor || metadata.Files == null ||
            !metadata.Files.Order().SequenceEqual(names.Order())) throw new InvalidDataException("updates.invalid_package");
        return names;
    }
}
