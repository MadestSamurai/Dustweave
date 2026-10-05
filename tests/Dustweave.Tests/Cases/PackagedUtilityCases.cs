using BD2Daily;
using System.Text.Json.Nodes;
using static BD2Daily.DailyData;

static class PackagedUtilityCases
{
    public static void Run(string output, List<string> cases)
    {
        string package = Path.Combine(output, "tool-layout");
        Directory.CreateDirectory(package);
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        void Reject(Action action, string name)
        {
            bool failed = false;
            try { action(); } catch (Exception e) when (e is InvalidDataException or ArgumentException or FileNotFoundException) { failed = true; }
            Check(failed, name);
        }
        Check(!DailyTools.Available(package, "live"), "absent utility is detected before process start");
        Reject(() => DailyTools.StartInfo(package, "live"), "missing development helper cannot fall through to PATH");
        File.WriteAllText(Path.Combine(package, "BD2DailyAssistant.exe"), "offline-not-executable");
        DailyJson.Write(Path.Combine(package, DailyTools.Marker), O(("protocol", DailyTools.Protocol)));
        foreach (string name in new[] { "live", "minigame", "exporter", "diagnostics" })
        {
            var start = DailyTools.StartInfo(package, name);
            start.ArgumentList.Add("argument with spaces 中文");
            Check(DailyTools.Available(package, name) && start.FileName == Path.Combine(package, "BD2DailyAssistant.exe") && start.WorkingDirectory == package && start.ArgumentList.SequenceEqual(new[] { "--utility", name, "argument with spaces 中文" }) && start.CreateNoWindow && !start.UseShellExecute && start.RedirectStandardError && start.RedirectStandardOutput,
                "unified utility keeps exact arguments and uses isolated hidden process: " + name);
        }
        Check(DailyTools.PackageDirectory(Path.Combine(package, "connection")) == package && DailyTools.EvidenceSpec(package, "test.json") == Path.Combine(package, "connection/specs", "test.json"), "nested connection utility resolves packaged evidence without developer paths");
        Reject(() => DailyTools.StartInfo(package, "unknown"), "unknown utility never launches the desktop UI");
        DailyJson.Write(Path.Combine(package, DailyTools.Marker), O(("protocol", DailyTools.Protocol + 1)));
        Reject(() => DailyTools.Available(package, "live"), "incompatible unified layout fails before connecting");
        DailyJson.Write(Path.Combine(package, DailyTools.Marker), O(("protocol", DailyTools.Protocol)));
        File.Delete(Path.Combine(package, "BD2DailyAssistant.exe"));
        Reject(() => DailyTools.Available(package, "live"), "incomplete unified distribution cannot report available");
    }

    public static async Task Travel(string output, List<string> cases)
    {
        foreach (string mode in new[] { "delay", "never", "stop" })
        {
            using var f = new WorkflowHarness(Path.Combine(output, "travel-readiness-" + mode), []);
            f.Page("GameFieldDefaultUI");
            f.Frame["Surfaces"]![0]!["InputReady"] = false;
            f.Readings = () => [WorkflowCases.Reading("navigation.pack", (DailyTravel.CurrentPack + ".Id", 3), (DailyTravel.CurrentPack + ".PackType", 1))];
            f.OnDelay = () =>
            {
                if (mode == "delay" && f.Time >= 1) f.Frame["Surfaces"]![0]!["InputReady"] = true;
                if (mode == "stop" && f.Time >= .4) f.Stopped = true;
            };
            string outcome = "";
            try { if (await DailyTravel.Reuse(f.Workflow, packId: 3, seconds: 3)) outcome = "ready"; }
            catch (StageHostException e) { outcome = e.Kind; }
            if (outcome != (mode == "delay" ? "ready" : mode == "never" ? "adapter" : "stopped") || f.Box.Commands.Count != 0 || mode == "delay" && f.Time < 1.3)
                throw new Exception("Cartridge readiness regression: " + mode + " => " + outcome);
            cases.Add("current cartridge waits for actual input readiness without re-entering: " + mode);
        }
    }
}
