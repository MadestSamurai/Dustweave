using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Dustweave.Ota;

static class MirrorSelfTest
{
    public static async Task Run(string parent, List<string> checks)
    {
        void Pass(string name, bool ok = true) { Rules.Need(ok, "Mirror test failed: " + name); checks.Add(name); }
        async Task Reject(string name, Func<Task> action)
        {
            try { await action(); } catch (InvalidDataException) { checks.Add(name); return; }
            throw new Exception("Mirror test did not reject: " + name);
        }
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keys = new Dictionary<string, string> { ["mirror-test"] = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo()) };
        var config = new Config(Path.Combine(parent, "mirror/state"), Path.Combine(parent, "mirror/public"), keys, 2,
            "https://test.cdn.7caiyun.com/dustweave/ota/v2");
        Directory.CreateDirectory(config.State); Directory.CreateDirectory(config.Public);
        var notes = new Dictionary<string, string[]> { ["zh-CN"] = ["test"], ["zh-TW"] = ["test"], ["en-US"] = ["test"] };
        var assets = new[] { "Lite", "Portable" }.Select(f => new Asset(f, $"Dustweave-1.4.13-{f}-win-x64.zip", 400, Rules.Hash([1, 2, 3, 4]))).ToArray();
        var deltas = assets.Select(a => new Delta(a.Flavor, "1.4.12", $"Dustweave-1.4.13-{a.Flavor}-from-1.4.12-win-x64.delta.zip",
            4, Rules.Hash([1, 2, 3, 4]), "dustweave-copy-v2")).ToArray();
        var current = new Release("1.4.13", null, 2, notes, assets, deltas, "8.0.30");
        var feed = new Feed(3, "Dustweave", "stable", [current]);
        byte[] Sign(Feed value)
        {
            var payload = Rules.Bytes(value);
            return Rules.Bytes(new Envelope(1, "mirror-test", "ECDSA-P256-SHA256", Convert.ToBase64String(payload),
                Convert.ToBase64String(signer.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))));
        }
        var signed = Sign(feed);
        File.WriteAllBytes(Path.Combine(config.Public, "updates.json"), signed);
        var versionFolder = Path.Combine(config.Public, "v1.4.13"); Directory.CreateDirectory(versionFolder);
        foreach (var delta in deltas) File.WriteAllBytes(Path.Combine(versionFolder, delta.FileName), [1, 2, 3, 4]);
        int requests = 0;
        HttpResponseMessage Response(HttpRequestMessage r)
        {
            Interlocked.Increment(ref requests);
            Rules.Need(r.RequestUri!.Host == "test.cdn.7caiyun.com" &&
                r.RequestUri.AbsolutePath.StartsWith("/dustweave/ota/v2/v1.4.13/Dustweave-", StringComparison.Ordinal), "Unexpected mirror destination");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) };
        }
        using var valid = new HttpClient(new Handler(Response));
        using var corrupt = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 5]) }));
        await Reject("mirror-same-size-corruption-rejected", () => Mirror.Prepare(config, signed, corrupt));
        Pass("mirror-no-routes-before-all-bodies-verified", !Directory.Exists(Path.Combine(Mirror.Root(config), "routes")) && !File.Exists(Mirror.Enabled(config)));
        await Mirror.Prepare(config, signed, valid);
        Pass("mirror-prepare-does-not-enable-before-health", !File.Exists(Mirror.Enabled(config)));
        Pass("mirror-valid-objects-enable-routes", await Mirror.Probe(config, valid) && File.Exists(Mirror.Enabled(config)));
        await Mirror.Prepare(config, signed, valid);
        Pass("mirror-repeat-preparation-idempotent", await Mirror.Probe(config, valid));
        using var unavailable = new HttpClient(new Handler(_ => new(HttpStatusCode.ServiceUnavailable)));
        Pass("mirror-outage-selects-origin", !await Mirror.Probe(config, unavailable) && !File.Exists(Mirror.Enabled(config)));
        Pass("mirror-recovery-enables-verified-routes", await Mirror.Probe(config, valid));
        using var wrongLength = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) }));
        Pass("mirror-truncated-object-disables-routes", !await Mirror.Probe(config, wrongLength) && !File.Exists(Mirror.Enabled(config)));
        using var redirect = new HttpClient(new Handler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.Found);
            r.Headers.Location = new Uri("https://unexpected.example/file"); return r;
        }));
        await Reject("mirror-does-not-accept-arbitrary-redirect", () => Mirror.Prepare(config, signed, redirect));
        foreach (var url in new[] { "http://test.cdn.7caiyun.com/dustweave/ota/v2", "https://test.cdn.7caiyun.com.evil.test/dustweave/ota/v2",
            "https://u:p@test.cdn.7caiyun.com/dustweave/ota/v2", "https://test.cdn.7caiyun.com/dustweave/ota/v2?a=b",
            "https://127.0.0.1/dustweave/ota/v2", "https://test.cdn.7caiyun.com/other" })
            await Reject("mirror-reject-invalid-base-" + url, () => { Mirror.BaseUrl(config with { MirrorBaseUrl = url }); return Task.CompletedTask; });
        Pass("mirror-keeps-signed-feed-byte-identical", Rules.HashFile(Path.Combine(config.Public, "updates.json")) == Rules.Hash(signed));
        string route = Path.Combine(Mirror.Root(config), "routes/v1.4.13", deltas[0].FileName);
        File.WriteAllText(route, new string('0', 64));
        Pass("mirror-route-tamper-disables-download-redirection", !await Mirror.Probe(config, valid));
        File.WriteAllText(route, deltas[0].Sha256);
        Pass("mirror-restored-route-recoverable", await Mirror.Probe(config, valid));
        var receipt = Path.Combine(Mirror.Root(config), "verified/v1.4.13.json");
        File.Delete(receipt);
        Pass("mirror-missing-receipt-falls-back", !await Mirror.Probe(config, valid) && !File.Exists(Mirror.Enabled(config)));

        const string original = """
        server {
            location = /updates/dustweave/v2/updates.json { alias /origin/updates.json; }
            location ^~ /updates/dustweave/v2/ {
                alias /usr/share/nginx/dustweave-updates-v2/;
                autoindex off;
            }
            location ^~ /updates/dustweave/ { alias /legacy/; }
            location / { proxy_pass http://simulator; }
        }
        """;
        var production = config with { Public = "/home/nginx/dustweave-updates-v2" };
        string patched = MirrorNginx.Patch(original, production);
        Pass("mirror-nginx-patch-idempotent", MirrorNginx.Patch(patched, production) == patched);
        Pass("mirror-nginx-keeps-manifest-legacy-and-simulator", patched.Contains("location = /updates/dustweave/v2/updates.json { alias /origin/updates.json; }") &&
            patched.Contains("location ^~ /updates/dustweave/ { alias /legacy/; }") && patched.Contains("location / { proxy_pass http://simulator; }"));
        Pass("mirror-nginx-temporary-uncached-redirect-and-origin-override", patched.Contains("return 302") &&
            patched.Contains("Cache-Control \"no-store\"") && patched.Contains("$arg_origin = \"1\""));
        Pass("mirror-nginx-requires-health-and-per-file-route", patched.Contains(".mirror/enabled") &&
            patched.Contains(".mirror/routes/$dw_relative") && patched.Contains("$dw_mirror = \"11\""));
        Pass("mirror-nginx-hides-internal-route-state", patched.Contains("location ^~ /updates/dustweave/v2/.mirror/ { return 404; }"));
        await Reject("mirror-nginx-rejects-missing-alias", () => { MirrorNginx.Patch("server {}", production); return Task.CompletedTask; });
        await Reject("mirror-nginx-rejects-custom-proxy-drift", () => { MirrorNginx.Patch(original.Replace("autoindex off;", "proxy_pass http://other;"), production); return Task.CompletedTask; });
    }
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
}
