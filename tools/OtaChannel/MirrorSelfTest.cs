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

        Config Setup(string name, int layout = 2)
        {
            var c = config with { State = Path.Combine(parent, name, "state"), Public = Path.Combine(parent, name, "public"), Layout = layout,
                MirrorBaseUrl = layout == 3 ? "https://test.cdn.7caiyun.com/dustweave/ota/all-in-one" : config.MirrorBaseUrl };
            Directory.CreateDirectory(c.State); Directory.CreateDirectory(c.Public);
            File.WriteAllBytes(Rules.FeedPath(c, writing: true), signed);
            Directory.CreateDirectory(Path.Combine(c.Public, "v1.4.13"));
            foreach (var delta in deltas) File.WriteAllBytes(Path.Combine(c.Public, "v1.4.13", delta.FileName), [1, 2, 3, 4]);
            return c;
        }
        MirrorFailure FailureOf(Config c) => Rules.Read<MirrorFailure>(File.ReadAllBytes(Path.Combine(Mirror.Root(c), "failures/v1.4.13.json")));
        var resumeConfig = Setup("mirror-resume");
        var gets = new List<string>(); int failedRequests = 0;
        using var partial = new HttpClient(new Handler(r =>
        {
            if (r.Method == HttpMethod.Get) gets.Add(r.RequestUri!.Segments.Last());
            if (r.RequestUri!.AbsolutePath.Contains("Portable"))
            { failedRequests++; return new(HttpStatusCode.ServiceUnavailable); }
            return Response(r);
        }));
        Pass("mirror-maintenance-partial-outage-keeps-origin", !await Mirror.Maintain(resumeConfig, partial) && !File.Exists(Mirror.Enabled(resumeConfig)));
        Pass("mirror-transient-download-retries-bounded", failedRequests == 3);
        var progress = Rules.Read<MirrorProgress>(File.ReadAllBytes(Path.Combine(Mirror.Root(resumeConfig), "progress/v1.4.13.json")));
        Pass("mirror-partial-preparation-saves-only-verified-body", progress.Files.Length == 1 && progress.Files[0].FileName.Contains("Lite"));
        var firstFailure = FailureOf(resumeConfig);
        Pass("mirror-preparation-error-identifies-file-and-status", firstFailure.FirstStage == "prepare" && firstFailure.FirstError.Contains("Portable") && firstFailure.FirstError.Contains("503"));
        await Mirror.Probe(resumeConfig, valid);
        Pass("mirror-later-probe-preserves-original-failure", FailureOf(resumeConfig).FirstError == firstFailure.FirstError && FailureOf(resumeConfig).LastStage == "probe");
        gets.Clear();
        using var resumed = new HttpClient(new Handler(r => { if (r.Method == HttpMethod.Get) gets.Add(r.RequestUri!.Segments.Last()); return Response(r); }));
        Pass("mirror-timer-recovers-missing-receipt", await Mirror.Maintain(resumeConfig, resumed) && File.Exists(Mirror.Enabled(resumeConfig)));
        Pass("mirror-resume-skips-only-exact-verified-body", gets.Count == 1 && gets[0].Contains("Portable"));
        Pass("mirror-recovery-preserves-error-history", FailureOf(resumeConfig).RecoveredUtc != null && FailureOf(resumeConfig).FirstError == firstFailure.FirstError);
        gets.Clear();
        Pass("mirror-healthy-timer-uses-head-only", await Mirror.Maintain(resumeConfig, resumed) && gets.Count == 0);
        using (var occupied = new FileStream(Path.Combine(resumeConfig.State, "publish.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Pass("mirror-busy-publication-does-not-disable-current-routes", !await Mirror.Maintain(resumeConfig, resumed) && File.Exists(Mirror.Enabled(resumeConfig)) && gets.Count == 0);

        var rejectConfig = Setup("mirror-reject-body"); int corruptRequests = 0;
        using var bodyMismatch = new HttpClient(new Handler(_ => { corruptRequests++; return new(HttpStatusCode.OK) { Content = new ByteArrayContent([9, 2, 3, 4]) }; }));
        Pass("mirror-integrity-error-not-retried-or-enabled", !await Mirror.Maintain(rejectConfig, bodyMismatch) && corruptRequests == 1 && !File.Exists(Mirror.Enabled(rejectConfig)));
        Pass("mirror-corrupt-body-never-checkpointed", !File.Exists(Path.Combine(Mirror.Root(rejectConfig), "progress/v1.4.13.json")));
        Pass("mirror-corrupt-body-can-recover-with-correct-download", await Mirror.Maintain(rejectConfig, valid));

        var canceledConfig = Setup("mirror-cancel-resume");
        using var cancel = new CancellationTokenSource();
        using var stalled = new HttpClient(new AsyncHandler(async (r, token) =>
        {
            if (r.RequestUri!.AbsolutePath.Contains("Portable")) { cancel.Cancel(); await Task.Delay(Timeout.Infinite, token); }
            return Response(r);
        }));
        Pass("mirror-canceled-run-stops-with-origin", !await Mirror.Maintain(canceledConfig, stalled, cancel.Token) && !File.Exists(Mirror.Enabled(canceledConfig)));
        Pass("mirror-timeout-error-keeps-file-identity", FailureOf(canceledConfig).FirstError.Contains("Portable"));
        gets.Clear();
        Pass("mirror-next-timer-resumes-canceled-run", await Mirror.Maintain(canceledConfig, resumed) && gets.Count == 1 && gets[0].Contains("Portable"));

        foreach (var mismatch in new[] { "feed", "endpoint", "blob" })
        {
            var c = Setup("mirror-checkpoint-" + mismatch);
            string p = Path.Combine(Mirror.Root(c), "progress/v1.4.13.json"); Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            var changed = progress with { Files = deltas.Select(d => new Blob(d.FileName, d.Bytes, d.Sha256)).ToArray() };
            changed = mismatch switch
            {
                "feed" => changed with { FeedSha256 = new string('0', 64) },
                "endpoint" => changed with { BaseUrl = "https://other.cdn.7caiyun.com/dustweave/ota/v2" },
                _ => changed with { Files = [changed.Files[0] with { Sha256 = new string('0', 64) }] }
            };
            File.WriteAllBytes(p, Rules.Bytes(changed)); gets.Clear();
            Pass("mirror-checkpoint-mismatch-rechecks-" + mismatch, await Mirror.Maintain(c, resumed) && gets.Count == 2);
        }
        var localCorrupt = Setup("mirror-local-corrupt");
        await Mirror.Prepare(localCorrupt, signed, valid);
        File.WriteAllBytes(Path.Combine(localCorrupt.Public, "v1.4.13", deltas[0].FileName), [1, 2, 3, 5]);
        File.Delete(Path.Combine(Mirror.Root(localCorrupt), "verified/v1.4.13.json"));
        Pass("mirror-resume-still-rehashes-origin-files", !await Mirror.Maintain(localCorrupt, valid) && !File.Exists(Mirror.Enabled(localCorrupt)));

        var changedFeed = Setup("mirror-feed-change");
        using var changeDuringCheck = new HttpClient(new Handler(r =>
        {
            if (r.Method == HttpMethod.Head) File.WriteAllBytes(Rules.FeedPath(changedFeed), Sign(feed with { Releases = [current with { Notes = notes.ToDictionary(p => p.Key, _ => new[] { "updated notes" }) }] }));
            return Response(r);
        }));
        Pass("mirror-feed-change-during-health-never-enables-stale-routes", !await Mirror.Maintain(changedFeed, changeDuringCheck) && !File.Exists(Mirror.Enabled(changedFeed)));

        var aio = Setup("mirror-aio-recovery", 3);
        var aioDeltas = deltas.Select(d => d with { FromVersion = "1.5.0", FileName = $"Dustweave-1.5.1-{d.Flavor}-from-1.5.0-win-x64.delta.zip" }).ToArray();
        var aioRelease = current with { Version = "1.5.1", Layout = 3, Deltas = aioDeltas,
            Assets = assets.Select(a => a with { FileName = $"Dustweave-1.5.1-{a.Flavor}-win-x64.zip" }).ToArray() };
        File.WriteAllBytes(Rules.FeedPath(aio, writing: true), Sign(feed with { Releases = [aioRelease] }));
        Directory.CreateDirectory(Path.Combine(aio.Public, "v1.5.1"));
        foreach (var d in aioDeltas) File.WriteAllBytes(Path.Combine(aio.Public, "v1.5.1", d.FileName), [1, 2, 3, 4]);
        using var aioClient = new HttpClient(new Handler(r => { Rules.Need(r.RequestUri!.AbsolutePath.StartsWith("/dustweave/ota/all-in-one/"), "Wrong AIO lane"); return new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) }; }));
        Pass("mirror-aio-first-maintenance-prepares-current-signed-lane", await Mirror.Maintain(aio, aioClient));
        string[] aioRoutes = Directory.GetFiles(Path.Combine(Mirror.Root(aio), "routes/v1.5.1"));
        Pass("mirror-aio-outage-does-not-switch-to-origin", !await Mirror.Probe(aio, unavailable) &&
            File.Exists(Mirror.Enabled(aio)) && aioRoutes.All(File.Exists));
        var aioStatus = System.Text.Json.JsonSerializer.SerializeToElement(Mirror.Status(aio));
        Pass("mirror-aio-reachability-separate-from-delivery", aioStatus.GetProperty("active").GetBoolean() &&
            !aioStatus.GetProperty("originFallback").GetBoolean() && aioStatus.GetProperty("health").GetProperty("State").GetString() == "cdn-degraded");
        File.Delete(Mirror.Enabled(aio)); // Previous publisher may have disabled this switch.
        Pass("mirror-aio-restart-with-old-disabled-switch-retains-verified-routes", !await Mirror.Maintain(aio, unavailable) &&
            System.Text.Json.JsonSerializer.SerializeToElement(Mirror.Status(aio)).GetProperty("active").GetBoolean());
        Pass("mirror-aio-real-recovery-clears-warning", await Mirror.Maintain(aio, aioClient) &&
            System.Text.Json.JsonSerializer.SerializeToElement(Mirror.Status(aio)).GetProperty("health").GetProperty("Error").ValueKind == System.Text.Json.JsonValueKind.Null);
        Pass("mirror-aio-wrong-length-does-not-divert-unrelated-files", !await Mirror.Probe(aio, wrongLength) && aioRoutes.All(File.Exists));
        using var instantTimeout = new HttpClient(new AsyncHandler((_, _) => throw new TaskCanceledException("injected timeout")));
        Pass("mirror-aio-timeout-identifies-file-without-origin", !await Mirror.Probe(aio, instantTimeout) &&
            File.ReadAllText(Path.Combine(Mirror.Root(aio), "health.json")).Contains("Dustweave-1.5.1-"));
        Pass("mirror-aio-repeated-recovery", await Mirror.Probe(aio, aioClient));

        int rangeRequests = 0;
        using var slowRange = new HttpClient(new AsyncHandler(async (r, token) =>
        {
            Rules.Need(r.Method == HttpMethod.Get && r.Headers.Range?.ToString() == "bytes=0-3", "Expected bounded download probe");
            Interlocked.Increment(ref rangeRequests);
            await Task.Delay(TimeSpan.FromSeconds(6.5), token);
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent([1, 2, 3, 4]) };
            response.Content.Headers.ContentRange = new(0, 3, 4); return response;
        }));
        Pass("mirror-aio-download-probe-survives-former-six-second-deadline", await Mirror.Probe(aio, slowRange) && rangeRequests == 2);
        using var wrongRange = new HttpClient(new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent([1, 2, 3, 4]) };
            response.Content.Headers.ContentRange = new(1, 4, 5); return response;
        }));
        Pass("mirror-aio-wrong-content-range-is-not-healthy", !await Mirror.Probe(aio, wrongRange));
        Pass("mirror-aio-same-size-range-corruption-is-not-healthy", !await Mirror.Probe(aio, corrupt));
        using var probeCancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        int canceledRequests = 0;
        using var waitingRange = new HttpClient(new AsyncHandler(async (_, token) =>
        { Interlocked.Increment(ref canceledRequests); await Task.Delay(Timeout.Infinite, token); return new(HttpStatusCode.OK); }));
        Pass("mirror-aio-cancel-bounds-download-probe-without-retrying", !await Mirror.Probe(aio, waitingRange, probeCancel.Token) && canceledRequests <= 2);
        Pass("mirror-aio-canceled-range-preserves-routes-and-diagnostic", aioRoutes.All(File.Exists) &&
            Rules.Read<MirrorHealth>(File.ReadAllBytes(Path.Combine(Mirror.Root(aio), "health.json"))).Error != null);
        Pass("mirror-aio-download-probe-recovers-after-cancellation", await Mirror.Probe(aio, aioClient));

        var capable = Rules.Capability(aio); Directory.CreateDirectory(capable.State);
        byte[] largeBody = Enumerable.Range(0, 70000).Select(i => (byte)(i % 251)).ToArray();
        var capableDeltas = aioDeltas.Select(d => d with { FromVersion = "1.6.0", FileName = $"Dustweave-1.6.1-{d.Flavor}-from-1.6.0-win-x64.delta.zip",
            Bytes = largeBody.Length, Sha256 = Rules.Hash(largeBody) }).ToArray();
        var capableRelease = aioRelease with { Version = "1.6.1", Deltas = capableDeltas,
            Assets = assets.Select(a => a with { FileName = $"Dustweave-1.6.1-{a.Flavor}-win-x64.zip" }).ToArray() };
        var capableFeed = Sign(feed with { Releases = [capableRelease] });
        File.WriteAllBytes(Rules.FeedPath(capable, writing: true), capableFeed);
        Directory.CreateDirectory(Path.Combine(capable.Public, "v1.6.1"));
        foreach (var d in capableDeltas) File.WriteAllBytes(Path.Combine(capable.Public, "v1.6.1", d.FileName), largeBody);
        var sampledStreams = new System.Collections.Concurrent.ConcurrentBag<CountedStream>();
        using var bothLanes = new HttpClient(new Handler(r =>
        {
            bool isCapable = r.RequestUri!.AbsolutePath.Contains("/v1.6.1/");
            byte[] data = isCapable ? largeBody : [1, 2, 3, 4];
            if (r.Headers.Range != null)
            {
                Rules.Need(r.Headers.Range.ToString() == $"bytes=0-{Math.Min(data.Length, 65536) - 1}", "Unexpected probe range");
                var stream = new CountedStream(data); sampledStreams.Add(stream);
                var content = new StreamContent(stream); content.Headers.ContentLength = data.Length;
                return new(HttpStatusCode.OK) { Content = content }; // CDN may ignore Range.
            }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
        }));
        Pass("mirror-aio-old-health-not-reported-for-new-feed", System.Text.Json.JsonSerializer.SerializeToElement(Mirror.Status(capable))
            .GetProperty("health").ValueKind == System.Text.Json.JsonValueKind.Null);
        Pass("mirror-aio-timer-maintains-both-signed-feeds", await Mirror.MaintainConfiguredFeeds(aio, bothLanes));
        Pass("mirror-aio-range-ignored-transfers-only-bounded-prefix", sampledStreams.Count == 4 &&
            sampledStreams.All(s => s.ReadBytes is 4 or 65536) && sampledStreams.Count(s => s.ReadBytes == 65536) == 2);
        await Mirror.Probe(aio, wrongLength);
        var capableStatus = System.Text.Json.JsonSerializer.SerializeToElement(Mirror.Status(capable));
        Pass("mirror-aio-transition-error-does-not-replace-capable-health", capableStatus.GetProperty("health").GetProperty("Version").GetString() == "1.6.1" &&
            capableStatus.GetProperty("health").GetProperty("State").GetString() == "cdn" &&
            capableStatus.GetProperty("preparation").GetProperty("Version").GetString() == "1.6.1" &&
            capableStatus.GetProperty("failure").ValueKind == System.Text.Json.JsonValueKind.Null);
        using (var occupied = new FileStream(Path.Combine(aio.State, "publish.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            sampledStreams.Clear();
            Pass("mirror-aio-busy-transition-does-not-starve-capable-probe", !await Mirror.MaintainConfiguredFeeds(aio, bothLanes) &&
                sampledStreams.Count == 2 && sampledStreams.All(s => s.ReadBytes == 65536));
        }
        Pass("mirror-aio-next-timer-recovers-both-lanes", await Mirror.MaintainConfiguredFeeds(aio, bothLanes));
        Pass("mirror-aio-timer-never-rewrites-either-signed-index", Rules.HashFile(Rules.FeedPath(capable)) == Rules.Hash(capableFeed) &&
            Rules.VerifyFeed(File.ReadAllBytes(Rules.FeedPath(aio)), aio).Releases[0].Version == "1.5.1");

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
        var aioProduction = aio with { Public = "/home/nginx/dustweave-updates-aio" };
        string aioPatched = MirrorNginx.BundlePatch(patched, aioProduction);
        Pass("mirror-aio-nginx-patch-idempotent", MirrorNginx.BundlePatch(aioPatched, aioProduction) == aioPatched);
        string aioBlock = aioPatched[..aioPatched.IndexOf("# END DUSTWEAVE AIO", StringComparison.Ordinal)];
        Pass("mirror-aio-nginx-no-origin-escape-or-global-health-switch", !aioBlock.Contains("$arg_origin") &&
            !aioBlock.Contains(".mirror/enabled") && !aioBlock.Contains("alias /usr/share/nginx/dustweave-updates-aio/;"));
        Pass("mirror-aio-nginx-unverified-request-bounded-unavailable", aioBlock.Contains("return 503;") && aioBlock.Contains("Retry-After"));
        Pass("mirror-aio-nginx-preserves-other-lanes", aioPatched.EndsWith(patched[patched.IndexOf("# BEGIN DUSTWEAVE OTA CDN", StringComparison.Ordinal)..], StringComparison.Ordinal));
        await Reject("mirror-aio-nginx-ambiguous-block-rejected", () => { MirrorNginx.BundlePatch(aioPatched + "\n# BEGIN DUSTWEAVE AIO", aioProduction); return Task.CompletedTask; });
    }
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
    sealed class CountedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public long ReadBytes { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { int n = await base.ReadAsync(buffer, token); ReadBytes += n; return n; }
    }
    sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
