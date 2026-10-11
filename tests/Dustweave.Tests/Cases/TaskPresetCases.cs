using Dustweave;

static class TaskPresetCases
{
    public static void Run(string output, List<string> cases)
    {
        string root = Path.Combine(output, "task-presets"), a = new('a', 64), b = new('b', 64);
        var store = new DailyTaskPresetStore(root);
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        void Reject(Action action, string name) { try { action(); } catch (InvalidDataException) { cases.Add(name); return; } throw new Exception(name); }
        var preferences = new DailyPreferences();
        var tasks = DailyStageCatalog.Selectable.Where(s => s.Enabled(preferences)).Take(2).Select(s => s.Id).ToArray();
        Check(tasks.Length == 2 && store.Selection(a, preferences) == null, "unassigned accounts retain their configured daily queue");
        var first = store.Save("Short daily", [tasks[0]]); var second = store.Save("Two tasks", tasks);
        store.Assign(a, first.Id); store.Assign(b, second.Id);
        var restarted = new DailyTaskPresetStore(root);
        Check(restarted.Selection(a, preferences)!.Tasks.SequenceEqual([tasks[0]]) && restarted.Selection(b, preferences)!.Tasks.SequenceEqual(tasks), "account preset identities and distinct selections survive restart");
        Reject(() => store.Save("short DAILY", tasks), "preset names are unique without case sensitivity");
        Reject(() => store.Save("Empty", []), "empty presets cannot silently expand to all tasks");
        Reject(() => store.Save("Unknown", ["invented"]), "unknown task IDs are rejected");
        Reject(() => store.Delete(first.Id), "deletion cannot broaden an assigned unattended queue");
        var renamed = store.Save("Renamed", [tasks[1]], first.Id);
        Check(renamed.Id == first.Id && restarted.Selection(a, preferences)!.Tasks.SequenceEqual([tasks[1]]) && restarted.Selection(b, preferences)!.Tasks.SequenceEqual(tasks), "editing a preset preserves account references and other presets");
        store.Assign(a, null); store.Delete(first.Id);
        Check(store.Selection(a, preferences) == null && store.Read().Presets.Single().Id == second.Id, "unassign and delete restores only the chosen account default");
        var behavior = DailyDesktopBehavior.Read(root);
        Check(behavior.CloseAction == "ask" && !behavior.RunOnOpen, "new users choose close behavior and never auto-run by default");
        behavior = behavior with { CloseAction = "tray", RunOnOpen = true }; behavior.Save(root);
        Check(DailyDesktopBehavior.Read(root) == behavior && behavior.ShouldRun(false, false, false, false, false), "remembered desktop behavior survives restart");
        for (int i = 0; i < 5; i++) { var guards = new bool[5]; guards[i] = true; Check(!behavior.ShouldRun(guards[0], guards[1], guards[2], guards[3], guards[4]), "startup automation avoids duplicate execution guard " + i); }
        Check(!behavior.ShouldRun(false, false, false, false, false, true), "explicit inspect or resume launches cannot trigger an unrelated account queue");
        Reject(() => (behavior with { CloseAction = "invalid" }).Save(root), "corrupt desktop action is not treated as permission to execute");
    }
}
