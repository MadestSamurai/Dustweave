using System.Xml.Linq;
using Dustweave;

internal static class SchedulePathCases
{
    public static void Run(string root, List<string> cases)
    {
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); cases.Add("schedule path: " + name); }
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        const string sid = "S-1-5-21-1000";
        string before = Path.Combine(root, "旧版 & 安装", "Dustweave.exe"), after = Path.Combine(root, "新版 & 安装", "Dustweave.exe");
        string Fixture(string path) => new XDocument(new XElement(ns + "Task",
            new XElement(ns + "RegistrationInfo", new XElement(ns + "Description", "fixture preserved")),
            new XElement(ns + "Triggers", new XElement(ns + "CalendarTrigger", new XElement(ns + "StartBoundary", "2026-10-07T09:15:00"))),
            new XElement(ns + "Principals", new XElement(ns + "Principal", new XElement(ns + "UserId", sid), new XElement(ns + "LogonType", "InteractiveToken"), new XElement(ns + "RunLevel", "LeastPrivilege"))),
            new XElement(ns + "Settings", new XElement(ns + "Enabled", "true"), new XElement(ns + "StartWhenAvailable", "true"), new XElement(ns + "ExecutionTimeLimit", "PT0S")),
            new XElement(ns + "Actions", new XElement(ns + "Exec", new XElement(ns + "Command", path), new XElement(ns + "Arguments", "--scheduled"), new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(path)))))).ToString();
        var plan = new DailySchedulePlan { Enabled = true, Hour = 9, Minute = 15, Accounts = [new string('a', 64)], SavedUtc = DateTimeOffset.UtcNow.AddDays(-1), Execution = new(true, 2, true), TakeOverTools = true };
        var store = new DailyScheduleStore(Path.Combine(root, "launch-path-plan")); store.Save(plan);
        store.Claim(DateTimeOffset.UtcNow.Date, DateTimeOffset.UtcNow);
        var planBytes = File.ReadAllBytes(store.PlanPath);
        string? xml = Fixture(before); int reads = 0, writes = 0;
        bool denyRead = false, denyWrite = false, race = false, discard = false;
        string? Read() { reads++; if (denyRead) throw new UnauthorizedAccessException("fixture read denied"); return xml; }
        void Update(string expected, string replacement)
        {
            if (denyWrite) throw new UnauthorizedAccessException("fixture write denied");
            if (race) { race = false; xml = xml!.Replace("fixture preserved", "changed in Windows"); }
            if (xml != expected) throw new InvalidOperationException("fixture concurrent change");
            writes++; if (!discard) xml = replacement;
        }
        DailySchedulePathResult Repair(DailySchedulePlan? p = null) => DailySchedulePathRepair.Repair(p ?? store.Read(), after, sid, Read, Update);
        string original = xml;
        var moved = Repair();
        Check(moved is { State: "repaired", NeedsAttention: false } && moved.Previous == before && moved.Current == after && writes == 1, "manual move updates existing task once, including Unicode and ampersand paths");
        var expected = XDocument.Parse(original);
        expected.Descendants(ns + "Command").Single().Value = after;
        expected.Descendants(ns + "WorkingDirectory").Single().Value = Path.GetDirectoryName(after)!;
        Check(XNode.DeepEquals(expected, XDocument.Parse(xml!)), "only executable and working directory change; trigger, enabled state and Windows options survive");
        Check(File.ReadAllBytes(store.PlanPath).SequenceEqual(planBytes) && store.IsClaimed(DateTimeOffset.UtcNow.Date), "accounts, saved time, mode and execution claims remain byte-for-byte unchanged");
        Check(Repair().State == "unchanged" && writes == 1, "same-path OTA/restart is a no-op without another success notice");
        Check(DailySchedulePathRepair.Repair(plan, after.ToUpperInvariant(), sid, Read, Update).State == "unchanged", "path casing does not trigger reminders");
        int priorReads = reads;
        Check(Repair(plan with { Enabled = false }).State == "skipped" && reads == priorReads && writes == 1, "disabled saved plan never touches Windows");
        Check(Repair(plan with { Days = [1] }).State == "skipped", "legacy weekly plan remains owned by the existing migration");
        xml = null; Check(Repair() is { State: "missing", NeedsAttention: true } && writes == 1, "missing Windows task is reported rather than recreated implicitly");
        foreach (string disabled in new[] { "false", "0" })
        {
            xml = Fixture(before).Replace("<Enabled>true</Enabled>", "<Enabled>" + disabled + "</Enabled>");
            Check(Repair().State == "disabled" && writes == 1, "Windows disabled task stays disabled: " + disabled);
        }
        foreach (var foreign in new[] { Fixture(before).Replace(sid, "S-1-5-21-2000"), Fixture(before).Replace("--scheduled", "--scheduled --other"), Fixture(before).Replace("InteractiveToken", "Password"), Fixture(before).Replace("Dustweave.exe", "Other.exe") })
        {
            xml = foreign; Check(Repair().State == "review" && xml == foreign && writes == 1, "unrecognized task is preserved with an actionable review");
        }
        xml = Fixture(before); denyRead = true;
        Check(Repair() is { State: "failed", NeedsAttention: true } && writes == 1, "read denial does not disable the user's plan");
        denyRead = false;
        Check(Repair().State == "repaired" && writes == 2, "read access restored then retry actually repairs");
        xml = Fixture(before); denyWrite = true;
        Check(Repair().State == "failed" && xml == Fixture(before), "write denial preserves original task");
        denyWrite = false; Check(Repair().State == "repaired" && writes == 3, "write access restored then retry actually repairs");
        xml = Fixture(before); race = true;
        Check(Repair().State == "failed" && xml.Contains("changed in Windows") && writes == 3, "concurrent Windows edit is not overwritten");
        Check(Repair().State == "repaired" && xml!.Contains("changed in Windows") && writes == 4, "retry preserves concurrent edits while repairing the path");
        xml = Fixture(before); discard = true;
        Check(Repair().State == "failed", "read-back catches a discarded Windows write");
        discard = false; Check(Repair().State == "repaired", "failed read-back can retry without stale permanent block");
        xml = Fixture(before);
        Check(DailySchedulePathRepair.Repair(plan, after, sid, Read, (_, _) => throw new OperationCanceledException()).State == "skipped" && xml == Fixture(before), "shutdown/update cancellation does not modify task or show failure");
        xml = "<!DOCTYPE Task [<!ENTITY x SYSTEM 'file:///unreadable'>]><Task>&x;</Task>";
        Check(Repair().State == "failed", "external XML entities are rejected without reading files");
        xml = Fixture(after).Replace(Path.GetDirectoryName(after)!.Replace("&", "&amp;") + "</WorkingDirectory>", Path.GetDirectoryName(before)!.Replace("&", "&amp;") + "</WorkingDirectory>");
        Check(Repair().State == "repaired", "stale working directory alone is repaired");

        string? autorun = "\"" + before + "\""; int startupWrites = 0;
        var startup = new DailyWindowsStartup(() => autorun, command => { autorun = command; startupWrites++; });
        Check(startup.RefreshEnabledPath(after) && startupWrites == 1 && autorun == "\"" + after + "\"", "existing Windows startup follows the manual move");
        Check(!startup.RefreshEnabledPath(after) && startupWrites == 1, "same-path OTA does not rewrite Windows startup");
        autorun = null;
        Check(!startup.RefreshEnabledPath(after) && startupWrites == 1, "disabled Windows startup stays off");
        Check(File.ReadAllBytes(store.PlanPath).SequenceEqual(planBytes), "all failure and recovery paths leave the saved plan intact");
    }
}
