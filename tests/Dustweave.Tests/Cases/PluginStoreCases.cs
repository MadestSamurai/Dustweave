using System.IO.Compression;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Dustweave;

public sealed class SyntheticPlugin(string root) : IDailyExtension
{
    public int ApiVersion => DailyPlugin.ApiVersion;
    public DailyBusinessProof[] ExtendProofs(DailyBusinessProof[] current) => current;
    public Task<JsonObject> ExecuteAsync(string stage, DailyWorkflow workflow) => Task.FromResult(new JsonObject { ["synthetic"] = true, ["root"] = root });
    public bool CanResume(string root, string stage, DailyStageFrame frame) => true;
}

internal static class PluginStoreCases
{
    public static async Task Run(string output, List<string> cases)
    {
        var root = Path.Combine(output, "plugin-store"); Directory.CreateDirectory(root);
        var store = new DailyPluginStore(Path.Combine(root, "user"));
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); cases.Add("plugin-store: " + label); }
        string Package(string version, Action<JsonObject>? edit = null, string? extra = null)
        {
            string folder = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(folder, "managed")); Directory.CreateDirectory(Path.Combine(folder, "hook"));
            File.Copy(typeof(SyntheticPlugin).Assembly.Location, Path.Combine(folder, "managed", "Sample.dll"));
            File.WriteAllText(Path.Combine(folder, "hook", "Sample.cs"), "// synthetic");
            var files = new JsonArray();
            foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) files.Add(new JsonObject { ["path"] = Path.GetRelativePath(folder, file).Replace('\\', '/'), ["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) });
            var manifest = new JsonObject { ["id"] = "example.test", ["version"] = version, ["runtime"] = "net8.0-windows-x64", ["apiVersion"] = 4, ["bridgeExtensionApi"] = 1, ["minHostVersion"] = "0.9.12", ["maxHostVersion"] = DailyPlugin.HostVersion, ["names"] = new JsonObject { ["zh-CN"] = "示例扩展", ["en-US"] = "Example" }, ["publisher"] = "Example", ["entryAssembly"] = "managed/Sample.dll", ["entryType"] = "SyntheticPlugin", ["capabilities"] = new JsonArray("sample_task"), ["hookSources"] = new JsonArray("hook/Sample.cs"), ["files"] = files };
            edit?.Invoke(manifest); File.WriteAllText(Path.Combine(folder, "plugin.json"), manifest.ToJsonString());
            string zip = folder + ".zip"; ZipFile.CreateFromDirectory(folder, zip);
            if (extra != null) { using var archive = ZipFile.Open(zip, ZipArchiveMode.Update); using var writer = new StreamWriter(archive.CreateEntry(extra).Open()); writer.Write("unlisted"); }
            return zip;
        }
        string? UpdateBlock(string version, string? environment = null)
        {
            string? previous = Environment.GetEnvironmentVariable("DUSTWEAVE_PLUGIN");
            try
            {
                Environment.SetEnvironmentVariable("DUSTWEAVE_PLUGIN", environment);
                return DailyPlugin.HostUpdateBlockReason(Path.Combine(root, "user"), version);
            }
            finally { Environment.SetEnvironmentVariable("DUSTWEAVE_PLUGIN", previous); }
        }
        Check(!DailyPluginStore.HasSelection(Path.Combine(root, "user")), "empty store preserves explicit local installation");
        Check(typeof(DailyPlugin).GetMethod(nameof(DailyPlugin.ResolveRoot), Type.EmptyTypes) != null, "parameterless plugin API stays binary compatible");
        Check(UpdateBlock("99.0.0") == null, "fresh no-plugin user can update with the environment override absent");
        Check(UpdateBlock("99.0.0", Path.Combine(root, "missing-extension")) == null, "nonempty missing fallback directory cannot block a host update");
        string empty = Path.Combine(root, "empty-extension"); Directory.CreateDirectory(empty);
        Check(UpdateBlock("99.0.0", empty) == null, "empty plugin directory is not an active incompatible plugin");
        var firstZip = Package("1.0.0");
        using var prepared = store.Prepare(firstZip);
        DailyInstalledPlugin first;
        using (var held = new FileStream(Path.Combine(prepared.Info.Root, "managed/Sample.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var install = Task.Run(() => store.Install(prepared));
            await Task.Delay(150);
            held.Dispose();
            first = await install;
        }
        Check(store.Inspect(first).Available, "transient extracted-file lock does not interrupt verified installation");
        Check(store.Read().Active == null && !store.Read().OverrideLocal, "import alone does not execute or disable local plugin");
        Check(store.Inspect(first).Name("zh-TW") == "Example", "plugin-owned localized metadata uses fallback");
        string nextHost = (int.Parse(DailyPlugin.HostVersion.Split('.')[0]) + 1) + ".0.0";
        Check(!store.Inspect(first, nextHost).Available, "host update compatibility rejected before loading");
        await DailyPluginProbe.RunAsync(Environment.ProcessPath!, Path.Combine(root, "probe"), store.Inspect(first));
        Check(true, "child process loads actual extension and reports matching fingerprint");
        string? priorProbe = Environment.GetEnvironmentVariable("DUSTWEAVE_TEST_PROBE");
        try
        {
            foreach (string behavior in new[] { "hang", "fail" })
            {
                Environment.SetEnvironmentVariable("DUSTWEAVE_TEST_PROBE", behavior);
                bool rejected = false;
                try { await DailyPluginProbe.RunAsync(Environment.ProcessPath!, Path.Combine(root, "probe"), store.Inspect(first), limit: TimeSpan.FromMilliseconds(1500)); }
                catch (IOException e) { rejected = e.Message == (behavior == "hang" ? "plugins.probe_timeout" : "plugins.probe_failed"); }
                Check(rejected, "isolated load " + behavior + " remains bounded without stopping the host");
            }
        }
        finally { Environment.SetEnvironmentVariable("DUSTWEAVE_TEST_PROBE", priorProbe); }
        int probes = 0;
        Task Probe(DailyPluginInfo info, CancellationToken token)
        {
            var extension = DailyExtensionLoader.Load(info)!; probes++;
            Check(extension.ApiVersion == 4, "real managed fixture shares API identity");
            Check(AssemblyLoadContext.GetLoadContext(extension.GetType().Assembly) != AssemblyLoadContext.Default, "plugin uses separate load context");
            Check(extension.ExtendProofs([]).Length == 0, "real extension method works without game input");
            return Task.CompletedTask;
        }
        await store.ActivateAsync(first.Fingerprint, Probe);
        Check(store.Read().Active == first.Fingerprint && store.SelectedRoot() == store.PathFor(first.Fingerprint), "activation selects immutable payload");
        Check(UpdateBlock(DailyPlugin.HostVersion) == null, "compatible active plugin permits host update");
        Check(UpdateBlock(nextHost) == "plugins.incompatible_host", "actual enabled incompatible plugin still blocks host update");
        Check(UpdateBlock(nextHost, "none") == null, "explicit no-plugin mode overrides installed plugin during host update");
        string pinned = store.SelectedRoot();
        using var preparedTwo = store.Prepare(Package("1.1.0")); var second = store.Install(preparedTwo);
        bool failed = false;
        try { await store.ActivateAsync(second.Fingerprint, (_, _) => throw new IOException("probe failed")); } catch (IOException) { failed = true; }
        Check(failed && store.Read().Active == first.Fingerprint, "failed probe preserves active version");
        await store.ActivateAsync(second.Fingerprint, Probe);
        Check(store.Read().Active == second.Fingerprint && store.Read().Previous == first.Fingerprint, "update keeps rollback target");
        Check(DailyPlugin.Inspect(pinned).Fingerprint == first.Fingerprint, "in-flight queue's old root remains intact after update");
        await store.ActivateAsync(store.Read().Previous!, Probe);
        Check(store.Read().Active == first.Fingerprint, "rollback performs validated activation");
        store.Disable(); Check(store.SelectedRoot() == "" && store.Read().OverrideLocal, "disable cannot fall back to local plugin");
        Check(UpdateBlock(nextHost) == null, "disabled plugin permits host update even though its files remain installed");
        await store.ActivateAsync(first.Fingerprint, Probe);
        store.Remove(first.Fingerprint);
        Check(store.Read().Active == null && Directory.Exists(pinned), "removal disables next start without deleting in-use files");
        using var repeat = store.Prepare(firstZip); store.Install(repeat);
        Check(store.Read().Installed.Length == 2, "removed immutable version can be reinstalled");
        using var repeated = store.Prepare(firstZip); store.Install(repeated);
        Check(store.Read().Installed.Length == 2, "duplicate import is idempotent");
        foreach (var extra in new[] { "../escape", "/absolute", "managed/Sample.dll:evil", "managed/CON.txt", "managed/Sample.dll ", "unexpected.txt" })
        {
            bool rejected = false; try { using var p = store.Prepare(Package("2.0.0", extra: extra)); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "rejects unsafe or unlisted entry: " + extra);
        }
        foreach (var edit in new Action<JsonObject>[] { m => m["apiVersion"] = 99, m => m["minHostVersion"] = "99.0.0", m => m["files"]![0]!["sha256"] = new string('0', 64), m => m["id"] = "../outside", m => m["version"] = "banana" })
        {
            bool rejected = false; try { using var p = store.Prepare(Package("2.0.0", edit)); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "invalid package rejected without changing selection");
        }
        await store.ActivateAsync(second.Fingerprint, Probe);
        bool changed = false;
        try { await store.ActivateAsync(first.Fingerprint, (_, _) => { store.Disable(); return Task.CompletedTask; }); } catch (InvalidDataException) { changed = true; }
        Check(changed && store.Read().Active == null, "concurrent selection change does not overwrite latest choice");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        bool cancelled = false; try { await store.ActivateAsync(first.Fingerprint, Probe, cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && store.Read().Active == null, "cancelled activation preserves selection");
        await store.ActivateAsync(first.Fingerprint, Probe);
        string installedManifest = Path.Combine(store.PathFor(first.Fingerprint), "plugin.json");
        var replacedManifest = JsonNode.Parse(File.ReadAllText(installedManifest))!; replacedManifest["publisher"] = "Changed after approval";
        File.WriteAllText(installedManifest, replacedManifest.ToJsonString());
        Check(store.SelectedRoot() == "", "changed manifest cannot bypass the approved fingerprint at startup");
        Check(UpdateBlock(nextHost) == null, "unloadable altered extension does not hold the base app update hostage");
        string selectionFile = Path.Combine(root, "user", "extensions", "selection.json");
        var invalidState = JsonNode.Parse(File.ReadAllText(selectionFile))!; invalidState["Installed"]![0]!["Id"] = null;
        File.WriteAllText(selectionFile, invalidState.ToJsonString());
        Check(store.SelectedRoot() == "", "null identity in a corrupt registry cannot crash base startup");
        File.WriteAllText(Path.Combine(root, "user", "extensions", "selection.json"), "{");
        Check(store.SelectedRoot() == "" && DailyPluginStore.HasSelection(Path.Combine(root, "user")), "corrupt registry fails closed without reviving local plugin");
    }
}
