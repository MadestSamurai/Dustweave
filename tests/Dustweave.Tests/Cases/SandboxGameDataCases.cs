using Dustweave;

static class SandboxGameDataCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); cases.Add(name); }
        string root = Path.Combine(output, "sandbox-tables"), source = Path.Combine(root, "host", "Data", "t"),
            target = Path.Combine(root, "box", "Data", "t"), backup = Path.Combine(root, "recovery");
        Directory.CreateDirectory(source); Directory.CreateDirectory(target);
        string name = new('A', 40), newer = new('B', 40), unknown = new('C', 40);
        DateTime old = DateTime.UtcNow.AddDays(-10), current = old.AddDays(5);
        void Put(string folder, string file, string text, DateTime time)
        { string path = Path.Combine(folder, file); File.WriteAllText(path, text); File.SetLastWriteTimeUtc(path, time); }
        Put(source, ".gamedata_filelist", name + "\n" + newer + "\n../credential\n", current);
        Put(source, name, "new-host-table", current); Put(target, name, "old-box-table", old);
        Put(source, newer, "old-host-hotfix", old); Put(target, newer, "newer-box-hotfix", current);
        Put(source, unknown, "not-in-manifest", current); Put(target, unknown, "unrecognized-overlay", old);
        Put(target, "login.json", "private-login", old);
        Put(Path.GetDirectoryName(target)!, "user-db", "private-user-data", old);
        int checks = 0, reports = 0;
        Task<bool> Idle() { checks++; return Task.FromResult(true); }
        Task<int> Sync(Func<Task<bool>>? idle = null, Action<string>? report = null, CancellationToken token = default) =>
            DailySandboxGameData.SynchronizeAsync(source, target, backup, idle ?? Idle, report ?? (_ => reports++), token);
        Check(await Sync() == 1 && checks == 2 && reports == 1, "old overlay repaired before launch and idle checked again before replacement");
        Check(File.ReadAllText(Path.Combine(target,name)) == "new-host-table", "host design tables replace stale sandbox override");
        Check(File.ReadAllText(Path.Combine(backup,name)) == "old-box-table", "one recoverable previous table is retained outside game lookup");
        Check(File.ReadAllText(Path.Combine(source,name)) == "new-host-table", "host table is never modified");
        Check(File.ReadAllText(Path.Combine(target,newer)) == "newer-box-hotfix", "newer sandbox hot update is not overwritten by older host tables");
        Check(File.ReadAllText(Path.Combine(target,unknown)) == "unrecognized-overlay", "unlisted files are not migrated");
        Check(File.ReadAllText(Path.Combine(target,"login.json")) == "private-login" &&
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(target)!,"user-db")) == "private-user-data", "credentials and writable user databases stay isolated");
        Check(await Sync(() => throw new Exception("unnecessary idle restriction")) == 0, "unchanged tables don't rewrite or restrict an already running box");
        Put(source, name, "next-host-table", current.AddDays(1));
        bool busy = false;
        try { await Sync(() => Task.FromResult(false)); } catch (InvalidOperationException e) when(e.Message == "client_update.sync_busy") { busy = true; }
        Check(busy && File.ReadAllText(Path.Combine(target,name)) == "new-host-table", "running sandbox is not changed beneath the game");
        bool changed = false;
        try { await Sync(report: _ => Put(source, name, "host-changed-during-copy", current.AddDays(2))); }
        catch (IOException e) when(e.Message == "client_update.sync_changed") { changed = true; }
        Check(changed && File.ReadAllText(Path.Combine(target,name)) == "new-host-table", "concurrent host update does not install unverified input");
        Check(!Directory.EnumerateFiles(target, ".dustweave-*").Any(), "interrupted copy leaves no staging files");
        Check(await Sync() == 1 && File.ReadAllText(Path.Combine(backup,name)) == "new-host-table", "retry repairs current source and replaces the single previous backup");
        bool canceled = false;
        try { await Sync(token: new CancellationToken(true)); } catch (OperationCanceledException) { canceled = true; }
        Check(canceled, "cancellation never starts a game table migration");
        Check(!DailySandboxGameData.TableName("../" + name) && !DailySandboxGameData.TableName("Neo") && DailySandboxGameData.TableName(name), "only flat SHA1 game table filenames are accepted");
        File.Delete(Path.Combine(source, ".gamedata_filelist"));
        Check(await Sync() == 0, "absent host download manifest leaves initialization to the game");
    }
}
