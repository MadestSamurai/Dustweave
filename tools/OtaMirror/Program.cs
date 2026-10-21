using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Dustweave.Ota;

record Connection(string Endpoint, string Region, string Bucket, string PublicBaseUrl, string AccessKey, string SecretKey);
record Trust(Dictionary<string, string> Keys);
record Published(string FileName, long Bytes, string Sha256, string Url, bool Uploaded);

static class Program
{
    static void Validate(Connection c)
    {
        Rules.Need(c.Endpoint == "https://s3.7caiyun.com" && c.Region == "us-west", "Unexpected storage endpoint");
        Rules.Need(System.Text.RegularExpressions.Regex.IsMatch(c.Bucket, @"^[a-z0-9-]{3,63}$"), "Invalid bucket");
        Rules.Need(c.PublicBaseUrl.TrimEnd('/') == $"https://{c.Bucket}.cdn.7caiyun.com", "Bucket and public host differ");
        Rules.Need(!string.IsNullOrWhiteSpace(c.AccessKey) && !string.IsNullOrWhiteSpace(c.SecretKey), "Storage credentials missing");
    }
    static async Task MainRun(string[] args)
    {
        if (args.Length == 4 && args[0] == "build-repair")
        { RepairBuilder.Build(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), Path.GetFullPath(args[3])); return; }
        if (args.SequenceEqual(new[] { "self-test" }))
        {
            var valid = new Connection("https://s3.7caiyun.com", "us-west", "dustweave-test", "https://dustweave-test.cdn.7caiyun.com", "test", "test");
            Validate(valid);
            int checks = 1;
            foreach (var bad in new[] { valid with { Endpoint = "http://s3.7caiyun.com" }, valid with { Region = "other" },
                valid with { Bucket = "../escape" }, valid with { PublicBaseUrl = "https://unexpected.example" }, valid with { SecretKey = "" } })
            {
                try { Validate(bad); } catch (InvalidDataException) { checks++; continue; }
                throw new Exception("Invalid configuration accepted");
            }
            Console.WriteLine($"{checks} mirror upload configuration checks passed");
            return;
        }
        Rules.Need(args.Length == 5 && args[0] is "upload" or "upload-repair", "Usage: upload|upload-repair PRIVATE_CONFIG PACKAGE_DIRECTORY PUBLIC_TRUST RECEIPT");
        bool repair = args[0] == "upload-repair";
        var connection = Rules.Read<Connection>(File.ReadAllBytes(args[1]));
        Validate(connection);
        string packages = Path.GetFullPath(args[2]);
        var trust = Rules.Read<Trust>(File.ReadAllBytes(args[3]));
        string publicBase = connection.PublicBaseUrl.TrimEnd('/') + "/dustweave/ota/v2";
        var config = new Config("", "", trust.Keys, 2, publicBase);
        var feedBytes = File.ReadAllBytes(Path.Combine(packages, "updates.json"));
        var feed = Rules.VerifyFeed(feedBytes, config);
        string version = feed.Releases[0].Version;
        var files = repair ? RepairBuilder.Inventory(packages, config) :
            Rules.Patches(feed).Select(d => new Blob(d.FileName, d.Bytes, d.Sha256.ToLowerInvariant())).OrderBy(b => b.FileName).ToArray();
        Rules.Need(files.Length > 0 && files.Select(f => f.FileName).Distinct().Count() == files.Length, "Empty or duplicate inventory");
        // Validate the complete set before sending any credential or object.
        foreach (var file in files) { if (!repair) _ = Mirror.FileUri(config, version, file); Rules.CheckArchive(Path.Combine(packages, file.FileName), file); }
        using var s3 = new AmazonS3Client(new BasicAWSCredentials(connection.AccessKey, connection.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = connection.Endpoint, AuthenticationRegion = connection.Region, ForcePathStyle = false,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED, MaxErrorRetry = 0,
                Timeout = TimeSpan.FromMinutes(5), HttpClientFactory = new DirectFactory()
            });
        using var client = Mirror.Client();
        var published = new System.Collections.Concurrent.ConcurrentBag<Published>();
        int completed = 0;
        await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = repair ? 8 : 1 }, async (file, cancellation) =>
        {
            string relative = repair ? file.FileName : $"v{version}/{file.FileName}";
            string key = "dustweave/ota/v2/" + relative;
            bool exists = false;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMinutes(8));
            try
            {
                var head = await s3.GetObjectMetadataAsync(connection.Bucket, key, timeout.Token);
                Rules.Need(head.Headers.ContentLength == file.Bytes && head.Metadata["x-amz-meta-sha256"] == file.Sha256,
                    "Refusing to overwrite different immutable object: " + file.FileName);
                exists = true;
            }
            catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound) { }
            if (!exists)
            {
                using var input = File.OpenRead(Path.Combine(packages, file.FileName));
                var put = new PutObjectRequest
                {
                    BucketName = connection.Bucket, Key = key, InputStream = input, UseChunkEncoding = false,
                    ContentType = repair ? "application/octet-stream" : "application/zip", AutoCloseStream = false
                };
                put.Headers.CacheControl = "public, max-age=31536000, immutable";
                put.Metadata.Add("sha256", file.Sha256);
                await s3.PutObjectAsync(put, timeout.Token);
            }
            var uri = new Uri(publicBase + "/" + relative);
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            Rules.Need(response.StatusCode == HttpStatusCode.OK && response.Content.Headers.ContentLength == file.Bytes,
                "Public download size or status differs: " + file.FileName);
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[65536]; long length = 0;
            while (true)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                idle.CancelAfter(TimeSpan.FromSeconds(30));
                int read = await stream.ReadAsync(buffer, idle.Token);
                if (read == 0) break;
                length += read; Rules.Need(length <= file.Bytes, "Public download exceeds signed size");
                hash.AppendData(buffer, 0, read);
            }
            Rules.Need(length == file.Bytes && Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase),
                "Public download SHA-256 differs: " + file.FileName);
            published.Add(new(file.FileName, file.Bytes, file.Sha256, uri.AbsoluteUri, !exists));
            Console.WriteLine($"{Interlocked.Increment(ref completed)}/{files.Length}: verified {file.FileName} ({file.Bytes} bytes)");
        });
        var receipt = new { status = "passed", version, feedSha256 = Rules.Hash(feedBytes), publicBaseUrl = publicBase,
            checkedUtc = DateTimeOffset.UtcNow, bytes = published.Sum(f => f.Bytes), files = published.OrderBy(f => f.FileName, StringComparer.Ordinal).ToArray() };
        string output = Path.GetFullPath(args[4]); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        Rules.Atomic(output, Rules.Bytes(receipt));
        Console.WriteLine($"Verified {published.Count} public OTA objects; receipt: {output}");
    }
    static async Task<int> Main(string[] args)
    {
        try { await MainRun(args); return 0; }
        // SDK exception text may contain a signed URL. Only expose status and service error code.
        catch (AmazonS3Exception e) { Console.Error.WriteLine($"Storage failed: HTTP {(int)e.StatusCode}, code {e.ErrorCode}"); return 1; }
        catch (Exception e) when (e is InvalidDataException or IOException or JsonException or CryptographicException or OperationCanceledException or HttpRequestException)
        { Console.Error.WriteLine(e.Message); return 1; }
    }
    sealed class DirectFactory : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig config) => new(new HttpClientHandler { UseProxy = false })
        { Timeout = TimeSpan.FromMinutes(5) };
    }
}
