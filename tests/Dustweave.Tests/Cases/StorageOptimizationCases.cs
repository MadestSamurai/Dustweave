using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dustweave;

internal static class StorageOptimizationCases
{
    [DllImport("kernel32.dll", EntryPoint="CreateHardLinkW", CharSet=CharSet.Unicode, SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] static extern bool CreateHardLink(string path,string existing,IntPtr security);
    public static async Task Run(string output,List<string> cases)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);cases.Add("storage-optimization: "+label);}
        string root=Path.Combine(output,"storage-optimization"),step=Path.GetFullPath(Path.Combine(root,"live/steps",Guid.NewGuid().ToString("N")));
        var value=new {state="unknown_timeout",amount=9007199254740993L,text="中文🔎",rows=Enumerable.Range(0,800).Select(i=>new {id=i,details="unchanged recovery snapshot",hp=1234.5678m}).ToArray()};
        string record=Path.Combine(step,"result.json"),preferences=Path.Combine(root,"preferences.json");
        DailyJson.Write(record,value);DailyJson.Write(preferences,value);
        var compact=File.ReadAllBytes(record);var indented=File.ReadAllBytes(preferences);
        Check(compact.Length<indented.Length && JsonNode.DeepEquals(JsonNode.Parse(compact),JsonNode.Parse(indented)),"compact writes preserve Unicode, large integers, decimals and complete recovery fields");
        Check(File.ReadAllText(preferences).Contains('\n')&&!File.ReadAllText(record).Contains('\n'),"human settings retain formatting while machine records are compact");
        DateTime old=DateTime.UtcNow.AddDays(-3);File.SetLastWriteTimeUtc(record,old);
        DailyStorageItem Item(string path){var f=new FileInfo(path);return new(path,f.Length,f.LastWriteTimeUtc){DiskBytes=DailyStorageCompression.StoredBytes(path,f.Length,f.Attributes)};}
        string Put(string rel,int size=65536,int days=3)
        {string p=Path.GetFullPath(Path.Combine(root,rel));Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllBytes(p,new byte[size]);File.SetLastWriteTimeUtc(p,DateTime.UtcNow.AddDays(-days));return p;}
        string account=Put("accounts/session.json"),update=Put("updates/1.4.2-Portable/backup/recovery.json"),fresh=Put("live/diagnostics/new.json",days:0);
        string component=Put("suite-cache/modules/daily/generation/client/module.dll"),unknown=Put("unmanaged/history.json"),small=Put("live/diagnostics/small.json",size:100);
        string held=Put("live/diagnostics/locked.json"),changed=Put("live/diagnostics/changed.json");
        var all=new[]{record,preferences,account,update,fresh,component,unknown,small,held,changed}.Select(Item).ToArray();
        if(DailyStorageCompression.Supported(root))
        {
            var plan=DailyStorageCompression.Plan(root,all,DateTime.UtcNow);
            Check(plan.Select(f=>f.Path).ToHashSet().SetEquals(new[]{record,component,held,changed}),"compression is restricted to aged machine records and reproducible components");
            File.AppendAllText(changed,"changed after preview");
            using(var hold=new FileStream(held,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {
                var result=DailyStorageCompression.Apply(plan);
                Check(result.CompressedFiles==2 && result.Skipped==2 && result.Bytes>0,"compression skips busy and changed files without delaying other records");
            }
            Check(SHA256.HashData(File.ReadAllBytes(record)).SequenceEqual(SHA256.HashData(compact)),"NTFS compression leaves every original byte unchanged");
            Check(File.GetLastWriteTimeUtc(record)==old,"compression does not renew retention age");
            Check(DailyJson.TryRead<JsonObject>(record)?["state"]?.GetValue<string>()=="unknown_timeout","pending outcome remains unknown and readable by the existing recovery reader");
            Check((File.GetAttributes(record)&FileAttributes.Compressed)!=0 && Item(record).DiskBytes<Item(record).Bytes,"stored-size accounting reports compressed bytes");
            var storage=await DailyCacheStorage.ScanAsync(root);
            Check(storage.TotalBytes==Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Select(Item).Sum(f=>f.DiskBytes),"storage totals use the compressed size, not uncompressed JSON length");
            var logPlan=await DailyLogCleanup.ScanAsync(root,new());
            Check(logPlan.ManagedBytes<Directory.EnumerateFiles(Path.Combine(root,"live"),"*",SearchOption.AllDirectories).Sum(p=>new FileInfo(p).Length),"log budget also uses stored bytes");
            Check(!DailyStorageCompression.Plan(root,all,DateTime.UtcNow).Any(f=>f.Path==record),"already compressed files are not processed again");
            string longRecord=Put("live/diagnostics/"+string.Join('/',Enumerable.Repeat(new string('x',70),4))+"/result.json");
            byte[] longHash=SHA256.HashData(File.ReadAllBytes(longRecord));
            DateTime longWritten=File.GetLastWriteTimeUtc(longRecord);
            var longResult=DailyStorageCompression.Apply(DailyStorageCompression.Plan(root,[Item(longRecord)],DateTime.UtcNow));
            Check(longRecord.Length>260&&longResult.CompressedFiles==1&&Item(longRecord).DiskBytes<Item(longRecord).Bytes,"deep sandbox paths compress and report actual stored bytes beyond MAX_PATH");
            Check(SHA256.HashData(File.ReadAllBytes(longRecord)).SequenceEqual(longHash)&&File.GetLastWriteTimeUtc(longRecord)==longWritten,"long-path compression preserves recovery content and retention age");
            var zip=await DailyDiagnosticExport.CreateAsync(root,Path.Combine(output,"compressed-diagnostics.zip"),"compression fixture","test");
            using(var archive=ZipFile.OpenRead(zip.Path))
            {var entry=archive.GetEntry("logs/main/"+Path.GetRelativePath(root,record).Replace('\\','/'));Check(entry!=null,"existing diagnostics exporter includes compressed records");using var reader=new StreamReader(entry!.Open());Check(JsonNode.DeepEquals(JsonNode.Parse(reader.ReadToEnd()),JsonNode.Parse(compact)),"diagnostic export reconstructs all original recovery data");}
            // A rewrite atomically replaces the old compressed file; no stale archive is consulted.
            DailyJson.Write(record,new{state="completed",revision=2});Check(DailyJson.TryRead<JsonObject>(record)?["revision"]?.GetValue<int>()==2,"subsequent atomic journal writes remain supported");
            var many=Enumerable.Range(0,135).Select(i=>Item(Put("live/diagnostics/batch-"+i+".json"))).ToArray();
            var batch=DailyStorageCompression.Apply(DailyStorageCompression.Plan(root,many,DateTime.UtcNow));
            Check(batch.CompressedFiles==128&&batch.MoreWork,"one idle batch stops at its file limit and schedules the remainder");
            var rest=DailyStorageCompression.Apply(DailyStorageCompression.Plan(root,many,DateTime.UtcNow));Check(rest.CompressedFiles==7&&!rest.MoreWork,"a later batch resumes without reprocessing completed files");
            var busyPrefix=Enumerable.Range(0,129).Select(i=>Item(Put("live/diagnostics/busy-prefix-"+i+".json",16384))).ToArray();
            var handles=new List<FileStream>();
            try
            {
                foreach(var file in busyPrefix.Take(128))handles.Add(new(file.Path,FileMode.Open,FileAccess.ReadWrite,FileShare.None));
                var blocked=DailyStorageCompression.Apply(busyPrefix,scope:root);
                Check(blocked.CompressedFiles==0&&blocked.MoreWork,"a fully busy first batch retains pending maintenance");
                using(var last=new FileStream(busyPrefix[^1].Path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                {
                    var noProgress=DailyStorageCompression.Apply(busyPrefix,scope:root);
                    Check(noProgress.CompressedFiles==0&&!noProgress.MoreWork,"an exhausted busy pass returns to the normal maintenance interval");
                }
                DailyStorageCompression.Apply(busyPrefix,scope:root);
                var progressed=DailyStorageCompression.Apply(busyPrefix,scope:root);
                Check(progressed.CompressedFiles==1,"batch rotation reaches available files behind a busy prefix");
            }
            finally{foreach(var handle in handles)handle.Dispose();}
            var released=DailyStorageCompression.Apply(DailyStorageCompression.Plan(root,busyPrefix,DateTime.UtcNow),scope:root);
            Check(released.CompressedFiles==128&&!released.MoreWork,"deferred files are maintained after writers release them");
            string hardlink=Path.Combine(root,"live/diagnostics/hardlink.json");
            if(!CreateHardLink(hardlink,account,IntPtr.Zero))throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
            var shared=DailyStorageCompression.Apply(DailyStorageCompression.Plan(root,[Item(hardlink)],DateTime.UtcNow));
            Check(shared.CompressedFiles==0&&shared.Skipped==1&&(File.GetAttributes(account)&FileAttributes.Compressed)==0,"hard links cannot compress an account file through a permitted record path");
            string continuation=Put("live/diagnostics/continuation.json"),expiredLog=Put("continuation.log",days:10);
            var continuationResult=await new DailyStorageMaintenance(root,null,null,_=>true,()=>true).ContinueCompressionAsync();
            Check(continuationResult.CompressedFiles>0 && File.Exists(expiredLog) && (File.GetAttributes(continuation)&FileAttributes.Compressed)!=0,
                "compression continuations do not re-run log retention or journal reconciliation");
            using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();bool stopped=false;try{DailyStorageCompression.Apply([Item(held)],cancelled.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped,"compression observes cancellation before touching another file");}
            string linked=Path.Combine(root,"live/diagnostics/linked.json");
            try{File.CreateSymbolicLink(linked,account);Check(!DailyStorageCompression.Plan(root,[Item(linked)],DateTime.UtcNow).Any(),"linked records are never compression candidates");}catch(UnauthorizedAccessException){}
        }
        else Check(DailyStorageCompression.Plan(root,all,DateTime.UtcNow).Length==0,"unsupported volumes retain ordinary files without a compression requirement");
        string dumps=Path.Combine(root,"CrashDumps");
        string d1=Put("CrashDumps/Dustweave.exe.1.dmp",days:10),d2=Put("CrashDumps/Dustweave.exe.2.dmp",days:3),d3=Put("CrashDumps/Dustweave.exe.3.dmp",days:2),d4=Put("CrashDumps/Dustweave.exe.4.dmp",days:0);
        string other=Put("CrashDumps/OtherApp.exe.1.dmp",days:10),nested=Put("CrashDumps/nested/Dustweave.exe.5.dmp",days:10);
        var dumpPlan=DailyCrashStorage.Plan(dumps,new[]{d1,d2,d3,d4,other,nested}.Select(Item),DateTime.UtcNow);
        Check(dumpPlan.Select(u=>u.Path).ToHashSet().SetEquals(new[]{d1,d2}),"crash retention keeps two recent own dumps, expires old ones and excludes other applications and nested files");
        var service=new DailyStorageMaintenance(Path.Combine(root,"empty-host"),null,null,_=>true,()=>true,dumps);
        var before=await service.ScanAsync(new());Check(before.TotalBytes==new[]{d1,d2,d3,d4}.Select(Item).Sum(f=>f.DiskBytes),"host dump accounting counts only owned dumps");
        await service.CleanAsync(new());Check(!File.Exists(d1)&&!File.Exists(d2)&&new[]{d3,d4,other,nested}.All(File.Exists),"dump cleanup applies the same ownership and retention boundaries");
    }
}
