using System.Diagnostics;
using Dustweave;

internal static class StoragePathCases
{
    // An actual Windows junction reproduces redirected AppData without touching
    // the user's directories, registry or game.
    internal static string Junction(string path, string target)
    {
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["DUSTWEAVE_TEST_LINK"] = path;
        start.Environment["DUSTWEAVE_TEST_TARGET"] = target;
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path $env:DUSTWEAVE_TEST_LINK -Target $env:DUSTWEAVE_TEST_TARGET | Out-Null" }) start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
        if (!child.WaitForExit(15000)) throw new Exception("Fixture junction creation did not finish.");
        if (child.ExitCode != 0) throw new Exception(stderr.GetAwaiter().GetResult());
        return path;
    }
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); cases.Add("storage-path: " + label); }
        string basis = Path.Combine(output, "redirected-storage"), physical = Path.Combine(basis, "physical"), alias = Path.Combine(basis, "alias");
        Directory.CreateDirectory(physical);
        Junction(alias, physical);
        string root = Path.Combine(physical, "data"), redirected = Path.Combine(alias, "data");
        Directory.CreateDirectory(root);
        Check(DailyStoragePath.ResolveRoot(redirected) == root, "parent junction resolves to physical data without creating a new store");
        string nested = Junction(Path.Combine(physical, "nested"), root);
        Check(DailyStoragePath.ResolveRoot(Path.Combine(alias, "nested", "new")) == Path.Combine(root, "new"), "nested junction and not-yet-created cache path resolve correctly");
        using (DailyCacheStorage.Lease(redirected))
        using (DailyCacheStorage.Lease(root))
        {
            bool blocked = false; try { using var exclusive = DailyCacheStorage.Lease(root, true); } catch (IOException) { blocked = true; }
            Check(blocked, "aliases share the same storage lock");
        }
        using (DailyCacheStorage.Lease(redirected, true))
        {
            bool blocked = false; try { using var reader = DailyCacheStorage.Lease(root); } catch (IOException) { blocked = true; }
            Check(blocked, "exclusive cleanup blocks reads through the physical path");
        }
        using (DailyToolControl.Acquire(redirected))
        {
            bool blocked = false; try { using var maintenance = DailyToolControl.AcquireMaintenance(root); } catch (InvalidOperationException) { blocked = true; }
            Check(blocked, "alias resolution cannot bypass an active automation control lock");
        }
        Directory.CreateDirectory(Path.Combine(root, "accounts"));
        string account = Path.Combine(root, "accounts", "keep.json"); File.WriteAllText(account, "synthetic-durable");
        foreach (char version in "abc")
        {
            string folder = Path.Combine(root, "suite-cache", new string(version, 64)); Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "observer.dll"); File.WriteAllText(file, "fixture"); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-5 + version - 'a'));
        }
        var plan = await DailyCacheStorage.ScanAsync(redirected);
        Check(plan.AllFiles.Any(f => f.Path == account) && plan.Units.Count(u => u.Path.Contains("suite-cache")) == 1, "scan counts durable data and plans only obsolete cache");
        var clean = await DailyCacheStorage.CleanAsync(redirected);
        Check(clean.Bytes > 0 && File.Exists(account), "alias cleanup reclaims cache and preserves accounts");
        var overview = await new DailyStorageMaintenance(redirected, null, null, _ => false, () => false).ScanAsync(new());
        Check(overview.TotalBytes > 0, "combined storage and log scan accepts redirected roots");
        string outside = Path.Combine(basis, "outside"); Directory.CreateDirectory(outside);
        string keep = Path.Combine(outside, "keep.txt"); File.WriteAllText(keep, "unrelated");
        string childLink = Junction(Path.Combine(root, "suite-cache", "outside"), outside);
        var protectedPlan = await DailyCacheStorage.ScanAsync(redirected);
        Check(protectedPlan.Skipped > 0 && !protectedPlan.AllFiles.Any(f => f.Path.Contains("keep.txt")), "nested cache links are not traversed or counted as owned files");
        await DailyCacheStorage.CleanAsync(redirected);
        Check(File.ReadAllText(keep) == "unrelated", "cleanup cannot delete a linked external target");
        bool updateBlocked = false;
        try { DailyUpdates.EnsureNoLinks(Path.Combine(childLink, "keep.txt")); }
        catch (InvalidDataException error) { updateBlocked = error.Message.Contains(childLink); }
        Check(updateBlocked, "update descendant link still blocked with the specific path");
        string brokenTarget = Path.Combine(basis, "vanished"); Directory.CreateDirectory(brokenTarget);
        string broken = Junction(Path.Combine(basis, "broken"), brokenTarget);
        Directory.Delete(brokenTarget); // Exact empty fixture directory; no recursive deletion.
        bool unavailable = false; try { DailyStoragePath.ResolveRoot(broken); } catch (IOException error) { unavailable = error.Message.Contains(broken); }
        Check(unavailable, "broken redirection reports its offending path");
        bool overlap = false; try { await DailyCacheStorage.MoveAsync(redirected, Path.Combine(root, "other")); } catch (IOException) { overlap = true; }
        Check(overlap, "cache migration detects overlaps across aliases");
        // Remove the fixture links themselves so later test compaction cannot
        // follow them; retain all real fixture directories as test evidence.
        Directory.Delete(childLink);
        string targetParent = Path.Combine(basis, "target"); Directory.CreateDirectory(targetParent);
        string targetAlias = Junction(Path.Combine(basis, "target-alias"), targetParent);
        await DailyCacheStorage.MoveAsync(redirected, Path.Combine(targetAlias, "new-cache"));
        Check(DailyCacheStorage.Location(redirected) == Path.Combine(targetParent, "new-cache") && File.Exists(account),
            "migration through redirected parent stores a physical cache location without moving accounts");
        foreach (string link in new[] { broken, nested, alias, targetAlias }) Directory.Delete(link);
    }
}
