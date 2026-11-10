using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dustweave.Bootstrap;

namespace Dustweave.RuntimeNotice;

// The old application's verified helper performs replacement and rollback.
// The native notice only commits its own visible startup; it does not implement
// another installer, download program files, or deserialize executable commands.
internal static class NoticeTransaction
{
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidDataException(name);
    private static bool Same(string a, string b) => Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    private static void Need(bool condition, string why) { if (!condition) throw new InvalidDataException(why); }
    private static JsonDocument Read(string path)
    {
        Recovery.NoLinks(path);
        using var stream = File.OpenRead(path);
        Need(stream.Length <= 4 * 1024 * 1024, "Update record too large.");
        return JsonDocument.Parse(stream);
    }
    internal static void Acknowledge(string nonce, string dataRoot, string target)
    {
        Need(nonce.Length == 32 && nonce.All(Uri.IsHexDigit), "Invalid update nonce.");
        using var markerDocument = Read(Path.Combine(Path.GetDirectoryName(target)!, ".dustweave-update.json"));
        var marker = markerDocument.RootElement;
        Need(Text(marker, "Nonce") == nonce, "Update nonce changed.");
        using var jobDocument = Read(Text(marker, "JobPath")); var job = jobDocument.RootElement;
        var release = job.GetProperty("Release"); var asset = job.GetProperty("Asset");
        string flavor = Text(asset, "Flavor");
        Need(flavor is "Portable" or "Lite" && Text(release, "Version") == Program.Version, "Wrong notice target.");
        string directory = Path.Combine(dataRoot, "updates", Program.Version + "-" + flavor);
        string attempt = Path.Combine(directory, "attempts", nonce);
        Need(Text(job, "Nonce") == nonce && Same(Text(job, "Target"), target) && Same(Text(job, "Directory"), directory) &&
            Same(Text(marker, "JobPath"), Path.Combine(attempt, "job.json")), "Wrong update owner.");
        string helper = Path.Combine(attempt, "Dustweave.Updater.exe"); Recovery.NoLinks(helper);
        Need(Recovery.Hash(helper).Equals(Text(marker, "HelperSha256"), StringComparison.OrdinalIgnoreCase), "Update helper changed.");
        using var trustStream = typeof(NoticeTransaction).Assembly.GetManifestResourceStream("Dustweave.update-trust.json")!;
        using var trust = JsonDocument.Parse(trustStream);
        using var envelope = Read(Path.Combine(directory, "signed-feed.json"));
        using var signed = Verify(envelope.RootElement, trust.RootElement.GetProperty("Keys"));
        var signedRelease = signed.RootElement.GetProperty("Releases").EnumerateArray().Single(r => Text(r, "Version") == Program.Version);
        var signedAsset = signedRelease.GetProperty("Assets").EnumerateArray().Single(a => Text(a, "Flavor") == flavor);
        foreach (string name in new[] { "Flavor", "FileName", "Sha256" }) Need(Text(asset, name) == Text(signedAsset, name), "Unsigned update asset.");
        Need(asset.GetProperty("Bytes").GetInt64() == signedAsset.GetProperty("Bytes").GetInt64() &&
            release.GetProperty("Layout").GetInt32() == 3 && signedRelease.GetProperty("Layout").GetInt32() == 3 &&
            Text(release, "RuntimeVersion") == Text(signedRelease, "RuntimeVersion"), "Unsigned update identity.");
        string delta = Text(job, "DeltaFileName");
        var patches = signedRelease.GetProperty("Deltas").EnumerateArray().ToArray().AsEnumerable();
        if (signed.RootElement.TryGetProperty("BaselineDeltas", out var extended) && extended.ValueKind == JsonValueKind.Array) patches = patches.Concat(extended.EnumerateArray());
        Need(patches.Any(p => Text(p, "FileName") == delta && Text(p, "Flavor") == flavor), "Unsigned update patch.");
        string journalPath = Path.Combine(attempt, "backup", "transaction.json");
        using var journalDocument = Read(journalPath); var journal = journalDocument.RootElement;
        Need(Same(Text(journal, "Target"), Path.GetDirectoryName(target)!) && Same(Text(journal, "Backup"), Path.Combine(attempt, "backup")) &&
            Text(journal, "State") == "awaiting_start", "Wrong update transaction.");
        string staged = Path.Combine(Text(journal, "Staging"), "Dustweave.exe");
        Recovery.NoLinks(staged); Recovery.NoLinks(target);
        Need(Recovery.Hash(staged) == Recovery.Hash(target), "Installed notice differs from verified staging.");
        // Commit durably BEFORE ready.json, matching DailyUpdateEngine. If the
        // helper disappears after this point, recovery must keep this valid start.
        Atomic(journalPath, writer =>
        {
            writer.WriteStartObject();
            foreach (var property in journal.EnumerateObject())
                if (property.Name == "State") writer.WriteString("State", "completed"); else property.WriteTo(writer);
            writer.WriteEndObject();
        });
        Atomic(Path.Combine(attempt, "ready.json"), writer =>
        {
            writer.WriteStartObject(); writer.WriteNumber("ProcessId", Environment.ProcessId);
            writer.WriteString("Version", Program.Version); writer.WriteString("Nonce", nonce); writer.WriteEndObject();
        });
    }
    private static JsonDocument Verify(JsonElement envelope, JsonElement keys)
    {
        Need(envelope.GetProperty("Schema").GetInt32() == 1 && Text(envelope, "Algorithm") == "ECDSA-P256-SHA256", "Unknown update signature.");
        byte[] payload = Convert.FromBase64String(Text(envelope, "Payload"));
        Need(payload.Length is > 0 and <= 1048576, "Invalid signed metadata size.");
        using var key = ECDsa.Create(); key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(Text(keys, Text(envelope, "KeyId"))), out _);
        Need(key.VerifyData(payload, Convert.FromBase64String(Text(envelope, "Signature")), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation), "Invalid update signature.");
        var feed = JsonDocument.Parse(payload);
        try { Need(Text(feed.RootElement, "Product") == "Dustweave" && Text(feed.RootElement, "Channel") == "stable", "Wrong update product."); return feed; }
        catch { feed.Dispose(); throw; }
    }
    private static void Atomic(string path, Action<Utf8JsonWriter> write)
    {
        Recovery.NoLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { using var writer = new Utf8JsonWriter(stream); write(writer); writer.Flush(); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static void SelfTest()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] payload = Encoding.UTF8.GetBytes("{\"Product\":\"Dustweave\",\"Channel\":\"stable\"}");
        string body = Convert.ToBase64String(payload), signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        using var keys = JsonDocument.Parse("{\"test\":\"" + Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) + "\"}");
        string envelope = $$"""{"Schema":1,"Algorithm":"ECDSA-P256-SHA256","KeyId":"test","Payload":"{{body}}","Signature":"{{signature}}"}""";
        using var valid = JsonDocument.Parse(envelope); using var result = Verify(valid.RootElement, keys.RootElement);
        foreach (string bad in new[] { envelope.Replace("ECDSA-P256-SHA256", "none"), envelope.Replace("\"test\"", "\"unknown\""),
            envelope.Replace(body, Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"))), envelope.Replace("\"Schema\":1", "\"Schema\":2") })
        {
            bool rejected = false;
            try { using var document = JsonDocument.Parse(bad); using var ignored = Verify(document.RootElement, keys.RootElement); }
            catch (Exception e) when (e is InvalidDataException or KeyNotFoundException) { rejected = true; }
            Need(rejected, "Invalid signature fixture accepted.");
        }
    }
}
