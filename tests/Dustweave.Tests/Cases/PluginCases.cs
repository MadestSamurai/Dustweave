using Dustweave;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
static class PluginCases
{
    public static void Run(string output, List<string> cases)
    {
        var root = Path.Combine(output, "plugin-fixtures");
        Directory.CreateDirectory(root);
        void Check(bool value, string label)
        {
            if (!value) throw new Exception(label);
            cases.Add("plugin: " + label);
        }
        string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
        JsonObject Manifest()
        {
            var files = new JsonArray();
            foreach (string path in new[] { "hook/Extension.cs", "managed/Example.Extension.dll", "data/config.json" })
            {
                string file = Path.Combine(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, path == "hook/Extension.cs" ? "// synthetic fixture" : "fixture payload");
                files.Add(new JsonObject { ["path"] = path, ["sha256"] = Hash(file) });
            }
            return new JsonObject { ["id"] = "example.extension", ["version"] = "1.0.0", ["apiVersion"] = DailyPlugin.ApiVersion, ["bridgeExtensionApi"] = 1, ["runtime"] = "net8.0-windows-x64", ["entryAssembly"] = "managed/Example.Extension.dll", ["entryType"] = "Example.Extension", ["capabilities"] = new JsonArray("sample_task"), ["hookSources"] = new JsonArray("hook/Extension.cs"), ["files"] = files };
        }
        void Write(JsonObject m) => File.WriteAllText(Path.Combine(root, "plugin.json"), m.ToJsonString());
        Check(!DailyPlugin.Inspect(Path.Combine(root, "missing")).Available, "absence leaves base available");
        Write(Manifest());
        var ready = DailyPlugin.Inspect(root);
        Check(ready.Available && ready.HookSources.Single() == "// synthetic fixture", "validates complete inventory");
        Check(ready.Fingerprint == Hash(Path.Combine(root, "plugin.json")), "manifest binds all payloads");
        Check(ready.Supports("sample_task") && !ready.Supports("other_task"), "only declared tasks are available");
        foreach (var key in new[] { "apiVersion", "bridgeExtensionApi" })
        {
            var m = Manifest(); m[key] = 99; Write(m);
            Check(!DailyPlugin.Inspect(root).Available, key + " mismatch unavailable");
        }
        {
            var m = Manifest(); m["apiVersion"] = 3; Write(m);
            Check(!DailyPlugin.Inspect(root).Available, "pre-rename managed plugin is rejected before loading its old assembly references");
        }
        foreach (var pair in new[] { ("runtime", "unknown"), ("id", ""), ("entryAssembly", "../outside.dll"), ("entryAssembly", "managed/Missing.dll"), ("entryType", "") })
        {
            var m = Manifest(); m[pair.Item1] = pair.Item2; Write(m);
            Check(!DailyPlugin.Inspect(root).Available, pair.Item1 + " invalid value unavailable");
        }
        foreach (var capabilities in new[] { new JsonArray(), new JsonArray("sample", "sample"), new JsonArray("../invalid"), new JsonArray((JsonNode?)null) })
        {
            var m = Manifest(); m["capabilities"] = capabilities; Write(m);
            Check(!DailyPlugin.Inspect(root).Available, "invalid task declaration rejected");
        }
        foreach (var hooks in new[] { new JsonArray("hook/Missing.cs"), new JsonArray("hook/Extension.cs", "hook/Extension.cs"), new JsonArray((JsonNode?)null) })
        {
            var m = Manifest(); m["hookSources"] = hooks; Write(m);
            Check(!DailyPlugin.Inspect(root).Available, "invalid hook declaration rejected");
        }
        {
            var m = Manifest(); m["files"]![0]!["path"] = "../outside.cs"; Write(m);
            Check(!DailyPlugin.Inspect(root).Available, "traversal rejected");
        }
        {
            var m = Manifest(); m["files"]!.AsArray().Add(m["files"]![0]!.DeepClone()); Write(m);
            Check(!DailyPlugin.Inspect(root).Available, "duplicate inventory rejected");
        }
        {
            var m = Manifest(); m["files"]!.AsArray().RemoveAt(1); Write(m);
            Check(!DailyPlugin.Inspect(root).Available, "unlisted entry assembly rejected");
        }
        Write(Manifest());
        File.WriteAllText(Path.Combine(root, "hook", "Extension.cs"), "tampered");
        Check(!DailyPlugin.Inspect(root).Available, "modified payload unavailable");
        bool refused = false;
        try { DailyExtensionLoader.Load(ready); } catch (StageHostException) { refused = true; }
        Check(refused, "load rechecks inventory even when prior inspection passed");
        File.WriteAllText(Path.Combine(root, "plugin.json"), "{");
        Check(!DailyPlugin.Inspect(root).Available, "malformed plugin does not break base");
        Check(DailyExtensionLoader.Load(DailyPlugin.Inspect(root)) == null, "invalid plugin cannot be loaded");
        var preferences = new DailyPreferences(); preferences.Tactics.Enabled = true;
        Check(!DailyStageCatalog.All.Single(s => s.Id == "tactics").Enabled(preferences), "saved opt-in cannot schedule unavailable task");
        Check(!DailyStageCatalog.Selectable.Any(s => s.Id == "tactics"), "unavailable task is not selectable");
        Check(DailyStageCatalog.Selectable.Any(s => s.Id == "event_battle"), "built-in task remains selectable");
    }
}
