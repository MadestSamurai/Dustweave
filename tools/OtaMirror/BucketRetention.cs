using Amazon.S3;
using Amazon.S3.Model;
using Dustweave.Ota;
using System.Net;

record StoredObject(string Key, long Bytes, string ETag, DateTimeOffset ModifiedUtc);
record BucketInventory(string Bucket, DateTimeOffset CheckedUtc, long ObjectBytes, long MultipartBytes, StoredObject[] Objects);
record FeedPin(string Url, string Sha256);
record RetentionPlan(string Bucket, DateTimeOffset CreatedUtc, long BeforeBytes, string[] KeepVersions, FeedPin[] Feeds, StoredObject[] Delete);

// Whole-bucket accounting includes unrelated plugin/legacy objects and unfinished
// uploads. Only our recognized, unreferenced AIO target directories and explicitly
// named historical experiments can be pruned; account/replay/solution data cannot.
static class BucketRetention
{
    internal const long UploadCeiling = 8_000_000_000;
    internal const long AbsoluteBudget = 10_000_000_000;
    const string Prefix = "dustweave/ota/all-in-one/";
    static readonly string[] FeedUrls =
    [
        "https://bd2.madsam.work/updates/dustweave/all-in-one/releases.json",
        "https://bd2.madsam.work/updates/dustweave/all-in-one/releases-v2.json",
        "https://bd2.madsam.work/updates/dustweave/all-in-one/releases-v3.json",
        "https://bd2.madsam.work/updates/dustweave/all-in-one/releases-1.6.0.json"
    ];
    internal static async Task<BucketInventory> Inventory(IAmazonS3 s3, string bucket, CancellationToken token)
    {
        var versioning = await s3.GetBucketVersioningAsync(bucket, token);
        Rules.Need(versioning.VersioningConfig.Status == VersionStatus.Off,
            "Bucket versioning is not confirmed off; account for noncurrent versions before uploading or pruning");
        var objects = new List<StoredObject>(); string? continuation = null;
        do
        {
            var response = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket, MaxKeys = 1000, ContinuationToken = continuation }, token);
            objects.AddRange((response.S3Objects ?? []).Select(o => new StoredObject(o.Key, o.Size ?? 0, o.ETag, o.LastModified ?? throw new InvalidDataException("Object timestamp missing"))));
            continuation = response.NextContinuationToken;
            Rules.Need(response.IsTruncated != true || !string.IsNullOrEmpty(continuation), "Incomplete bucket inventory");
            if (response.IsTruncated != true) break;
        } while (true);
        long multipartBytes = 0; string? keyMarker = null, uploadMarker = null;
        do
        {
            var response = await s3.ListMultipartUploadsAsync(new ListMultipartUploadsRequest { BucketName = bucket, KeyMarker = keyMarker, UploadIdMarker = uploadMarker, MaxUploads = 1000 }, token);
            foreach (var upload in response.MultipartUploads ?? [])
            {
                int partMarker = 0;
                do
                {
                    var parts = await s3.ListPartsAsync(new ListPartsRequest { BucketName = bucket, Key = upload.Key, UploadId = upload.UploadId, PartNumberMarker = partMarker.ToString(System.Globalization.CultureInfo.InvariantCulture) }, token);
                    multipartBytes = checked(multipartBytes + (parts.Parts ?? []).Sum(p => p.Size ?? 0));
                    if (parts.IsTruncated != true) break;
                    int next = parts.NextPartNumberMarker ?? 0; Rules.Need(next > partMarker, "Incomplete multipart inventory"); partMarker = next;
                } while (true);
            }
            if (response.IsTruncated != true) break;
            Rules.Need(response.NextKeyMarker != keyMarker || response.NextUploadIdMarker != uploadMarker, "Incomplete upload inventory");
            keyMarker = response.NextKeyMarker; uploadMarker = response.NextUploadIdMarker;
        } while (true);
        Rules.Need(objects.Select(o => o.Key).Distinct().Count() == objects.Count, "Duplicate bucket inventory entries");
        return new(bucket, DateTimeOffset.UtcNow, objects.Sum(o => o.Bytes), multipartBytes, objects.ToArray());
    }
    internal static void RequireBudget(BucketInventory inventory, IEnumerable<(string Key, long Bytes)> incoming)
    {
        var existing = inventory.Objects.ToDictionary(o => o.Key, o => o.Bytes, StringComparer.Ordinal);
        long growth = incoming.Sum(o => existing.ContainsKey(o.Key) ? 0 : o.Bytes);
        Rules.Need(checked(inventory.ObjectBytes + inventory.MultipartBytes + growth) <= UploadCeiling,
            "Bucket upload budget exceeded (8 GB working ceiling, 10 GB total budget). Review and prune unreferenced objects before publishing; never upload first");
    }
    private static async Task<(FeedPin[] Pins, string[] Keep)> Pins(Trust trust, CancellationToken token)
    {
        using var http = Mirror.Client(); var pins = new List<FeedPin>(); var keep = new HashSet<string>(StringComparer.Ordinal);
        var config = new Config("", "", trust.Keys, 3, Capable: true);
        foreach (string url in FeedUrls)
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode == HttpStatusCode.NotFound && url.EndsWith("releases-v3.json")) { pins.Add(new(url, "absent")); continue; }
            Rules.Need(response.StatusCode == HttpStatusCode.OK, "Cannot verify live feed before pruning: " + url);
            using var stream = await response.Content.ReadAsStreamAsync(token); using var memory = new MemoryStream();
            byte[] buffer = new byte[16384]; int count;
            while ((count = await stream.ReadAsync(buffer, token)) != 0)
            { Rules.Need(memory.Length + count <= 1048576, "Live feed exceeds limit"); memory.Write(buffer, 0, count); }
            byte[] bytes = memory.ToArray(); var feed = Rules.VerifyFeed(bytes, config);
            pins.Add(new(url, Rules.Hash(bytes)));
            // The current target and two rollback targets stay online. Frozen
            // transition feeds pin their own heads independently, including 1.6.0.
            foreach (var release in feed.Releases.Take(url.EndsWith("releases-v3.json") || url.EndsWith("releases-v2.json") ? 3 : 1))
                keep.Add(Mirror.ObjectVersion(config, release.Version));
        }
        return (pins.ToArray(), keep.Order(StringComparer.Ordinal).ToArray());
    }
    internal static bool Eligible(StoredObject item, IReadOnlyCollection<string> keep, DateTimeOffset now)
    {
        if (item.ModifiedUtc > now.AddDays(-3)) return false; // downloads already in flight
        if (item.Key.StartsWith(Prefix, StringComparison.Ordinal))
        {
            string relative = item.Key[Prefix.Length..]; var parts = relative.Split('/');
            return parts.Length == 2 && !keep.Contains(parts[0]) &&
                System.Text.RegularExpressions.Regex.IsMatch(parts[0], @"^v\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$") &&
                System.Text.RegularExpressions.Regex.IsMatch(parts[1], @"^Dustweave-[a-zA-Z0-9.-]+-(?:Lite|Portable)-from-[a-zA-Z0-9.-]+-win-x64\.delta\.zip$");
        }
        return item.ModifiedUtc <= now.AddDays(-7) && (item.Key.StartsWith("dustweave-trial/", StringComparison.Ordinal) || item.Key.StartsWith("repair-trial/", StringComparison.Ordinal));
    }
    internal static async Task<RetentionPlan> Plan(IAmazonS3 s3, string bucket, Trust trust, CancellationToken token)
    {
        var inventory = await Inventory(s3, bucket, token); var pins = await Pins(trust, token);
        return new(bucket, DateTimeOffset.UtcNow, inventory.ObjectBytes + inventory.MultipartBytes, pins.Keep, pins.Pins,
            inventory.Objects.Where(o => Eligible(o, pins.Keep, inventory.CheckedUtc)).OrderBy(o => o.Key, StringComparer.Ordinal).ToArray());
    }
    internal static async Task Apply(IAmazonS3 s3, string bucket, Trust trust, RetentionPlan plan, string output, CancellationToken token)
    {
        Rules.Need(plan.Bucket == bucket && plan.CreatedUtc <= DateTimeOffset.UtcNow && plan.CreatedUtc >= DateTimeOffset.UtcNow.AddHours(-24), "Stale or wrong-bucket cleanup plan");
        var pins = await Pins(trust, token);
        Rules.Need(plan.Feeds.SequenceEqual(pins.Pins) && plan.KeepVersions.SequenceEqual(pins.Keep), "Live feeds changed; regenerate the cleanup plan");
        Rules.Need(plan.Delete.Select(o => o.Key).Distinct().Count() == plan.Delete.Length && plan.Delete.All(o => Eligible(o, pins.Keep, plan.CreatedUtc)), "Unsafe cleanup candidate");
        var deleted = new List<StoredObject>(); string state = "interrupted";
        try
        {
            foreach (var item in plan.Delete)
            {
                var head = await s3.GetObjectMetadataAsync(bucket, item.Key, token);
                Rules.Need(head.ETag == item.ETag && head.Headers.ContentLength == item.Bytes && SameHttpTimestamp(head.LastModified, item.ModifiedUtc), "Cleanup candidate changed: " + item.Key);
                await s3.DeleteObjectAsync(bucket, item.Key, token); deleted.Add(item);
                Rules.Atomic(output, Rules.Bytes(new { state = "running", bucket, deleted, bytes = deleted.Sum(o => o.Bytes) }));
            }
            state = "completed";
        }
        finally { Rules.Atomic(output, Rules.Bytes(new { state, bucket, completedUtc = DateTimeOffset.UtcNow, deleted, bytes = deleted.Sum(o => o.Bytes) })); }
    }
    // LIST may contain fractional seconds; HTTP Last-Modified on HEAD cannot.
    // Preserve ETag/length identity and compare both timestamps at HTTP precision.
    static bool SameHttpTimestamp(DateTime? head, DateTimeOffset listed) =>
        head is { } value && new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeSeconds() == listed.ToUnixTimeSeconds();
    internal static void SelfTest()
    {
        var now = DateTimeOffset.UtcNow; var old = new StoredObject(Prefix + "v1.7.0/Dustweave-1.7.0-Lite-from-1.6.9-win-x64.delta.zip", 5, "etag", now.AddDays(-8));
        Rules.Need(Eligible(old, [], now) && !Eligible(old, ["v1.7.0"], now) && !Eligible(old with { ModifiedUtc = now }, [], now), "Retention pin/grace regression");
        foreach (string key in new[] { "accounts/credentials", "dustweave/plugins/packages/tool.zip", "dustweave/ota/v2/repair/objects/a.bin", Prefix + "v1.7.0/nested/file", Prefix + "../file", "unknown.zip" })
            Rules.Need(!Eligible(old with { Key = key }, [], now), "Unowned cleanup path accepted");
        var inventory = new BucketInventory("test", now, UploadCeiling - 10, 0, [old]);
        RequireBudget(inventory, [(old.Key, 5), ("new", 10)]);
        bool rejected = false; try { RequireBudget(inventory with { MultipartBytes = 1 }, [("new", 10)]); } catch (InvalidDataException) { rejected = true; }
        Rules.Need(rejected, "Multipart storage escaped capacity check");
        var listed = DateTimeOffset.Parse("2026-10-20T23:52:24.676285+08:00");
        Rules.Need(SameHttpTimestamp(listed.UtcDateTime.AddTicks(-(listed.Ticks % TimeSpan.TicksPerSecond)), listed)
            && !SameHttpTimestamp(listed.UtcDateTime.AddSeconds(1), listed) && !SameHttpTimestamp(null, listed), "HTTP timestamp precision regression");
    }
}
