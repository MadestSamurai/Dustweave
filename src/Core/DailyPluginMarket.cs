using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dustweave;

public sealed record DailyMarketPlugin(string Id, string Version, Dictionary<string, string> Names,
    Dictionary<string, string> Descriptions, string Publisher, string MinHostVersion, string MaxHostVersion,
    string Url, long Bytes, string Sha256, string ManifestSha256, string[] Capabilities)
{
    public bool SupportsSolutionSharing { get; init; }
    public string Name(string language) => Names.GetValueOrDefault(language) ?? Names["en-US"];
    public string Description(string language) => Descriptions.GetValueOrDefault(language) ?? Descriptions["en-US"];
    public bool Compatible => DailyPlugin.CompatibleHost(MinHostVersion, MaxHostVersion, DailyPlugin.HostVersion);
}
public sealed record DailyMarketCatalog(int Schema, string Purpose, long Revision, DateTimeOffset PublishedUtc,
    DateTimeOffset ExpiresUtc, DailyMarketPlugin[] Plugins);
public sealed record DailyMarketSnapshot(DailyMarketCatalog Catalog, bool Cached);

/// <summary>The catalog authorizes immutable downloads, never execution. Activation remains a separate probed transaction.</summary>
public sealed class DailyPluginMarket : IDisposable
{
    public const string CatalogUrl = "https://bd2.madsam.work/dustweave/plugins/catalog.json";
    public const int MaxCatalogBytes = 256 * 1024;
    public const long MaxPackageBytes = 64L * 1024 * 1024;
    public const string Purpose = "dustweave-plugin-market-v1";
    private static readonly byte[] Domain = Encoding.UTF8.GetBytes(Purpose + "\n");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string directory;
    private readonly HttpClient client;
    private readonly IReadOnlyDictionary<string, string> keys;
    public DailyPluginMarket(string dataRoot) : this(dataRoot, new HttpClientHandler { AllowAutoRedirect = false }, DailyUpdateTrust.Production().Keys) { }
    internal DailyPluginMarket(string dataRoot, HttpMessageHandler handler, IReadOnlyDictionary<string, string> keys)
    {
        directory = Path.Combine(dataRoot, "extensions", "market");
        this.keys = keys;
        client = new(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Dustweave/" + DailyPlugin.HostVersion);
    }
    public static byte[] SigningData(byte[] payload) => Domain.Concat(payload).ToArray();
    public static DailySignedUpdate Sign(DailyMarketCatalog catalog, string keyId, ECDsa key)
    {
        Validate(catalog, DateTimeOffset.UtcNow);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(catalog, Json);
        return new(1, keyId, "ECDSA-P256-SHA256", Convert.ToBase64String(payload),
            Convert.ToBase64String(key.SignData(SigningData(payload), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
    }
    public static DailyMarketCatalog Verify(byte[] bytes, IReadOnlyDictionary<string, string> keys, DateTimeOffset now)
    {
        try
        {
            if (bytes.Length > MaxCatalogBytes) throw new InvalidDataException("plugins.market_invalid");
            var signed = JsonSerializer.Deserialize<DailySignedUpdate>(bytes, Json)!;
            if (signed.Schema != 1 || signed.Algorithm != "ECDSA-P256-SHA256" || !keys.TryGetValue(signed.KeyId, out string? publicKey))
                throw new InvalidDataException("plugins.market_signature");
            byte[] payload = Convert.FromBase64String(signed.Payload), signature = Convert.FromBase64String(signed.Signature);
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            if (key.KeySize != 256 || signature.Length != 64 || !key.VerifyData(SigningData(payload), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new InvalidDataException("plugins.market_signature");
            var catalog = JsonSerializer.Deserialize<DailyMarketCatalog>(payload, Json)!;
            Validate(catalog, now);
            return catalog;
        }
        catch (Exception e) when (e is JsonException or FormatException or CryptographicException or NullReferenceException or ArgumentException)
        { throw new InvalidDataException("plugins.market_signature", e); }
    }
    private static void Validate(DailyMarketCatalog c, DateTimeOffset now)
    {
        bool Hash(string s) => s is { Length: 64 } && s.All(char.IsAsciiHexDigit);
        bool Text(Dictionary<string, string> d) => d != null && d.TryGetValue("en-US", out var text) && !string.IsNullOrWhiteSpace(text) && d.Count <= 20 && d.All(p => p.Key.Length <= 16 && p.Value is { Length: > 0 and <= 1600 });
        if (c == null || c.Schema != 1 || c.Purpose != Purpose || c.Revision < 1 || c.PublishedUtc > now.AddHours(1) ||
            c.ExpiresUtc <= now || c.ExpiresUtc <= c.PublishedUtc || c.Plugins == null || c.Plugins.Length > 100 ||
            c.Plugins.Select(p => p?.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != c.Plugins.Length)
            throw new InvalidDataException("plugins.market_invalid");
        foreach (var p in c.Plugins)
            if (p == null || !DailyPlugin.ValidId(p.Id) || !Version.TryParse(p.Version, out _) || !Text(p.Names) || !Text(p.Descriptions) ||
                string.IsNullOrWhiteSpace(p.Publisher) || p.Publisher.Length > 120 || !Hash(p.Sha256) || !Hash(p.ManifestSha256) ||
                p.Bytes is <= 0 or > MaxPackageBytes || !Https(p.Url) || p.MinHostVersion == null || p.MaxHostVersion == null ||
                (p.MinHostVersion.Length > 0 && !Version.TryParse(p.MinHostVersion, out _)) || (p.MaxHostVersion.Length > 0 && !Version.TryParse(p.MaxHostVersion, out _)) ||
                p.Capabilities is not { Length: > 0 and <= 20 } || p.Capabilities.Any(s => string.IsNullOrEmpty(s) || s.Length > 80 || s.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')) ||
                p.Capabilities.Distinct(StringComparer.Ordinal).Count() != p.Capabilities.Length)
                throw new InvalidDataException("plugins.market_invalid");
    }
    private static bool Https(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" && u.UserInfo.Length == 0 && u.Fragment.Length == 0 && u.IsDefaultPort;
    private string Cache => Path.Combine(directory, "catalog.json");
    public DailyMarketSnapshot? Cached()
    {
        try { return File.Exists(Cache) && new FileInfo(Cache).Length <= MaxCatalogBytes ? new(Verify(File.ReadAllBytes(Cache), keys, DateTimeOffset.UtcNow), true) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { return null; }
    }
    public async Task<DailyMarketSnapshot> RefreshAsync(CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var response = await GetAsync(CatalogUrl, timeout.Token);
        using var output = new MemoryStream();
        await CopyAsync(response, output, MaxCatalogBytes, null, timeout.Token);
        var bytes = output.ToArray(); var catalog = Verify(bytes, keys, DateTimeOffset.UtcNow);
        if (Cached() is { } previous && catalog.Revision < previous.Catalog.Revision) throw new InvalidDataException("plugins.market_rollback");
        DailyPluginStore.NoLinks(directory); Directory.CreateDirectory(directory);
        DailyPluginStore.NoLinks(Cache);
        string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try { await File.WriteAllBytesAsync(temporary, bytes, timeout.Token); File.Move(temporary, Cache, true); }
        finally { File.Delete(temporary); }
        return new(catalog, false);
    }
    public async Task<DailyPluginPackage> DownloadAsync(DailyMarketPlugin item, DailyPluginStore store, IProgress<int>? progress = null, CancellationToken token = default)
    {
        // Re-read the signed catalog: UI metadata is never sufficient authorization.
        var catalog = Cached()?.Catalog ?? throw new InvalidDataException("plugins.market_invalid");
        var authorized = catalog.Plugins.SingleOrDefault(p => p.Id == item.Id && p.Version == item.Version);
        if (authorized == null || JsonSerializer.Serialize(authorized, Json) != JsonSerializer.Serialize(item, Json)) throw new InvalidDataException("plugins.market_invalid");
        if (!item.Compatible) throw new InvalidDataException("plugins.incompatible_host");
        DailyPluginStore.NoLinks(directory); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(5));
            using var response = await GetAsync(item.Url, timeout.Token);
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
            {
                long length = await CopyAsync(response, output, item.Bytes, progress, timeout.Token);
                output.Position = 0;
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(output, timeout.Token));
                if (length != item.Bytes || !hash.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("plugins.invalid_hash");
            }
            token.ThrowIfCancellationRequested();
            var package = await Task.Run(() => store.Prepare(path), token);
            if (package.Info.Id != item.Id || package.Info.Version != item.Version || package.Info.Publisher != item.Publisher ||
                !package.Info.Fingerprint.Equals(item.ManifestSha256, StringComparison.OrdinalIgnoreCase) || package.Info.SupportsSolutionSharing != item.SupportsSolutionSharing || !package.Info.Capabilities!.Order().SequenceEqual(item.Capabilities.Order()))
            { package.Dispose(); throw new InvalidDataException("plugins.market_package_mismatch"); }
            return package;
        }
        finally { File.Delete(path); }
    }
    private async Task<HttpResponseMessage> GetAsync(string url, CancellationToken token)
    {
        for (int redirect = 0; redirect < 6; redirect++)
        {
            if (!Https(url)) throw new InvalidDataException("plugins.market_invalid");
            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var next = response.Headers.Location; response.Dispose();
                if (next == null) throw new InvalidDataException("plugins.market_invalid");
                url = new Uri(new Uri(url), next).AbsoluteUri; continue;
            }
            try { response.EnsureSuccessStatusCode(); return response; } catch { response.Dispose(); throw; }
        }
        throw new InvalidDataException("plugins.market_invalid");
    }
    private static async Task<long> CopyAsync(HttpResponseMessage response, Stream output, long limit, IProgress<int>? progress, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("plugins.package_too_large");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        var buffer = new byte[81920]; long length = 0; int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            length += count; if (length > limit) throw new InvalidDataException("plugins.package_too_large");
            await output.WriteAsync(buffer.AsMemory(0, count), token); progress?.Report((int)(length * 100 / limit));
        }
        return length;
    }
    public void Dispose() => client.Dispose();
}
