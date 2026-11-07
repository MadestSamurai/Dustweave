using Dustweave;
internal static class StorageResourceCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); cases.Add("storage-resources: " + label); }
        string area = Path.GetFullPath(Path.Combine(output, "storage-resources")), root = Path.Combine(area, "host-data"),
            instances = Path.Combine(area, "instances"), source = Path.Combine(area, "host-audio");
        string Put(string basis, string relative, string value)
        { string path = Path.Combine(basis, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, value); return path; }
        const string catalog = "{\"catalogVersion\":\"fixture-v1\",\"entries\":{}}";
        string key = new('c', 64), boxName = DailySandbox.CanonicalBoxName(key), box = Path.Combine(instances, boxName),
            overlay = Path.Combine(box, "root", DailySandboxAudio.RelativeRoot), relative = "Patched/Sound/" + new string('b', 40);
        DailyJson.Write(Path.Combine(box, "instance.json"), new DailySandboxRequest(boxName, 1, key, "fixture", @"C:\fixture\BrownDust II.exe", DateTimeOffset.UtcNow));
        string credentials = Put(box, "root/user/current/AppData/LocalLow/Gamfs/BrownDust II/Neo/sign-in", "must-retain");
        Put(source, "catalog_sound_hd.json", catalog); Put(overlay, "catalog_sound_hd.json", catalog);
        Put(source, relative, "RIFFfixture-bank"); string copy = Put(overlay, relative, "RIFFfixture-bank");
        bool idle = true, hostIdle = true, normal = true;
        var service = new DailyStorageMaintenance(root, instances, null, _ => idle, () => true)
        { ResourceSource = source, ResourceHostIdle = () => hostIdle, ResourceView = _ => normal };
        Check(DailySandboxAudio.Bank(relative) && DailySandboxAudio.Bank("Patched/P11011_LocalVoice_JP/" + new string('e', 40)), "known bank families are recognized");
        Check(!DailySandboxAudio.Bank("Patched/Neo/" + new string('b', 40)) && !DailySandboxAudio.Bank("../" + relative)
            && !DailySandboxAudio.Bank("catalog_sound_hd.json"), "credentials, indexes and path traversal are excluded");
        string audioPath = @"C:\Users\fixture\AppData\LocalLow\Gamfs\BrownDust II\SoundData";
        Check(DailySandboxAudio.NormalView(audioPath, k => k == "WriteFilePath" ? @"C:\Users\fixture\AppData\LocalLow\Gamfs\BrownDust II\Neo" : ""), "separate credential isolation does not disable resource optimization");
        Check(DailySandboxAudio.NormalView(audioPath, k => k == "ClosedFilePath" ? @"Dustweave.exe,*\MacType.dll" : ""), "an unrelated process rule does not block optional audio maintenance");
        foreach (string rule in new[] { "WriteFilePath", "ReadFilePath", "ClosedFilePath", "OpenFilePath", "NormalFilePath" })
            Check(!DailySandboxAudio.NormalView(audioPath, k => k == rule ? @"BrownDust II.exe,C:\Users\fixture\AppData\LocalLow\Gamfs\BrownDust II\*" : ""), "resource-specific " + rule + " retains copies");
        Check(!DailySandboxAudio.NormalView(audioPath, k => k == "UseFileDeleteV2" ? "y" : ""), "unverified deletion metadata format retains copies");
        Check(!DailySandboxAudio.NormalView(audioPath, k => k == "UseFileDeleteV2" ? "y\nn" : "")
            && DailySandboxAudio.NormalView(audioPath, k => k == "UseFileDeleteV2" ? "n\ny" : ""), "effective first boolean value overrides inherited settings");
        var plan = await service.CheckResourceCopiesAsync();
        Check(plan.Copies.Length == 1 && plan.Bytes == new FileInfo(copy).Length && File.Exists(copy), "preview compares complete content without deletion");
        idle = false;
        Check((await service.OptimizeResourceCopiesAsync(plan)).Files == 0 && File.Exists(copy), "sandbox started after preview defers deletion"); idle = true;
        hostIdle = false;
        Check((await service.OptimizeResourceCopiesAsync(plan)).Files == 0, "host game running defers resources only"); hostIdle = true;
        normal = false;
        Check((await service.OptimizeResourceCopiesAsync(plan)).Files == 0, "changed access rules after preview preserve the copy"); normal = true;
        string snapshot = Put(box, "root/Snapshots.ini", "fixture");
        Check((await service.OptimizeResourceCopiesAsync(plan)).Files == 0 && File.Exists(copy), "layered snapshot metadata prevents deleting an unverified file view"); File.Delete(snapshot);
        string deletionIndex = Put(box, "root/FilePaths.dat", "fixture");
        Check((await service.OptimizeResourceCopiesAsync(plan)).Files == 0, "v2 deletion metadata remains untouched"); File.Delete(deletionIndex);
        string parent = Path.GetDirectoryName(copy)!; DateTime created = Directory.GetCreationTimeUtc(parent);
        Directory.SetCreationTimeUtc(parent, DateTime.FromFileTimeUtc(DailySandboxAudio.DeletedCreationTime));
        Check((await service.OptimizeResourceCopiesAsync(plan)).Files == 0 && File.Exists(copy), "a V1 deleted ancestor never exposes a hidden host file");
        Directory.SetCreationTimeUtc(parent, created);
        Put(source, relative, "RIFFchanged-bank");
        Check((await service.OptimizeResourceCopiesAsync(plan)).Files == 0 && File.Exists(copy), "same-length host changes invalidate a preview");
        Put(source, relative, "RIFFfixture-bank");
        Put(overlay, "catalog_sound_hd.json", "{\"catalogVersion\":\"fixture-v2\"}");
        Check((await service.OptimizeResourceCopiesAsync(plan)).Files == 0, "newer sandbox catalog is preserved");
        Put(overlay, "catalog_sound_hd.json", catalog);
        using (var writer = new FileStream(copy, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            Check((await service.OptimizeResourceCopiesAsync(plan)).Files == 0, "a live writer cannot be unlinked");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel(); bool canceled = false;
            try { await service.OptimizeResourceCopiesAsync(plan, cancel.Token); } catch (OperationCanceledException) { canceled = true; }
            Check(canceled && File.Exists(copy), "cancellation retains the unfinished file");
        }
        var done = await service.OptimizeResourceCopiesAsync(plan);
        Check(done.Files == 1 && done.Bytes == plan.Bytes && !File.Exists(copy) && File.Exists(Path.Combine(source, relative)), "restored conditions complete optimization and retain host content");
        Check(File.ReadAllText(credentials) == "must-retain" && File.ReadAllText(Path.Combine(overlay, "catalog_sound_hd.json")) == catalog, "sign-in and catalog bytes never change");
        Check((await service.OptimizeResourceCopiesAsync(plan)).Files == 0, "repeating a completed preview does not double count deletion");
        Put(overlay, relative, "RIFFfixture-bank");
        var restarted = new DailyStorageMaintenance(root, instances, null, _ => true, () => true)
        { ResourceSource = source, ResourceHostIdle = () => true, ResourceView = _ => true };
        Check((await restarted.MaintainResourceCopiesAsync()).Files == 1, "a fresh service can optimize a redownloaded copy without a persisted preview");
        Put(overlay, relative, "RIFFdifferent!!!");
        Check((await restarted.MaintainResourceCopiesAsync()).Files == 0 && File.Exists(copy), "different private banks survive background maintenance");
        Put(overlay, relative, "RIFFfixture-bank"); File.Delete(Path.Combine(source, relative));
        Check((await restarted.CheckResourceCopiesAsync()).Copies.Length == 0 && File.Exists(copy), "missing host bank never authorizes cleanup");
        Put(source, relative, "RIFFfixture-bank");
        var spoof = plan with { Copies = [plan.Copies[0] with { Sandbox = area, Relative = "../Neo/sign-in", StoredBytes = long.MaxValue }] };
        Check((await restarted.OptimizeResourceCopiesAsync(spoof)).Files == 0 && File.Exists(copy), "unowned preview paths cannot expand deletion scope");
        Check(File.Exists(Path.Combine(root, "resource-optimization-last.json")), "bounded maintenance evidence is persisted");
        Put(overlay, relative, "RIFFdifferent!!!");
        foreach(char suffix in new[]{'c','d'})
        {string name="Patched/Sound/"+new string(suffix,40);Put(overlay,name,"RIFFbatch-bank");Put(source,name,"RIFFbatch-bank");}
        var bounded = new DailyStorageMaintenance(root, instances, null, _=>true, ()=>true)
        {ResourceSource=source,ResourceHostIdle=()=>true,ResourceView=_=>true,ResourceBatchBytes=1};
        int total=0;bool more=true;
        for(int i=0;i<5&&more;i++){var batch=await bounded.MaintainResourceCopiesAsync();Check(batch.Files<=1,"background batch completes at most one fixture bank");total+=batch.Files;more=batch.MoreWork;}
        Check(total==2&&!more&&File.Exists(copy),"a different bank at the front cannot starve later duplicates or cause endless continuation");
        Check(DailyStorageBreakdown.Data(root, Path.Combine(root, "tool-plugins/example.tool/extensions/versions/hash/tool.dll")) == "plugins"
            && DailyStorageBreakdown.Data(root, Path.Combine(root, "tool-plugins/backups/example.tool/saved.json")) == "progress"
            && DailyStorageBreakdown.Data(root, Path.Combine(root, "tool-plugins/sessions/session/inventory.json")) == "tools",
            "plugin payloads, preset recovery and session data have distinct storage classifications");
    }
}
