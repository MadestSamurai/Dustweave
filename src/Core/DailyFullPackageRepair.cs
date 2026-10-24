namespace Dustweave;

// The bundled client uses the same signed archive for updates and repair.
// No per-block inventory, independent repair executable or bridge is required.
public static class DailyFullPackageRepair
{
    public static string PrepareExecutable(string directory, DailyUpdateRelease release, DailyUpdateAsset asset)
    {
        if (release.Layout != DailyUpdates.BundledLayout) throw new InvalidDataException("updates.manual_layout");
        string staging = Path.Combine(directory, "full-helper-" + Guid.NewGuid().ToString("N"));
        DailyUpdates.Extract(Path.Combine(directory, "package.zip"), staging, release, asset);
        return Path.Combine(staging, "Dustweave.exe");
    }
    public static DailyRepairProblem[] Inspect(string directory, string install, DailyUpdateRelease release, DailyUpdateAsset asset, CancellationToken token)
    {
        if (release.Layout != DailyUpdates.BundledLayout) throw new InvalidDataException("updates.manual_layout");
        string staging = Path.Combine(directory, "full-check-" + Guid.NewGuid().ToString("N"));
        DailyUpdates.EnsureNoLinks(staging);
        try
        {
            var names = DailyUpdates.Extract(Path.Combine(directory, "package.zip"), staging, release, asset);
            var problems = new List<DailyRepairProblem>();
            foreach (string name in names)
            {
                token.ThrowIfCancellationRequested();
                string original = Path.Combine(install, name), target = Path.Combine(staging, name);
                DailyUpdates.EnsureNoLinks(original);
                bool missing = !File.Exists(original);
                if (missing || new FileInfo(original).Length != new FileInfo(target).Length || DailyUpdateTransaction.Hash(original) != DailyUpdateTransaction.Hash(target))
                    problems.Add(new(name, missing, new FileInfo(target).Length));
            }
            return problems.ToArray();
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
