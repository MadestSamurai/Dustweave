using System.Security.Cryptography;
using System.Text.Json;

namespace Dustweave;

// A C experiment changes only program overlays. Login storage, LocalLow and the
// registry are never copied, removed or restored by this transaction. Every move
// is a same-volume rename, preceded by a flushed intent, and remains recoverable
// even when the collector, launcher or machine stops between two instructions.
internal static class DailyLoadingFileTransaction
{
    internal static readonly string[] Branches = ["BrownDust II.exe", "UnityPlayer.dll", "UnityCrashHandler64.exe", "BrownDust II_Data", "MonoBleedingEdge"];
    internal sealed class Entry
    {
        public string Name { get; set; } = "";
        public bool Existed { get; set; }
        public string Stage { get; set; } = "planned";
    }
    internal sealed class Journal
    {
        public int Schema { get; set; } = 1;
        public string Id { get; set; } = "";
        public string Box { get; set; } = "";
        public string BoxRoot { get; set; } = "";
        public string Executable { get; set; } = "";
        public string Output { get; set; } = "";
        public string State { get; set; } = "preparing";
        public int GameProcessId { get; set; }
        public long GameStartTicks { get; set; }
        public Dictionary<string,string> HostFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string[] OriginalRules { get; set; } = [];
        public string[] ChangedRules { get; set; } = [];
        public List<Entry> Entries { get; set; } = [];
    }
    internal static string Pending(string root) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(root))!, "loading-experiment-recovery", "pending.json");
    static string Backup(Journal j, string phase, string name) => Path.Combine(Path.GetDirectoryName(Pending(j.BoxRoot))!, j.Id, phase, name);
    internal static string Overlay(string root, string executable)
    {
        _ = DailySandboxGameFiles.LegacyRules(executable);
        string host = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        if (Path.GetPathRoot(host)?.Length != 3) throw new IOException("C 组需要本机磁盘上的游戏目录，不能使用网络共享目录。");
        return Path.Combine(Path.GetFullPath(root), "drive", host[..1].ToLowerInvariant(), host[3..]);
    }
    static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    static FileStream Acquire(string root)
    {
        string path=Path.Combine(Path.GetDirectoryName(Pending(root))!,"transaction.lock");
        DailyCacheStorage.NoLinks(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    }
    static void Move(string source, string destination)
    {
        // Resolve and verify BOTH locations before every directory move. Neither
        // argument is taken from an unvalidated journal path or shell expression.
        source = Path.GetFullPath(source); destination = Path.GetFullPath(destination);
        DailyCacheStorage.NoLinks(source); DailyCacheStorage.NoLinks(destination);
        if (!Path.GetPathRoot(source)!.Equals(Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase)) throw new IOException("实验保全目录必须与沙盒位于同一磁盘。");
        if (Exists(destination)) throw new IOException("实验保全位置已存在；原文件保持不变：" + destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (Directory.Exists(source)) Directory.Move(source, destination); else File.Move(source, destination);
    }
    static void Atomic(string file, object value)
    {
        DailyCacheStorage.NoLinks(file); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temp = file + ".writing";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { JsonSerializer.Serialize(stream, value, DailyJson.Options); stream.Flush(true); }
        File.Move(temp, file, true);
    }
    static void Save(Journal j)
    {
        // Local durable recovery is authoritative; failure of the user's output
        // disk must not prevent restoring their original program overlays.
        Atomic(Pending(j.BoxRoot), j);
        try { Atomic(Path.Combine(j.Output, "legacy-files-" + j.Box + ".json"), j); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    internal static Journal? Read(string root)
    {
        string file = Pending(root); if (!File.Exists(file)) return null;
        DailyCacheStorage.NoLinks(file);
        var j = JsonSerializer.Deserialize<Journal>(File.ReadAllText(file), DailyJson.Options) ?? throw new IOException("C 组恢复记录无法读取；原始文件仍在保全目录。");
        if (j.Schema != 1 || !Guid.TryParseExact(j.Id, "N", out _) || !DailySandbox.ValidBoxName(j.Box)
            || !Path.GetFullPath(j.BoxRoot).Equals(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)
            || !Path.IsPathFullyQualified(j.Output) || j.Entries.Count != Branches.Length
            || j.State is not ("preparing" or "rules-pending" or "prepared" or "restoring" or "restored")
            || j.Entries.Any(e => e.Stage is not ("planned" or "move-pending" or "prepared" or "during-save-pending" or "original-restore-pending" or "restored"))
            || !j.Entries.Select(e => e.Name).Order().SequenceEqual(Branches.Order())
            || j.ChangedRules.Except(DailySandboxGameFiles.LegacyRules(j.Executable), StringComparer.OrdinalIgnoreCase).Any())
            throw new IOException("C 组恢复记录校验失败，未改动任何游戏文件。");
        _ = Overlay(root, j.Executable); return j;
    }
    internal static void ObserveGame(string root,string output,string id,string box,int pid,long started)
    {
        var current=Read(root);
        if(current==null||current.Id!=id||current.Box!=box||current.State!="prepared"||current.GameProcessId!=0
            ||!Path.GetFullPath(current.Output).Equals(Path.GetFullPath(output),StringComparison.OrdinalIgnoreCase))return;
        using var gate=Acquire(root);current=Read(root);
        if(current==null||current.Id!=id||current.State!="prepared"||current.GameProcessId!=0)return;
        current.GameProcessId=pid;current.GameStartTicks=started;Save(current);
    }
    static IEnumerable<string> Files(string branch)
    {
        if (File.Exists(branch)) { DailyCacheStorage.NoLinks(branch); yield return branch; yield break; }
        if (!Directory.Exists(branch)) yield break;
        var todo = new Stack<string>(); todo.Push(branch);
        while (todo.TryPop(out var dir))
        {
            DailyCacheStorage.NoLinks(dir);
            foreach (string path in Directory.EnumerateFileSystemEntries(dir))
            {
                DailyCacheStorage.NoLinks(path);
                if (Directory.Exists(path)) todo.Push(path); else yield return path;
            }
        }
    }
    static string Hash(string path, CancellationToken token = default)
    {
        using var f = File.OpenRead(path); using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024]; int count;
        while ((count = f.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); sha.AppendData(buffer, 0, count); }
        token.ThrowIfCancellationRequested(); return Convert.ToHexString(sha.GetHashAndReset());
    }
    static void Preserve(string source, string target, CancellationToken token, Action<long> progress)
    {
        DailyCacheStorage.NoLinks(source); DailyCacheStorage.NoLinks(target); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var input = File.OpenRead(source);
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1024 * 1024);
        byte[] buffer = new byte[1024 * 1024]; int count; long bytes = 0;
        while ((count = input.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); bytes += count; progress(bytes); }
        output.Flush(true); token.ThrowIfCancellationRequested();
    }
    static Dictionary<string,string> HostFiles(string executable, CancellationToken token = default)
    {
        string root=Path.GetDirectoryName(Path.GetFullPath(executable))!;
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(string relative in new[]{"BrownDust II.exe","UnityPlayer.dll",@"BrownDust II_Data\globalgamemanagers",@"BrownDust II_Data\Managed\Assembly-CSharp.dll"}){
            string path=Path.Combine(root,relative);DailyCacheStorage.NoLinks(path);
            result[relative]=File.Exists(path)?Hash(path,token):"absent";
        }
        return result;
    }
    internal static async Task PrepareAsync(DailyLoadingExperiment.Control control, string executable, string root, string box,
        Func<Task<string>> read, Func<Task<bool>> idle, Func<string, Task> remove, CancellationToken token, Action<string>? checkpoint = null)
    {
        using var gate=Acquire(root);
        if (!await idle()) throw new IOException("本组准备前检测到沙盒仍在运行，未改动文件。");
        string overlay = Overlay(root, executable); DailyCacheStorage.NoLinks(overlay);
        var prior = Read(root);
        if (prior is { State: not "restored" }) throw new IOException("上一次 C 组尚未恢复；采集器会先恢复，再开始新的一组。");
        string[] original = (await read()).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var j = new Journal { Id = control.Id, Box = box, BoxRoot = Path.GetFullPath(root), Executable = Path.GetFullPath(executable), Output = Path.GetFullPath(control.Output),HostFiles=HostFiles(executable,token),
            OriginalRules = original, ChangedRules = DailySandboxGameFiles.LegacyRules(executable).Intersect(original, StringComparer.OrdinalIgnoreCase).ToArray(),
            Entries = Branches.Select(n => new Entry { Name = n, Existed = Exists(Path.Combine(overlay, n)) }).ToList() };
        Directory.CreateDirectory(j.Output);
        int files = 0; long copiedBytes = 0, nextProgress = 0;
        void Progress(string phase, string relative = "", long bytes = 0, bool force = false)
        {
            long now = Environment.TickCount64; if (!force && now < nextProgress) return; nextProgress = now + 1000;
            Atomic(Path.Combine(j.Output,"legacy-preparation.json"), new { phase, relative, files, copiedBytes, currentFileBytes = bytes, utc = DateTimeOffset.UtcNow });
        }
        Progress("checking", force:true);
        // Complete difference inventory before mutation: unlike the former guard,
        // a mismatch is evidence to preserve, not a reason to abort the C group.
        using (var manifest = new StreamWriter(Path.Combine(j.Output, "program-overlay-before.jsonl"), false))
        foreach (var entry in j.Entries)
        foreach (string file in Files(Path.Combine(overlay, entry.Name)))
        {
            token.ThrowIfCancellationRequested(); string relative = Path.GetRelativePath(overlay, file);
            files++; Progress("checking", relative);
            string host = Path.Combine(Path.GetDirectoryName(executable)!, relative), sha = Hash(file,token);
            string? current = File.Exists(host) ? Hash(host,token) : null;
            // Hashes identify a mismatch but cannot explain it. Preserve both
            // actual versions for this explicit, private diagnostic session.
            // No quota, and no credential/config branches outside Branches.
            if(current!=sha){
                string sample=Path.Combine(j.Output,"program-differences","sandbox",relative);
                Progress("preserving",relative,force:true);
                Preserve(file,sample,token,bytes=>{Progress("preserving",relative,bytes);checkpoint?.Invoke("preserved-bytes:"+relative+":"+bytes);});copiedBytes+=new FileInfo(sample).Length;
                if(Hash(sample,token)!=sha)throw new IOException("游戏副本保全校验失败，尚未修改沙盒文件。");
                if(current!=null){
                    sample=Path.Combine(j.Output,"program-differences","host",relative);
                    Preserve(host,sample,token,bytes=>Progress("preserving",relative,bytes));copiedBytes+=new FileInfo(sample).Length;
                    if(Hash(sample,token)!=current)throw new IOException("本机游戏文件在准备期间变化，尚未修改沙盒文件。");
                }
            }
            manifest.WriteLine(JsonSerializer.Serialize(new { relative, bytes = new FileInfo(file).Length, sha256 = sha, hostSha256 = current,
                difference = current == null ? "overlay-only" : sha == current ? "same" : "different", preserved = true }));
        }
        Progress("applying",force:true);Save(j); checkpoint?.Invoke("journal-created");
        foreach (var entry in j.Entries)
        {
            token.ThrowIfCancellationRequested(); if (!await idle()) throw new IOException("沙盒在准备期间开始运行；已保留恢复记录。");
            entry.Stage = "move-pending"; Save(j); checkpoint?.Invoke("move-pending:" + entry.Name);
            if (entry.Existed) Move(Path.Combine(overlay, entry.Name), Backup(j, "before", entry.Name));
            checkpoint?.Invoke("moved:" + entry.Name); entry.Stage = "prepared"; Save(j);
        }
        j.State = "rules-pending"; Save(j);
        foreach (string rule in j.ChangedRules)
        { token.ThrowIfCancellationRequested(); if (!await idle()) throw new IOException("沙盒在准备期间开始运行；等待退出后自动恢复。"); await remove(rule); checkpoint?.Invoke("rule-removed:" + rule); }
        if (DailySandboxGameFiles.LegacyRules(executable).Intersect((await read()).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase).Any())
            throw new IOException("C 组文件规则未应用完整，已保全文件并准备自动恢复。");
        if(HostFiles(executable,token).Any(pair=>!j.HostFiles.TryGetValue(pair.Key,out var expected)||expected!=pair.Value))
            throw new IOException("主机游戏在 C 组准备期间发生更新；已保留恢复记录，未启动可能混用版本的游戏。请等主机更新完成后再测试。");
        j.State = "prepared"; Save(j); Progress("prepared",force:true);checkpoint?.Invoke("prepared");
    }
    internal static async Task<bool> RecoverAsync(string root, string box, Func<Task<string>> read, Func<Task<bool>> idle,
        Func<string, Task> append, CancellationToken token, Action<string>? checkpoint = null)
    {
        using var gate=Acquire(root);
        var j = Read(root); if (j == null || j.State == "restored") return false;
        if (!j.Box.Equals(box, StringComparison.OrdinalIgnoreCase)) throw new IOException("C 组隔离空间已改名，未改动文件。");
        if (!await idle()) throw new LoadingExperimentBusyException();
        string overlay = Overlay(root, j.Executable); DailyCacheStorage.NoLinks(overlay);
        j.State = "restoring"; Save(j);
        foreach (string rule in j.ChangedRules)
        {
            token.ThrowIfCancellationRequested(); if (!await idle()) throw new LoadingExperimentBusyException();
            if (!(await read()).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(rule, StringComparer.OrdinalIgnoreCase)) await append(rule);
        }
        if (j.ChangedRules.Except((await read()).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase).Any())
            throw new IOException("原文件规则尚未恢复；保全文件未改动，将继续重试。");
        foreach (var entry in j.Entries)
        {
            token.ThrowIfCancellationRequested(); if (!await idle()) throw new LoadingExperimentBusyException();
            if (entry.Stage == "restored") continue;
            string source = Path.Combine(overlay, entry.Name), before = Backup(j, "before", entry.Name), after = Backup(j, "during", entry.Name);
            DailyCacheStorage.NoLinks(source); DailyCacheStorage.NoLinks(before); DailyCacheStorage.NoLinks(after);
            // Never moved: a preparation interrupted before its first rename.
            if (entry.Existed && !Exists(before) && entry.Stage is "planned" or "move-pending")
            {
                if (!Exists(source)) throw new IOException("原游戏副本不在预期位置；已保留记录，未覆盖文件。");
                entry.Stage = "restored"; Save(j); continue;
            }
            // A crash after restoring the original but before saving its outcome.
            if (entry.Existed && !Exists(before) && entry.Stage == "original-restore-pending")
            {
                if (!Exists(source)) throw new IOException("恢复后的副本暂不可见；已保留记录。");
                entry.Stage = "restored"; Save(j); continue;
            }
            if (entry.Existed && !Exists(before)) throw new IOException("保全副本缺失；没有覆盖当前文件。");
            if (Exists(source))
            {
                entry.Stage = "during-save-pending"; Save(j);
                Move(source, after); checkpoint?.Invoke("during-saved:" + entry.Name);
            }
            if (entry.Existed)
            {
                entry.Stage = "original-restore-pending"; Save(j);
                Move(before, source); checkpoint?.Invoke("original-restored:" + entry.Name);
            }
            entry.Stage = "restored"; Save(j);
        }
        j.State = "restored"; Save(j);
        Atomic(Path.Combine(Path.GetDirectoryName(Pending(root))!, j.Id, "restored.json"), j);
        try { Atomic(Path.Combine(j.Output, "legacy-files-" + box + ".json.restored.json"), new { restoredUtc = DateTimeOffset.UtcNow, box, credentialsChanged = false, preservedDuring = Backup(j, "during", "") }); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return true;
    }
}

public sealed class LoadingExperimentBusyException : IOException
{
    public LoadingExperimentBusyException() : base("正在等待本组游戏和隔离窗口退出，之后自动恢复文件与规则。") { }
}
