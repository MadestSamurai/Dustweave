using Dustweave.Accounts;

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
        Check(store.ResolveScopeDirectory() == automatic, "broken launcher preference does not block native ownership query or settings repair");
        store.UseAutomatic();
        Check(store.ResolveOptional() == automatic && !store.Read().IsManual, "user can return to automatic discovery");
        var absent = new SandboxInstallation(Path.Combine(root, "absent-sandbox"), () => null);
        Check(absent.ResolveOptional() == null && absent.Read().Error == SandboxInstallation.Missing, "missing optional sandbox does not block ordinary launch");
        File.WriteAllText(Path.Combine(settings, "sandbox-installation.json"), "{\"Schema\":99}");
        Reject(() => store.ResolveOptional(), SandboxInstallation.Unreadable, "unknown configuration is actionable");
        Check(store.ResolveScopeDirectory() == automatic, "corrupt preference can still use registered native ownership API");
        store.UseAutomatic(); Check(store.ResolveOptional() == automatic, "reset repairs an unreadable preference");
    }
}
