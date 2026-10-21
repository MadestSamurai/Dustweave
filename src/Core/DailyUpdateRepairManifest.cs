using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dustweave;

public sealed record DailyRepairBlock(string Sha256, int Bytes);
public sealed record DailyRepairFile(string Name, long Bytes, string Sha256, DailyRepairBlock[] Blocks);
public sealed record DailyRepairManifest(int Schema, string Product, string Version, string Flavor, int Layout,
    string RuntimeVersion, string ArchiveSha256, DailyRepairFile[] Files);
public sealed record DailyRepairEnvelope(int Schema, string KeyId, string Algorithm, string Payload, string Signature);

// Shared by the desktop, recovery helper and constrained publisher. Repair
// authorization is domain-separated from both release feeds and publish requests.
public static class DailyUpdateRepairManifest
{
    public const int BlockBytes = 2 * 1024 * 1024;
    public const int MaxEnvelopeBytes = 4 * 1024 * 1024;
    public const string Domain = "Dustweave-Repair-v1\n";
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static string Name(string version, string flavor) => $"Dustweave-{version}-{flavor}-repair.json";
    public static string ObjectName(DailyRepairBlock block) => block.Sha256.ToLowerInvariant() + ".bin";
    public static DailyRepairEnvelope Sign(DailyRepairManifest manifest, string keyId, ECDsa key)
    {
        Validate(manifest);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
        return new(1, keyId, "ECDSA-P256-SHA256", Convert.ToBase64String(payload),
            Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(Domain).Concat(payload).ToArray(),
                HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
    }
    public static DailyRepairManifest Verify(byte[] bytes, IReadOnlyDictionary<string, string> keys)
    {
        try
        {
            if (bytes.Length > MaxEnvelopeBytes) throw new InvalidDataException("updates.repair_signature");
            var envelope = JsonSerializer.Deserialize<DailyRepairEnvelope>(bytes, Json)!;
            if (envelope.Schema != 1 || envelope.Algorithm != "ECDSA-P256-SHA256" ||
                !keys.TryGetValue(envelope.KeyId, out string? publicKey))
                throw new InvalidDataException("updates.repair_signature");
            byte[] payload = Convert.FromBase64String(envelope.Payload), signature = Convert.FromBase64String(envelope.Signature);
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            if (key.KeySize != 256 || signature.Length != 64 ||
                !key.VerifyData(Encoding.UTF8.GetBytes(Domain).Concat(payload).ToArray(), signature,
                    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new InvalidDataException("updates.repair_signature");
            var manifest = JsonSerializer.Deserialize<DailyRepairManifest>(payload, Json)!;
            Validate(manifest);
            return manifest;
        }
        catch (Exception error) when (error is JsonException or FormatException or NullReferenceException or ArgumentException or CryptographicException or OverflowException)
        { throw new InvalidDataException("updates.repair_signature", error); }
    }
    public static void Validate(DailyRepairManifest manifest)
    {
        if (manifest.Schema != 1 || manifest.Product != "Dustweave" || manifest.Layout != 2 ||
            !System.Text.RegularExpressions.Regex.IsMatch(manifest.Version, @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$") ||
            manifest.Flavor is not ("Lite" or "Portable") || !Version.TryParse(manifest.RuntimeVersion, out _) ||
            !IsHash(manifest.ArchiveSha256) || manifest.Files.Length is < 3 or > 5000 ||
            manifest.Files.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Length ||
            manifest.Files.Sum(f => f.Bytes) > 2L * 1024 * 1024 * 1024)
            throw new InvalidDataException("updates.repair_manifest");
        var blockLengths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (!AllowedFile(file.Name) || file.Bytes < 0 || file.Bytes > 600L * 1024 * 1024 || !IsHash(file.Sha256) ||
                file.Blocks.Length != (file.Bytes + BlockBytes - 1) / BlockBytes ||
                file.Blocks.Sum(b => (long)b.Bytes) != file.Bytes)
                throw new InvalidDataException("updates.repair_manifest");
            for (int i = 0; i < file.Blocks.Length; i++)
            {
                var block = file.Blocks[i];
                if (!IsHash(block.Sha256) || block.Bytes != Math.Min(BlockBytes, file.Bytes - (long)i * BlockBytes) ||
                    blockLengths.TryGetValue(block.Sha256, out int length) && length != block.Bytes)
                    throw new InvalidDataException("updates.repair_manifest");
                blockLengths[block.Sha256] = block.Bytes;
            }
        }
        foreach (string required in new[] { "Dustweave.exe", "update-package.json", "app/support/Dustweave.Updater.exe" })
            if (!manifest.Files.Any(f => f.Name == required)) throw new InvalidDataException("updates.repair_manifest");
    }
    // The inventory is limited to distributed application files. Accounts,
    // preferences, plugins and unknown user files are never repair targets.
    public static bool AllowedFile(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.Contains(':') || name.StartsWith('/') ||
            name.Any(c => c < 32 || "<>\"|?*".Contains(c)) ||
            name.Split('/').Any(s => s is "" or "." or ".." || s.EndsWith('.') || s.EndsWith(' ') ||
                System.Text.RegularExpressions.Regex.IsMatch(s, @"^(?i:CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)"))) return false;
        if (name.StartsWith("app/", StringComparison.Ordinal))
        {
            var inner = name[4..];
            return inner is not ("Dustweave.exe" or "update-package.json") &&
                !inner.StartsWith("app/", StringComparison.Ordinal) && AllowedFile(inner);
        }
        return name is "Dustweave.exe" or "utility-host.json" or "tools.json" or "update-package.json" or "README.md" or "README.en.md" or "THIRD_PARTY_NOTICES.md" or "LICENSE"
            or "package-components.json" or "support/Dustweave.Updater.exe"
            || !name.Contains('/') && (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                (name.StartsWith("Dustweave", StringComparison.Ordinal) || name.StartsWith("BD2", StringComparison.Ordinal)) &&
                (name.EndsWith(".deps.json", StringComparison.Ordinal) || name.EndsWith(".runtimeconfig.json", StringComparison.Ordinal)))
            || System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-z]{2}(?:-[A-Za-z]+)?/[A-Za-z0-9._-]+\.resources\.dll$")
            || System.Text.RegularExpressions.Regex.IsMatch(name, @"^resources/shared/[A-Fa-f0-9]{64}\.bin$")
            || name.StartsWith("data/", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)
            || name.StartsWith("flows/", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)
            || name.StartsWith("connection/specs/", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)
            || name.StartsWith("licenses/", StringComparison.Ordinal)
            || name.StartsWith("docs/", StringComparison.Ordinal) && name.EndsWith(".md", StringComparison.Ordinal);
    }
}
