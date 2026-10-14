using System.Diagnostics;
using Dustweave;

internal static class ToolMenuCases
{
    public static void Run(string root, List<string> cases)
    {
        void Check(bool pass, string name) { if (!pass) throw new Exception(name); cases.Add("tool-menu: " + name); }
        Check(DailyToolCatalog.All.Count == 11, "nine bundled tools plus native mansion and pandora tools present");
        Check(DailyToolCatalog.All.Select(t => t.Id).Distinct().Count() == DailyToolCatalog.All.Count, "unique tool identities");
        Check(DailyToolCatalog.Search("连连看").Single().Id == "sichuan", "Chinese search");
        Check(DailyToolCatalog.Search(" SECRET ").Single().Id == "secret-vision", "case insensitive trimmed search");
        Check(!DailyToolCatalog.Search("not a tool").Any(), "empty search result");
        Check(DailyToolCatalog.Search("潘多拉").Single().Id == "pandora", "Pandora is discoverable in the host tools menu");
        Check(!DailyToolCatalog.All.Any(t => t.Id == "summoner"), "private tools excluded");
        foreach (var tool in DailyToolCatalog.All)
        {
            Check(DailyToolArguments.TryClassify(tool.Id, [], out var helper) && !helper, tool.Id + " normal launch requires interactive operation guard");
            Check(DailyToolArguments.TryClassify(tool.Id, ["--smoke", "output"], out helper) && helper, tool.Id + " offline UI entry recognized");
            Check(!DailyToolArguments.TryClassify(tool.Id, ["--smoke"], out _), tool.Id + " malformed check cannot open normal UI");
            Check(!DailyToolArguments.TryClassify(tool.Id, ["--unknown", "output"], out _), tool.Id + " unknown check rejected");
        }
        Check(!DailyToolArguments.TryClassify("equipment", ["--identity", "output"], out _), "equipment has no identity command");
        Check(!DailyToolArguments.TryClassify("fiend-hunter", ["--runtime-check", "output"], out _), "foreign diagnostic cannot bypass control");
        Check(!DailyToolArguments.TryClassify("sichuan", ["--connection", "self-test"], out _), "equipment helper not routed to other tools");
        Check(DailyToolArguments.TryClassify("equipment", ["--refine"], out var needsHelper) && !needsHelper, "refine opens interactive tool with operation guard");
        Check(DailyToolArguments.TryClassify("equipment", ["--connection", "self-test"], out needsHelper) && needsHelper, "equipment original connection sub-entry works");
        bool rejected = false; try { DailyToolCatalog.Find("../unknown"); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "unknown tool does not launch");
        string directory = Path.Combine(root, "tools-isolated");
        using (DailyToolControl.Acquire(directory))
        {
            rejected = false; try { using var second = DailyToolControl.Acquire(directory); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "daily and tool cannot own control together");
        }
        using (DailyToolControl.Acquire(directory)) Check(true, "normal release allows next operation");
        using var process = Process.GetCurrentProcess();
        var executable = process.MainModule!.FileName!;
        var manager = new DailyToolSession(directory, executable);
        string statePath = Path.Combine(directory, "tools", "window.json");
        Check(manager.Current == null, "missing journal stays idle");
        var state = new DailyToolProcessState("sichuan", process.Id, process.StartTime.ToUniversalTime().Ticks, executable);
        DailyJson.Write(statePath, state);
        Check(manager.Current == state, "reopening menu recognizes original process");
        var foreignScope = new DailyToolSession(directory, executable, containsProcess: _ => false);
        Check(foreignScope.Current == null, "a copied window journal cannot select another sandbox's tool");
        var unknownScope = new DailyToolSession(directory, executable, containsProcess: _ => throw new IOException("scope unavailable"));
        Check(unknownScope.Current == null, "unknown window scope cannot select a process");
        Check(manager.Current == state, "foreign scope lookup leaves original window journal intact");
        DailyJson.Write(statePath, state with { StartTicks = state.StartTicks + 1 });
        Check(manager.Current == null, "reused process id rejected");
        DailyJson.Write(statePath, state with { Executable = executable + ".other" });
        Check(manager.Current == null, "foreign executable rejected");
        DailyJson.Write(statePath, state with { ToolId = "unknown" });
        Check(manager.Current == null, "foreign tool rejected");
        File.WriteAllText(statePath, "{broken");
        Check(manager.Current == null, "corrupt journal cannot target a process");
        File.Delete(statePath);
        CheckHostedProcessSelection(Check);
    }

    private static void CheckHostedProcessSelection(Action<bool,string> check)
    {
        string[] keys = ["BD2_DAILY_HOSTED_TOOL", "BD2_DAILY_GAME_PID", "BD2_DAILY_GAME_START"];
        var saved = keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        using var self = Process.GetCurrentProcess();
        void Reject(Action action, string name)
        {
            bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, name);
        }
        Process? Find(Func<string,bool>? match = null) => BD2.LocalIpc.HostedConnection.FindGame(match ?? (_ => true), "multiple test processes");
        void Descriptor(string? pid, string? start)
        {
            Environment.SetEnvironmentVariable(keys[1], pid);
            Environment.SetEnvironmentVariable(keys[2], start);
        }
        string lifetime = self.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            Environment.SetEnvironmentVariable(keys[0], "territory");
            Descriptor(self.Id.ToString(), lifetime);
            // Every visible process matches. Only the exact parent-bound lifetime
            // may win; a global enumeration would report multiple instances here.
            using (var selected = Find()) check(selected?.Id == self.Id, "hosted tool uses exact parent-bound process despite other visible instances");
            using (var resumed = Find()) check(resumed?.Id == self.Id, "pause and reconnect retain the same hosted target");
            Reject(() => { using var ignored = Find(_ => false); }, "host descriptor cannot select an unrelated executable");
            Descriptor(self.Id.ToString(), (self.StartTime.ToUniversalTime().Ticks + 1).ToString());
            Reject(() => { using var ignored = Find(); }, "stale process lifetime never falls back to a different game");
            foreach (var pid in new[] { null, "bad", "0", "-1", int.MaxValue.ToString() })
            {
                Descriptor(pid, lifetime);
                Reject(() => { using var ignored = Find(); }, "missing or invalid hosted process rejected: " + (pid ?? "missing"));
            }
            foreach (var start in new[] { null, "bad", "0", "-1" })
            {
                Descriptor(self.Id.ToString(), start);
                Reject(() => { using var ignored = Find(); }, "invalid hosted lifetime rejected: " + (start ?? "missing"));
            }
            Environment.SetEnvironmentVariable(keys[0], null);
            using (var missing = Find(_ => false)) check(missing == null, "standalone missing game remains nullable even with a stale host descriptor");
            Reject(() => { using var ignored = Find(); }, "standalone ambiguous process selection remains blocked");
        }
        finally { foreach (var key in keys) Environment.SetEnvironmentVariable(key, saved[key]); }
    }
}
