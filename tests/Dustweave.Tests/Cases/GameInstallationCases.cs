using Dustweave.Accounts;

internal static class GameInstallationCases
{
    internal static void Run(string root, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add("game location: " + name); }
        void Reject(Action action, string expected, string name)
        {
            try { action(); } catch (SessionManagerException error) { Check(error.Message == expected, name); return; }
            throw new Exception("Not rejected: " + name);
        }
        string Client(string name)
        {
            string folder = Path.Combine(root, name);
            string managed = Path.Combine(folder, "BrownDust II_Data", "Managed"); Directory.CreateDirectory(managed);
            foreach (var file in new[] { "Assembly-CSharp.dll", "mscorlib.dll" }) File.WriteAllText(Path.Combine(managed, file), "synthetic, never loaded");
            string exe = Path.Combine(folder, "BrownDust II.exe"); File.WriteAllText(exe, "synthetic, never launched"); return exe;
        }
        string auto = Client("auto game"), chosen = Client("自选 遊戲 location");
        string settings = Path.Combine(root, "installation-settings");
        int detections = 0;
        var store = new GameInstallation(settings, () => { detections++; return auto; });
        Check(store.Resolve() == auto && !store.Read().IsManual, "automatic discovery remains the default");
        Check(!Directory.Exists(settings), "discovery does not write user settings");
        store.Select(chosen);
        int before = detections;
        Check(store.Resolve() == chosen && store.Read().IsManual && detections == before, "manual choice takes priority without probing another installation");
        Check(new GameInstallation(settings, () => auto).Resolve() == chosen, "choice survives restart and is shared across accounts");
        var saved = File.ReadAllBytes(Path.Combine(settings, "game-installation.json"));
        Reject(() => store.Select(Path.Combine(root, "BD2Starter.exe")), GameInstallation.Invalid, "launcher is rejected");
        Reject(() => store.Select("BrownDust II.exe"), GameInstallation.Invalid, "relative paths are rejected");
        Reject(() => store.Select(Path.Combine(root, "BrownDust II.lnk")), GameInstallation.Invalid, "shortcuts are rejected");
        Reject(() => store.Select(Path.Combine(root, "missing", "BrownDust II.exe")), GameInstallation.Incomplete, "missing files are rejected");
        Check(saved.SequenceEqual(File.ReadAllBytes(Path.Combine(settings, "game-installation.json"))), "invalid choice preserves the working configuration");
        string moved = Path.Combine(root, "moved game"); Directory.Move(Path.GetDirectoryName(chosen)!, moved);
        Check(store.Read().Executable == chosen && store.Read().Error == GameInstallation.Incomplete, "moved game retains the selected path for correction");
        Reject(() => store.Resolve(), GameInstallation.Incomplete, "missing override never silently launches a different copy");
        string corrected = Path.Combine(moved, "BrownDust II.exe"); store.Select(corrected);
        Check(store.Resolve() == corrected, "moved installation can be reselected");
        File.Move(Path.Combine(moved, "BrownDust II_Data", "Managed", "mscorlib.dll"), Path.Combine(moved, "runtime-held"));
        Reject(() => store.Resolve(), GameInstallation.Incomplete, "partially updated client is rejected");
        store.UseAutomatic(); Check(store.Resolve() == auto && !store.Read().IsManual, "automatic detection can be restored");
        string config = Path.Combine(settings, "game-installation.json"); File.WriteAllText(config, "interrupted or corrupt configuration");
        Reject(() => store.Resolve(), GameInstallation.Unreadable, "corrupt config reports an actionable error");
        store.Select(auto); Check(store.Resolve() == auto, "reselection repairs corrupt config");
        File.WriteAllText(config, "{\"Schema\":99,\"Executable\":null}");
        Reject(() => store.Resolve(), GameInstallation.Unreadable, "unknown schema is not silently ignored");
        store.UseAutomatic(); Check(store.Resolve() == auto, "restore repairs an unreadable preference");
        // A concurrent reader that does not share delete must not corrupt or lose the preference.
        var held = new FileStream(config, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(async () => { await Task.Delay(100); held.Dispose(); });
        try { store.Select(auto); } finally { held.Dispose(); release.GetAwaiter().GetResult(); }
        Check(store.Read().IsManual && store.Resolve() == auto, "brief replacement conflict recovers without losing the selected path");
        var beforeLock = File.ReadAllBytes(config);
        using (var locked = new FileStream(config, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool failed = false; var watch = System.Diagnostics.Stopwatch.StartNew();
            try { store.UseAutomatic(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failed = true; }
            Check(failed && watch.Elapsed < TimeSpan.FromSeconds(3), "persistent access failure is bounded and reported");
            Check(beforeLock.SequenceEqual(File.ReadAllBytes(config)), "failed replacement preserves the original settings");
        }
        store.UseAutomatic(); Check(!store.Read().IsManual, "saving works after a persistent lock is released");
        var empty = new GameInstallation(Path.Combine(root, "not-installed"), () => null);
        Check(empty.Read().Executable == "" && empty.Read().Error == GameInstallation.Missing, "new installation has a useful empty state");
        Check(Directory.GetFiles(settings, "*.tmp").Length == 0, "atomic writes leave no pending files");
    }
}
