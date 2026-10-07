using System.Security.Cryptography;
using System.Text.Json;

namespace Dustweave;

public sealed record DailyUpdateSource(string Id, string FeedUrl, string PackageBaseUrl);
public sealed record DailyUpdateTrust(Dictionary<string, string> Keys, DailyUpdateSource[] Sources)
{
    public static DailyUpdateTrust Production()
    {
        using var stream = typeof(DailyUpdateTrust).Assembly.GetManifestResourceStream("Dustweave.update-trust.json")!;
        return JsonSerializer.Deserialize<DailyUpdateTrust>(stream, DailyJson.Options)!;
    }
}
public sealed record DailySignedUpdate(int Schema, string KeyId, string Algorithm, string Payload, string Signature);
public sealed record DailyVerifiedUpdate(DailyUpdateFeed Feed, DailySignedUpdate Signed, string SourceId);

public static class DailyUpdateSignatures
{
    public const int MaxEnvelopeBytes = 3 * 1024 * 1024;
    public static DailySignedUpdate Sign(DailyUpdateFeed feed, string keyId, ECDsa key)
    {
        DailyUpdates.Validate(feed);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(feed, DailyJson.Options);
        return new(1, keyId, "ECDSA-P256-SHA256", Convert.ToBase64String(payload),
            Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
    }
    public static DailyUpdateFeed Verify(DailySignedUpdate envelope, DailyUpdateTrust trust)
    {
        try
        {
            if (envelope.Schema != 1 || envelope.Algorithm != "ECDSA-P256-SHA256" ||
                envelope.Payload.Length > 2800000 || !trust.Keys.TryGetValue(envelope.KeyId, out var publicKey))
                throw new InvalidDataException("updates.signature_failed");
            byte[] payload = Convert.FromBase64String(envelope.Payload), signature = Convert.FromBase64String(envelope.Signature);
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            if (key.KeySize != 256 || signature.Length != 64 ||
                !key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new InvalidDataException("updates.signature_failed");
            var feed = JsonSerializer.Deserialize<DailyUpdateFeed>(payload, DailyJson.Options) ?? throw new InvalidDataException("updates.invalid_feed");
            DailyUpdates.Validate(feed);
            return feed;
        }
        catch (Exception e) when (e is FormatException or CryptographicException or JsonException or NullReferenceException or ArgumentException)
        { throw new InvalidDataException("updates.signature_failed", e); }
    }
}
