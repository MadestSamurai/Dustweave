using Dustweave.Accounts;
using Dustweave;

internal static class SandboxInstallationCases
{
    internal static void Run(string root, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add("sandbox location: " + name); }
        void Reject(Action action, string expected, string name)
        {
            try { action(); } catch (SessionManagerException error) { Check(error.Message == expected, name); return; }
            throw new Exception("Not rejected: " + name);
        }
        string Installation(string name)
        {
            string directory = Path.Combine(root, name); Directory.CreateDirectory(directory);
            foreach (var file in new[] { "Start.exe", "SbieIni.exe", "SbieDll.dll" })
                File.WriteAllText(Path.Combine(directory, file), "synthetic, never executed or loaded");
            return directory;
        }
        string automatic = Installation("automatic sandbox"), selected = Installation("自选 沙盒 path");
        string settings = Path.Combine(root, "sandbox-preferences");
        var store = new SandboxInstallation(settings, () => automatic);
        Check(store.ResolveOptional() == automatic && !store.Read().IsManual, "automatic detection remains the default");
        store.Select(selected);
        Check(new SandboxInstallation(settings, () => automatic).ResolveOptional() == selected, "manual path is persisted and preferred by new instances");
        Reject(() => store.Select("relative"), SandboxInstallation.Invalid, "relative path rejected");
        Reject(() => store.Select(Path.Combine(selected, "Start.exe")), SandboxInstallation.Invalid, "an executable is not an installation directory");
        Check(store.ResolveOptional() == selected, "invalid edits preserve the working choice");
        File.Delete(Path.Combine(selected, "SbieDll.dll"));
        Reject(() => store.ResolveOptional(), SandboxInstallation.Invalid, "incomplete explicit path never silently switches to another launcher");
        Check(store.Read().Directory == selected && store.Read().IsManual, "broken choice remains visible for repair");
        store.UseAutomatic();
        Check(store.ResolveOptional() == automatic && !store.Read().IsManual, "user can return to automatic discovery");
        var absent = new SandboxInstallation(Path.Combine(root, "absent-sandbox"), () => null);
        Check(absent.ResolveOptional() == null && absent.Read().Error == SandboxInstallation.Missing, "missing optional sandbox does not block ordinary launch");
        File.WriteAllText(Path.Combine(settings, "sandbox-installation.json"), "{\"Schema\":99}");
        Reject(() => store.ResolveOptional(), SandboxInstallation.Unreadable, "unknown configuration is actionable");
        store.UseAutomatic(); Check(store.ResolveOptional() == automatic, "reset repairs an unreadable preference");

        // Regression: normal account launch used to call sandbox registration
        // solely because an old instance directory survived uninstall.
        string retained = Path.Combine(root, "retained-sandbox-account");
        Directory.CreateDirectory(retained);
        string retainedData = Path.Combine(retained, "instance.json");
        File.WriteAllText(retainedData, "retained account ownership evidence");
        var calls = new List<string>();
        void CollectForHost(SandboxInstallation installation, string? directory = null) =>
            DailySandboxSessions.CollectForHost(directory ?? retained, installation,
                () => calls.Add("available"), () => calls.Add("collect"), reason => calls.Add("skip:" + reason));
        CollectForHost(absent);
        Check(calls.SequenceEqual(new[] { "available", "skip:" + SandboxInstallation.Missing }),
            "uninstalled sandbox with a retained account directory does not invoke registration or credential export");
        Check(File.ReadAllText(retainedData) == "retained account ownership evidence",
            "ordinary launch preserves retained isolated account data for later recovery");
        calls.Clear();
        CollectForHost(new SandboxInstallation(Path.Combine(root, "never-installed"), () => throw new Exception("Unexpected installation lookup")),
            Path.Combine(root, "unused-account-directory"));
        Check(calls.SequenceEqual(new[] { "available" }), "ordinary accounts with no sandbox history need no installation lookup");
        calls.Clear();
        store.Select(automatic);
        File.Delete(Path.Combine(automatic, "SbieDll.dll"));
        CollectForHost(store);
        Check(calls.SequenceEqual(new[] { "available", "skip:" + SandboxInstallation.Invalid }),
            "uninstall after choosing a custom path does not block ordinary launch");
        Reject(() => store.ResolveOptional(), SandboxInstallation.Invalid,
            "explicit isolated launch still rejects a removed custom installation");
        calls.Clear();
        File.WriteAllText(Path.Combine(settings, "sandbox-installation.json"), "invalid json");
        CollectForHost(store);
        Check(calls.SequenceEqual(new[] { "available", "skip:" + SandboxInstallation.Unreadable }),
            "damaged optional sandbox preferences do not block ordinary account launch");
        Reject(() => store.ResolveOptional(), SandboxInstallation.Unreadable,
            "explicit isolated launch still reports damaged sandbox preferences");
        calls.Clear();
        bool activeAccountRejected = false;
        try
        {
            DailySandboxSessions.CollectForHost(retained, absent,
                () => throw new InvalidOperationException("account is running"), () => calls.Add("collect"), reason => calls.Add("skip"));
        }
        catch (InvalidOperationException error) when (error.Message == "account is running") { activeAccountRejected = true; }
        Check(activeAccountRejected && calls.Count == 0,
            "missing installation cannot bypass the running-account identity guard");
        Installation("automatic sandbox"); store.UseAutomatic();
        calls.Clear(); CollectForHost(store);
        Check(calls.SequenceEqual(new[] { "available", "collect" }),
            "installed sandbox still performs the full credential handoff before ordinary launch");
        bool handoffRejected = false;
        try
        {
            DailySandboxSessions.CollectForHost(retained, store, () => { },
                () => throw new InvalidDataException("account mismatch"), reason => throw new Exception("Unexpected skip"));
        }
        catch (InvalidDataException error) when (error.Message == "account mismatch") { handoffRejected = true; }
        Check(handoffRejected, "installed sandbox handoff failures remain blocking rather than falling back to stale credentials");
    }
}
