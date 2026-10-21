using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dustweave { public sealed record DailyRepairProgress(string Stage, int Completed, int Total, string? File = null); }
namespace Dustweave.Bootstrap
{
internal static class RepairBootstrap
{
    internal static void VerifyStartup()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dustweave.bootstrap-info.json")
            ?? throw new InvalidDataException("Missing bootstrap identity.");
        using var info = JsonDocument.Parse(stream);
        // CLR initialization can terminate the process without returning to our
        // native caller (for example BadImageFormat in the entry assembly).
        // Validate pinned startup dependencies before any CLR code is loaded.
        foreach (var file in info.RootElement.GetProperty("StartupFiles").EnumerateArray())
        {
            string name = Text(file, "Name"), hash = Text(file, "Sha256");
            if (!name.StartsWith("app/", StringComparison.Ordinal) || name[4..].Contains('/') || name.Contains('\\') || name.Contains(".."))
                throw new InvalidDataException("Invalid startup file.");
            if (!Matches(Path.Combine(Program.InstallRoot, name), hash))
                throw new InvalidDataException("Startup file is missing or damaged: " + name);
        }
    }
    internal static bool IsPortable
    {
        get
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dustweave.bootstrap-info.json")
                ?? throw new InvalidDataException("Missing bootstrap identity.");
            using var info = JsonDocument.Parse(stream);
            return Text(info.RootElement, "Flavor") switch { "Portable" => true, "Lite" => false, _ => throw new InvalidDataException("Invalid bootstrap edition.") };
        }
    }
    // Pinned after building the independent helper. No mutable local inventory
    // or managed application assembly authorizes executing a replacement helper.
    internal static (string Path, string Version, string Flavor) VerifiedHelper()
    {
        using var infoStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dustweave.bootstrap-info.json")
            ?? throw new InvalidDataException("Missing bootstrap identity.");
        using var info = JsonDocument.Parse(infoStream);
        string version = Text(info.RootElement, "Version"), flavor = Text(info.RootElement, "Flavor"), digest = Text(info.RootElement, "HelperSha256");
        if (flavor is not ("Lite" or "Portable") || digest.Length != 64 || !digest.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid bootstrap identity.");
        string local = Path.Combine(Program.InstallRoot, "app", "support", "Dustweave.Updater.exe");
        if (Matches(local, digest)) return (local, version, flavor);
        string helper = RepairDialog.Run(Program.Text("正在恢复修复组件", "正在恢復修復元件", "Restoring the repair component"),
            (progress, token) => Download(version, flavor, digest, progress, token));
        return (helper, version, flavor);
    }
    private static async Task<string> Download(string version, string flavor, string digest, IProgress<DailyRepairProgress> progress, CancellationToken token)
    {
        using var trustStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dustweave.update-trust.json")!;
        using var trust = JsonDocument.Parse(trustStream);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        string storagePath = Path.Combine(Program.DataRoot, "cache-storage.lock");
        Recovery.NoLinks(storagePath); Directory.CreateDirectory(Program.DataRoot);
        using var storage = new FileStream(storagePath, FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read);
        string directory = Path.Combine(Program.DataRoot, "updates", version + "-" + flavor);
        Recovery.NoLinks(directory); Directory.CreateDirectory(directory);
        string lockPath = Path.Combine(directory, "download.lock"); Recovery.NoLinks(lockPath);
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string folder = Path.Combine(directory, "bootstrap-helper");
        Recovery.NoLinks(folder); Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, "Dustweave.Updater.exe");
        if (Matches(target, digest)) return target;
        Exception? last = null;
        foreach (var source in trust.RootElement.GetProperty("Sources").EnumerateArray())
        {
          bool fallback = source.TryGetProperty("OriginFallback", out var fallbackValue) && fallbackValue.ValueKind == JsonValueKind.True;
          for (int attempt = 0; attempt < (fallback ? 2 : 1); attempt++)
          {
            token.ThrowIfCancellationRequested();
            try
            {
                string baseUrl = Text(source, "PackageBaseUrl").TrimEnd('/');
                string origin = attempt == 0 ? "" : "?origin=1";
                byte[] envelope = await Get(client, baseUrl + "/repair/v" + version + "/Dustweave-" + version + "-" + flavor + "-repair.json" + origin, 4 * 1024 * 1024, token);
                using var manifest = Verify(envelope, trust.RootElement.GetProperty("Keys"));
                var root = manifest.RootElement;
                if (Text(root, "Product") != "Dustweave" || Text(root, "Version") != version || Text(root, "Flavor") != flavor ||
                    root.GetProperty("Schema").GetInt32() != 1 || root.GetProperty("Layout").GetInt32() != 2)
                    throw new InvalidDataException("Repair identity mismatch.");
                var files = root.GetProperty("Files").EnumerateArray().Where(f => Text(f, "Name") == "app/support/Dustweave.Updater.exe").ToArray();
                if (files.Length != 1 || !Text(files[0], "Sha256").Equals(digest, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Repair helper identity mismatch.");
                var file = files[0]; long bytes = file.GetProperty("Bytes").GetInt64();
                var blocks = file.GetProperty("Blocks").EnumerateArray().ToArray();
                if (bytes <= 0 || bytes > 128 * 1024 * 1024 || blocks.Length != (bytes + 2097151) / 2097152) throw new InvalidDataException("Invalid helper length.");
                string temp = target + "." + Guid.NewGuid().ToString("N") + ".partial";
                try
                {
                    using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        for (int i = 0; i < blocks.Length; i++)
                        {
                            string hash = Text(blocks[i], "Sha256"); int size = blocks[i].GetProperty("Bytes").GetInt32();
                            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit) || size != Math.Min(2097152, bytes - (long)i * 2097152)) throw new InvalidDataException("Invalid helper block.");
                            string objectPath = Path.Combine(directory, "repair-objects", hash.ToLowerInvariant() + ".bin");
                            Recovery.NoLinks(objectPath);
                            byte[] data = File.Exists(objectPath) && new FileInfo(objectPath).Length == size && Matches(objectPath, hash)
                                ? await File.ReadAllBytesAsync(objectPath, token)
                                : await Get(client, baseUrl + "/repair/objects/" + hash.ToLowerInvariant() + ".bin" + origin, size, token);
                            if (data.Length != size || !Convert.ToHexString(SHA256.HashData(data)).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Repair block verification failed.");
                            Directory.CreateDirectory(Path.GetDirectoryName(objectPath)!);
                            if (!Matches(objectPath, hash)) await File.WriteAllBytesAsync(objectPath, data, token);
                            await output.WriteAsync(data, token); progress.Report(new("preparing", i + 1, blocks.Length));
                        }
                        output.Flush(true);
                    }
                    if (!Matches(temp, digest)) throw new InvalidDataException("Repair helper verification failed.");
                    Recovery.NoLinks(target); File.Move(temp, target, true); return target;
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or JsonException or CryptographicException)
            { token.ThrowIfCancellationRequested(); last = error; }
          }
        }
        throw new IOException("Repair component unavailable.", last);
    }
    private static JsonDocument Verify(byte[] bytes, JsonElement keys)
    {
        using var envelope = JsonDocument.Parse(bytes); var e = envelope.RootElement;
        if (e.GetProperty("Schema").GetInt32() != 1 || Text(e, "Algorithm") != "ECDSA-P256-SHA256" ||
            !keys.TryGetProperty(Text(e, "KeyId"), out var encodedKey)) throw new InvalidDataException("Untrusted repair manifest.");
        byte[] payload = Convert.FromBase64String(Text(e, "Payload")), signature = Convert.FromBase64String(Text(e, "Signature"));
        using var key = ECDsa.Create(); key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(encodedKey.GetString()!), out _);
        if (key.KeySize != 256 || signature.Length != 64 || !key.VerifyData(Encoding.UTF8.GetBytes("Dustweave-Repair-v1\n").Concat(payload).ToArray(), signature,
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) throw new InvalidDataException("Repair signature verification failed.");
        return JsonDocument.Parse(payload);
    }
    private static bool Matches(string path, string digest)
    {
        Recovery.NoLinks(path);
        return File.Exists(path) && Recovery.Hash(path).Equals(digest, StringComparison.OrdinalIgnoreCase);
    }
    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new InvalidDataException("Missing repair field.");
    private static async Task<byte[]> Get(HttpClient client, string address, int limit, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(90)); token = timeout.Token;
        var uri = new Uri(address);
        if (uri.Scheme != "https" || uri.UserInfo != "" || uri.Fragment != "") throw new InvalidDataException("Invalid repair URL.");
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Oversized repair response.");
        using var input = await response.Content.ReadAsStreamAsync(token); using var output = new MemoryStream();
        byte[] buffer = new byte[65536]; int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        { if (output.Length + count > limit) throw new InvalidDataException("Oversized repair response."); await output.WriteAsync(buffer.AsMemory(0, count), token); }
        return output.ToArray();
    }
}
}
