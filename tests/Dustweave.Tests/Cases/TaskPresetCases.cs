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
        {
            string legacyRoot = Path.Combine(output, "retired-presets"), oldId = Guid.NewGuid().ToString("N"), onlyId = Guid.NewGuid().ToString("N");
            DailyJson.Write(Path.Combine(legacyRoot, "task-presets.json"), new DailyTaskPresetData(1,
                [new(oldId, "Old daily", ["monster_hunt", "mail"]), new(onlyId, "Old boss only", ["monster_hunt"])], new() { [a] = oldId, [b] = onlyId }));
            var legacyStore = new DailyTaskPresetStore(legacyRoot);
            Check(legacyStore.Selection(a, preferences)!.Tasks.SequenceEqual(["mail"]), "legacy presets retain remaining tasks after Fiend Hunt removal");
            Reject(() => legacyStore.Selection(b, preferences), "retired-only preset never expands to all daily tasks");
            Check(!DailyStageCatalog.Selectable.Any(s => s.Id == "monster_hunt"), "removed Fiend Hunt is absent from task selection");
        }
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
        string? startupValue=null;int startupWrites=0;
        var startup=new DailyWindowsStartup(()=>startupValue,value=>{startupValue=value;startupWrites++;});
        startup.RefreshEnabledPath(@"C:\Apps\Dustweave\Dustweave.exe");
        Check(!startup.Enabled && startupWrites==0,"Windows startup remains opt-in on first run");
        startup.Set(true,@"C:\Apps\织尘 Portable\Dustweave.exe");
        Check(startup.Enabled && startupValue=="\"C:\\Apps\\织尘 Portable\\Dustweave.exe\"","startup quotes paths with spaces and Unicode without enabling a daily queue");
        startup.RefreshEnabledPath(@"D:\Apps\Dustweave.exe");
        Check(startupValue=="\"D:\\Apps\\Dustweave.exe\"","existing startup registration follows a moved portable installation");
        Reject(()=>startup.Set(true,"Dustweave.exe"),"relative startup executables rejected");
        Reject(()=>startup.Set(true,"C:\\App\\Dustweave.exe\" --other"),"startup command injection rejected");
        startup.Set(false,"");Check(!startup.Enabled && startupValue==null,"startup opt-out removes only the owned registration");
        var deniedStartup=new DailyWindowsStartup(()=>null,_=>throw new UnauthorizedAccessException());
        try { deniedStartup.Set(true,@"C:\App\Dustweave.exe");throw new Exception("Registration failure swallowed"); } catch(UnauthorizedAccessException) { cases.Add("startup failure propagates for actionable UI feedback"); }
    }
}
