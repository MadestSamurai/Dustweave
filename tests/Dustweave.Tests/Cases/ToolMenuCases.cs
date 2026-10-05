using System.Diagnostics;
using BD2Daily;

internal static class ToolMenuCases
{
    public static void Run(string root, List<string> cases)
    {
        void Check(bool pass, string name) { if (!pass) throw new Exception(name); cases.Add("tool-menu: " + name); }
        Check(DailyToolCatalog.All.Count == 9, "all nine public tools present");
        Check(DailyToolCatalog.All.Select(t => t.Id).Distinct().Count() == 9, "unique tool identities");
        Check(DailyToolCatalog.Search("连连看").Single().Id == "sichuan", "Chinese search");
        Check(DailyToolCatalog.Search(" SECRET ").Single().Id == "secret-vision", "case insensitive trimmed search");
        Check(!DailyToolCatalog.Search("not a tool").Any(), "empty search result");
        Check(!DailyToolCatalog.All.Any(t => t.Id is "pandora" or "summoner"), "private tools excluded");
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
        DailyJson.Write(statePath, state with { StartTicks = state.StartTicks + 1 });
        Check(manager.Current == null, "reused process id rejected");
        DailyJson.Write(statePath, state with { Executable = executable + ".other" });
        Check(manager.Current == null, "foreign executable rejected");
        DailyJson.Write(statePath, state with { ToolId = "unknown" });
        Check(manager.Current == null, "foreign tool rejected");
        File.WriteAllText(statePath, "{broken");
        Check(manager.Current == null, "corrupt journal cannot target a process");
        File.Delete(statePath);
    }
}

