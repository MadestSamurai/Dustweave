using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO.Compression;
using Dustweave;

static class PluginMarketCases
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(response(request)); }
    }
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); cases.Add("plugin market: " + label); }
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keys = new Dictionary<string, string> { ["test"] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) };
        string root = Path.Combine(output, "market-fixture"), source = Path.Combine(root, "payload");
        Directory.CreateDirectory(Path.Combine(source, "managed"));
        byte[] assembly = Encoding.UTF8.GetBytes("synthetic assembly, never loaded");
        File.WriteAllBytes(Path.Combine(source, "managed", "Example.dll"), assembly);
        string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
        var manifest = new JsonObject { ["id"] = "example.tactics", ["version"] = "1.0.0", ["publisher"] = "Example", ["apiVersion"] = 4,
            ["bridgeExtensionApi"] = 1, ["runtime"] = "net8.0-windows-x64", ["minHostVersion"] = "1.5.0",
            ["entryAssembly"] = "managed/Example.dll", ["entryType"] = "Example.Extension", ["capabilities"] = new JsonArray("tactics"), ["hookSources"] = new JsonArray(),
            ["files"] = new JsonArray(new JsonObject { ["path"] = "managed/Example.dll", ["sha256"] = Hash(assembly) }) };
        byte[] manifestBytes = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        File.WriteAllBytes(Path.Combine(source, "plugin.json"), manifestBytes);
        string zip = Path.Combine(root, "package.zip"); ZipFile.CreateFromDirectory(source, zip); byte[] archive = File.ReadAllBytes(zip);
        var names = new Dictionary<string, string> { ["en-US"] = "Tactics", ["zh-CN"] = "教材" };
        var plugin = new DailyMarketPlugin("example.tactics", "1.0.0", names, names, "Example", "1.5.0", "", "https://example.test/plugin.zip", archive.Length, Hash(archive), Hash(manifestBytes), ["tactics"]);
        var now = DateTimeOffset.UtcNow;
        var catalog = new DailyMarketCatalog(1, DailyPluginMarket.Purpose, 2, now, now.AddDays(7), [plugin]);
        byte[] Envelope(DailyMarketCatalog c) => JsonSerializer.SerializeToUtf8Bytes(DailyPluginMarket.Sign(c, "test", key), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        byte[] bytes = Envelope(catalog);
        Check(DailyPluginMarket.Verify(bytes, keys, now).Plugins[0].Id == plugin.Id, "signed catalog verified");
        void Reject(Action action, string label) { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } Check(rejected, label); }
        Reject(() => DailyPluginMarket.Verify(bytes, keys, now.AddDays(8)), "expired catalog cannot authorize installation");
        Reject(() => DailyPluginMarket.Verify(bytes, new Dictionary<string, string>(), now), "unknown signing key rejected");
        var envelope = JsonNode.Parse(bytes)!.AsObject(); envelope["signature"] = Convert.ToBase64String(new byte[64]);
        Reject(() => DailyPluginMarket.Verify(Encoding.UTF8.GetBytes(envelope.ToJsonString()), keys, now), "tampered signature rejected");
        foreach (var invalid in new[] { plugin with { Url = "http://example.test/a" }, plugin with { Bytes = DailyPluginMarket.MaxPackageBytes + 1 }, plugin with { Sha256 = "bad" }, plugin with { Capabilities = ["tactics", "tactics"] } })
            Reject(() => Envelope(catalog with { Plugins = [invalid] }), "invalid signed entry rejected");
        Reject(() => Envelope(catalog with { Plugins = [plugin, plugin] }), "duplicate plugin IDs rejected");
        // A valid OTA-key signature without the market domain prefix is not a market authorization.
        byte[] payload = Convert.FromBase64String(JsonNode.Parse(bytes)!["payload"]!.GetValue<string>());
        envelope = JsonNode.Parse(bytes)!.AsObject();
        envelope["signature"] = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Reject(() => DailyPluginMarket.Verify(Encoding.UTF8.GetBytes(envelope.ToJsonString()), keys, now), "signature purpose separated from updater");
        byte[] current = bytes, download = archive; bool offline = false, redirect = false;
        int requests = 0;
        using var market = new DailyPluginMarket(root, new Handler(request =>
        {
            requests++;
            if (offline) throw new HttpRequestException("test offline");
            if (request.RequestUri!.AbsolutePath.EndsWith("catalog.json")) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(current) };
            if (redirect) return new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("http://example.test/downgrade") } };
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(download) };
        }), keys);
        var store = new DailyPluginStore(root);
        Check(!DailyPluginSharing.Enabled(root, plugin.Id), "missing consent means entirely local calculation");
        DailyPluginSharing.Write(root, plugin.Id, true);
        Check(DailyPluginSharing.Enabled(root, plugin.Id) && !DailyPluginSharing.Enabled(root, "other.plugin"), "sharing consent is scoped to one plugin across versions");
        DailyPluginSharing.Write(root, plugin.Id, false);
        Check(!DailyPluginSharing.Enabled(root, plugin.Id) && DailyPluginSharing.Read(root, plugin.Id) != null, "explicit local choice is preserved rather than treated as unconfigured");
        Check(market.Cached() == null, "empty cache stays empty");
        await market.RefreshAsync();
        Check(market.Cached()?.Cached == true, "verified catalog cached for offline browsing");
        using (var package = await market.DownloadAsync(plugin, store)) Check(package.Info.Id == plugin.Id, "download validates archive and embedded manifest");
        Check(store.Read().Installed.Length == 0 && store.Read().Active == null, "download does not install or select code");
        async Task RejectAsync(Func<Task> operation, string label)
        { bool rejected = false; try { await operation(); } catch (InvalidDataException) { rejected = true; } Check(rejected, label); }
        await RejectAsync(() => market.DownloadAsync(plugin with { Url = "https://example.test/other" }, store), "UI metadata cannot replace signed authorization");
        download = [1, 2, 3]; await RejectAsync(() => market.DownloadAsync(plugin, store), "incomplete or modified download rejected"); download = archive;
        redirect = true; await RejectAsync(() => market.DownloadAsync(plugin, store), "redirect cannot downgrade HTTPS"); redirect = false;
        current = Envelope(catalog with { Revision = 1 }); await RejectAsync(() => market.RefreshAsync(), "catalog rollback rejected"); current = bytes;
        var badManifest = plugin with { ManifestSha256 = new string('b', 64) }; current = Envelope(catalog with { Plugins = [badManifest] }); await market.RefreshAsync();
        await RejectAsync(() => market.DownloadAsync(badManifest, store), "signed archive with wrong plugin identity rejected");
        current = bytes; await market.RefreshAsync(); offline = true;
        try { await market.RefreshAsync(); } catch (HttpRequestException) { }
        Check(market.Cached()?.Catalog.Revision == 2, "network failure preserves verified cache");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        int before = requests; try { await market.DownloadAsync(plugin, store, token: cancelled.Token); } catch (OperationCanceledException) { }
        Check(requests == before, "cancelled download never starts network transfer");
        Check(!Directory.EnumerateFiles(Path.Combine(root, "extensions", "market"), "*.zip").Any() && store.Read().Installed.Length == 0,
            "failed downloads clean only their temporary files and preserve selection");
    }
}
