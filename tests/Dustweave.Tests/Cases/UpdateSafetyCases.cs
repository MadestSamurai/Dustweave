using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dustweave;

internal static class UpdateSafetyCases
{
    public static async Task Run(string root, List<string> cases)
    {
        void Check(string name, bool value) { if (!value) throw new Exception(name); cases.Add(name); }
        void Reject(string name, Action action) { try { action(); } catch (InvalidDataException) { cases.Add(name); return; } throw new Exception(name); }
        string data = Path.Combine(root, "update-safety"); Directory.CreateDirectory(data);
        var notes = new Dictionary<string, string[]> { ["zh-CN"] = ["升级"], ["zh-TW"] = ["升級"], ["en-US"] = ["Update"] };
        byte[] package = Encoding.UTF8.GetBytes(new string('x', 500));
        var asset = new DailyUpdateAsset("Lite", "Dustweave-0.9.1-Lite-win-x64.zip", package.Length, Convert.ToHexString(SHA256.HashData(package)));
        var release = new DailyUpdateRelease("0.9.1", null, 1, notes, [asset]);
        var feed = new DailyUpdateFeed(2, "Dustweave", "stable", [release]);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var sources = new[] { new DailyUpdateSource("cn", "https://cn.test/updates.json", "https://cn.test"), new DailyUpdateSource("global", "https://global.test/updates.json", "https://global.test") };
        var trust = new DailyUpdateTrust(new() { ["test"] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) }, sources);
        var signed = DailyUpdateSignatures.Sign(feed, "test", key);
        Check("signed manifest authenticates release and package hashes", DailyUpdateSignatures.Verify(signed, trust).Releases[0].Assets[0] == asset);
        Reject("tampered signed payload is rejected", () => DailyUpdateSignatures.Verify(signed with { Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}")) }, trust));
        Reject("untrusted signing key is rejected", () => DailyUpdateSignatures.Verify(signed with { KeyId = "other" }, trust));
        Reject("missing or invalid signature is rejected", () => DailyUpdateSignatures.Verify(signed with { Signature = "!!" }, trust));
        Reject("unsigned legacy manifest cannot be used", () => DailyUpdateSignatures.Verify(new(0, "", "", "", ""), trust));
        var older = feed with { Releases = [release with { Version = "0.9.0", Assets = [asset with { FileName = asset.FileName.Replace("0.9.1", "0.9.0") }] }] };
        var oldSigned = DailyUpdateSignatures.Sign(older, "test", key);
        var transport = new DailyUpdateTransport(trust);
        int downloads = 0; bool rangeSeen = false;
        var handler = new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/updates.json")
                return Json(request.RequestUri.Host == "cn.test" ? oldSigned : signed);
            downloads++;
            if (request.RequestUri.Host == "global.test") return new(HttpStatusCode.ServiceUnavailable);
            long start = request.Headers.Range?.Ranges.Single().From ?? 0; rangeSeen = start == 31;
            var response = new HttpResponseMessage(start > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(package[(int)start..]) };
            if (start > 0) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, package.Length - 1, package.Length);
            return response;
        });
        using var client = new HttpClient(handler);
        var discovered = await transport.FetchAsync(client, data, default);
        Check("stale first mirror cannot hide a newer signed release", discovered.SourceId == "global");
        string cache = Path.Combine(data, "updates", "0.9.1-Lite"); Directory.CreateDirectory(cache);
        File.WriteAllBytes(Path.Combine(cache, "package.zip.partial"), package[..31]);
        string ready = await transport.DownloadAsync(client, discovered, "0.9.1", "Lite", data, null, default);
        Check("download fails over and resumes the same verified package", downloads == 3 && rangeSeen && DailyUpdates.Verify(Path.Combine(ready, "package.zip"), asset));
        int before = downloads;
        await transport.DownloadAsync(client, discovered, "0.9.1", "Lite", data, null, default);
        Check("verified cached package avoids repeat downloads", downloads == before);
        Check("successful download route is remembered", DailyJson.TryRead<DailyUpdateNetworkState>(Path.Combine(data, "updates/network.json"))?.Source == "cn");
        using var stale = new HttpClient(new Handler(_ => Json(oldSigned)));
        try { await transport.FetchAsync(stale, data, default); throw new Exception("stale accepted"); }
        catch (InvalidDataException e) { Check("previously seen version cannot silently disappear", e.Message == "updates.stale_feed"); }
        var different = DailyUpdateSignatures.Sign(feed with { Releases = [release with { Assets = [asset with { Sha256 = new string('0',64) }] }] }, "test", key);
        using var conflict = new HttpClient(new Handler(r => Json(r.RequestUri!.Host == "cn.test" ? signed : different)));
        try { await transport.FetchAsync(conflict, data, default); throw new Exception("conflict accepted"); }
        catch (InvalidDataException e) { Check("conflicting signed mirrors block installation", e.Message == "updates.mirror_conflict"); }
        using var bad = new HttpClient(new Handler(_ => Json(signed with { Signature = "AA==" })));
        try { await transport.FetchAsync(bad, data, default); throw new Exception("signature accepted"); }
        catch (InvalidDataException e) { Check("invalid signatures remain a status error", e.Message == "updates.signature_failed"); }
        using var mixed = new HttpClient(new Handler(r => Json(r.RequestUri!.Host == "cn.test" ? signed with { Signature = "AA==" } : signed)));
        var mixedResult = await transport.FetchAsync(mixed, Path.Combine(data, "mixed-signatures"), default);
        Check("one invalid manifest does not block another trusted source", mixedResult.SourceId == "global");
        int transientCalls = 0;
        using var transient = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)
        { Content = new ByteArrayContent(++transientCalls == 1 ? new byte[package.Length] : package) }));
        var transientFolder = await transport.DownloadAsync(transient, discovered with { SourceId = "cn" }, "0.9.1", "Lite", Path.Combine(data, "transient-corrupt"), null, default);
        Check("corrupt bytes are discarded before the bounded same-source retry", transientCalls == 2 && DailyUpdates.Verify(Path.Combine(transientFolder, "package.zip"), asset));
        int corruptRouteCalls = 0;
        using var corruptRoute = new HttpClient(new Handler(r =>
        {
            corruptRouteCalls++;
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(r.RequestUri!.Host == "cn.test" ? new byte[package.Length] : package) };
        }));
        var fallbackFolder = await transport.DownloadAsync(corruptRoute, discovered with { SourceId = "cn" }, "0.9.1", "Lite", Path.Combine(data, "corrupt-route"), null, default);
        Check("persistent corrupt route falls back to verified bytes on the next source", corruptRouteCalls == 3 && DailyUpdates.Verify(Path.Combine(fallbackFolder, "package.zip"), asset));
        string ignored = Path.Combine(data, "range-ignored"); Directory.CreateDirectory(Path.Combine(ignored, "updates/0.9.1-Lite"));
        File.WriteAllBytes(Path.Combine(ignored, "updates/0.9.1-Lite/package.zip.partial"), package[..31]);
        using var noRange = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(package) }));
        var downloaded = await transport.DownloadAsync(noRange, discovered, "0.9.1", "Lite", ignored, null, default);
        Check("Range ignored by server restarts rather than corrupts download", DailyUpdates.Verify(Path.Combine(downloaded, "package.zip"), asset));
        string corrupt = Path.Combine(data, "corrupt");
        using var badArchive = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[package.Length]) }));
        try { await transport.DownloadAsync(badArchive, discovered, "0.9.1", "Lite", corrupt, null, default); throw new Exception("bad archive accepted"); }
        catch (InvalidDataException e) { Check("matching length cannot bypass package digest", e.Message == "updates.hash_failed"); }
        Check("corrupt download never becomes ready", !File.Exists(Path.Combine(corrupt, "updates/0.9.1-Lite/package.zip")));

        string target = Path.Combine(data, "install"), staging = Path.Combine(data, "staging"), backup = Path.Combine(data, "backup");
        Directory.CreateDirectory(Path.Combine(staging, "flows")); Directory.CreateDirectory(Path.Combine(staging, "connection/specs"));
        File.WriteAllText(Path.Combine(staging, "Dustweave.exe"), "new binary");
        File.WriteAllText(Path.Combine(staging, "flows/new.json"), "{}"); File.WriteAllText(Path.Combine(staging, "connection/specs/a.json"), "{}");
        DailyJson.Write(Path.Combine(staging, "update-package.json"), new DailyUpdatePackage("0.9.1", "Lite"));
        string[] names = ["Dustweave.exe", "update-package.json", "flows/new.json", "connection/specs/a.json"];
        Directory.CreateDirectory(Path.Combine(target, "flows")); Directory.CreateDirectory(Path.Combine(target, "plugins"));
        File.WriteAllText(Path.Combine(target, "Dustweave.exe"), "old binary");
        File.WriteAllText(Path.Combine(target, "flows/obsolete.json"), "old flow");
        File.WriteAllText(Path.Combine(target, "plugins/private.dll"), "keep plugin");
        File.WriteAllText(Path.Combine(target, "accounts.json"), "keep account");
        DailyJson.Write(Path.Combine(target, "update-package.json"), new DailyUpdatePackage("0.9.0", "Lite", ["Dustweave.exe", "flows/obsolete.json", "update-package.json"]));
        var journal = DailyUpdateTransaction.Prepare(staging, target, backup, names);
        try { DailyUpdateTransaction.Apply(journal, i => { if (i == 2) throw new IOException("simulated interruption"); }); } catch (IOException) { }
        // Discard the in-memory transaction; recover only from persisted data.
        journal = DailyUpdateTransaction.Read(backup, target);
        Check("interruption leaves durable recovery journal", journal.State == "replacing");
        DailyUpdateTransaction.Restore(journal);
        Check("recovery restores binary and removes new flow files", File.ReadAllText(Path.Combine(target, "Dustweave.exe")) == "old binary" && !File.Exists(Path.Combine(target, "flows/new.json")));
        Check("recovery preserves accounts and private plugins", File.ReadAllText(Path.Combine(target, "accounts.json")) == "keep account" && File.ReadAllText(Path.Combine(target, "plugins/private.dll")) == "keep plugin");
        DailyUpdateTransaction.Restore(DailyUpdateTransaction.Read(backup, target));
        Check("recovery is idempotent", File.ReadAllText(Path.Combine(target, "flows/obsolete.json")) == "old flow");
        var retry = DailyUpdateTransaction.Prepare(staging, target, backup + "-retry", names);
        DailyUpdateTransaction.Apply(retry);
        Check("obsolete owned application files are removed on success", !File.Exists(Path.Combine(target, "flows/obsolete.json")));
        Check("configuration directories are part of OTA", File.Exists(Path.Combine(target, "flows/new.json")) && File.Exists(Path.Combine(target, "connection/specs/a.json")));
        File.WriteAllText(Path.Combine(backup + "-retry", "Dustweave.exe"), "damaged");
        Reject("recovery checks backup integrity before changing target", () => DailyUpdateTransaction.Restore(retry));
        Check("damaged backup cannot partly roll back installation", File.ReadAllText(Path.Combine(target, "Dustweave.exe")) == "new binary");
    }
    private static HttpResponseMessage Json(DailySignedUpdate envelope) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(envelope, DailyJson.Options)) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> run) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(run(request)); }
}
