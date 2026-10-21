using System.Net.Http;

namespace Dustweave;

// An explicitly authorized upgrade using a complete, signed target inventory.
// The repair manifest describes the bytes; the protocol-2 release feed and
// source/target versions authorize the upgrade. Repair alone cannot enable it.
public static class DailyUpdateInventory
{
    public const int Protocol = 2;
    public const string BridgeVersion = "1.4.15-bridge.1";
    public static void Validate(DailyUpdateFeed feed, DailyUpdateRelease release, string fromVersion, string flavor)
    {
        DailyUpdates.Validate(feed);
        var trusted = feed.Releases.SingleOrDefault(r => r.Version == release.Version);
        if (feed.Schema != 4 || trusted == null || trusted.InstallerProtocol != Protocol ||
            release.InstallerProtocol != Protocol || trusted.Layout != DailyUpdates.CurrentLayout ||
            DailyUpdates.VersionOf(fromVersion) < DailyUpdates.VersionOf(BridgeVersion) ||
            DailyUpdates.VersionOf(fromVersion) >= DailyUpdates.VersionOf(trusted.Version) ||
            !feed.Releases.Any(r => r.Version == fromVersion && r.Assets.Any(a => a.Flavor == flavor)) ||
            !trusted.Assets.Any(a => a.Flavor == flavor))
            throw new InvalidDataException("updates.invalid_package");
    }
    public static async Task<DailyRepairPlan> PrepareAsync(DailyUpdateTrust trust, HttpClient client,
        DailyVerifiedUpdate verified, DailyUpdateRelease release, string fromVersion, string flavor,
        string installedDirectory, string root, IProgress<DailyRepairProgress>? progress, CancellationToken token)
    {
        var feed = DailyUpdateSignatures.Verify(verified.Signed, trust);
        Validate(feed, release, fromVersion, flavor);
        var repair = new DailyUpdateRepair(trust);
        var source = await repair.InspectAsync(client, verified, fromVersion, flavor, installedDirectory, root, progress, token);
        var plan = await repair.InspectAsync(client, verified, release.Version, flavor, installedDirectory, root, progress, token);
        DailyUpdates.EnsureNoLinks(SourceManifestPath(plan.Directory));
        File.Copy(DailyUpdateRepair.ManifestPath(source.Directory), SourceManifestPath(plan.Directory), true);
        // Small deltas remain the first choice. They are only an optimization:
        // the signed complete inventory verifies every result and supplies any
        // missing/damaged blocks without another user confirmation.
        var asset = release.Assets.Single(a => a.Flavor == flavor);
        var delta = DailyUpdates.WithBaselines(feed, release).Deltas?.Where(d => d.Flavor == flavor && d.FromVersion == fromVersion &&
            DailyUpdateDeltaPackage.Supports(release, d) && d.Bytes < plan.DownloadBytes).OrderBy(d => d.Bytes).FirstOrDefault();
        if (delta != null)
        {
            try
            {
                string path = Path.Combine(plan.Directory, delta.FileName);
                await new DailyUpdateTransport(trust).DownloadPayload(client, verified, release.Version, root, plan.Directory,
                    delta.FileName, path, delta.Bytes, delta.Sha256, null, token);
                await Task.Run(() => DailyUpdateDeltaPackage.Extract(path, installedDirectory, DailyUpdateRepair.Staging(plan.Directory),
                    release, asset, delta, token, copyUnchanged: false), token);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { DailyUpdateJson.Write(Path.Combine(plan.Directory, "delta-fallback.json"), new { atUtc = DateTimeOffset.UtcNow, error = error.ToString() }); }
        }
        await repair.PrepareAsync(client, plan, root, progress, token);
        return plan;
    }
    public static string SourceManifestPath(string directory) => Path.Combine(directory, "source-repair-manifest.json");
    public static DailyRepairManifest ReadSource(DailyUpdateJob job, DailyUpdateFeed feed)
    {
        ValidateJob(job, feed);
        string path = SourceManifestPath(job.Directory); DailyUpdates.EnsureNoLinks(path);
        if (new FileInfo(path).Length > DailyUpdateRepairManifest.MaxEnvelopeBytes) throw new InvalidDataException("updates.repair_manifest");
        var manifest = DailyUpdateRepairManifest.Verify(File.ReadAllBytes(path), DailyUpdateTrust.Production().Keys);
        var release = feed.Releases.Single(r => r.Version == job.FromVersion);
        DailyUpdateRepair.Bind(manifest, release, release.Assets.Single(a => a.Flavor == job.Asset.Flavor));
        return manifest;
    }
    public static void ValidateSource(DailyUpdateJob job, DailyUpdateFeed feed, DailyUpdatePackage? installed)
    {
        ValidateJob(job, feed);
        if (installed == null || installed.Version != job.FromVersion || installed.Flavor != job.Asset.Flavor ||
            installed.Layout != DailyUpdates.CurrentLayout)
            throw new InvalidDataException("updates.target_changed");
    }
    public static void ValidateJob(DailyUpdateJob job, DailyUpdateFeed feed)
    {
        if (!job.InventoryUpgrade || job.Repair || job.DeltaFileName != null || job.FromVersion == null)
            throw new InvalidDataException("updates.invalid_package");
        Validate(feed, job.Release, job.FromVersion, job.Asset.Flavor);
    }
}
