using Dustweave;
using System.Diagnostics;

internal static class UpdateRetentionCases
{
    private sealed record Fixture(string Directory, string Attempt, string Job, string Target, string Helper, string Staging, string Backup);
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); cases.Add("update-retention: " + label); }
        string Area(string name) { string root=Path.Combine(output,"update-retention",name); Directory.CreateDirectory(root); return root; }
        string Put(string path, string text="fixture") { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path,text); return path; }
        void Age(string path, double days) { foreach(string file in Directory.EnumerateFiles(path,"*",SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(file,DateTime.UtcNow.AddDays(-days)); }
        string Download(string root,string version,double days=0,string flavor="Portable")
        {
            string dir=Path.Combine(root,"updates",version+"-"+flavor);
            Put(Path.Combine(dir,"package.zip"));Put(Path.Combine(dir,"signed-feed.json"),"{}");Put(Path.Combine(dir,"download.lock"),"");Age(dir,days);return dir;
        }
        Fixture Install(string root,string version,string? state,double days=0,string? directory=null,string installation="installation")
        {
            string dir=directory??Download(root,version),nonce=Guid.NewGuid().ToString("N"),attempt=Path.Combine(dir,"attempts",nonce);
            string target=Path.Combine(root,installation,"Dustweave.exe");Put(target);
            var asset=new DailyUpdateAsset("Portable","package.zip",1,new string('0',64));
            var release=new DailyUpdateRelease(version,null,2,[],[asset]);
            string job=Path.Combine(attempt,"job.json");DailyUpdateJson.Write(job,new DailyUpdateJob(dir,target,int.MaxValue,0,release,asset,nonce));
            string helper=Put(Path.Combine(attempt,"Dustweave.Updater.exe"));
            string staging=Put(Path.Combine(attempt,"staging","app","Dustweave.dll"));
            string backup=Path.Combine(attempt,"backup");
            if(state!=null)
            {
                Put(Path.Combine(backup,"app","Dustweave.dll"));
                DailyUpdateJson.Write(Path.Combine(backup,"transaction.json"),new DailyUpdateJournal(Path.GetDirectoryName(target)!,backup,Path.Combine(attempt,"staging"),[],state));
            }
            Age(dir,days);return new(dir,attempt,job,target,helper,staging,backup);
        }
        string root=Area("count");
        string old=Download(root,"1.3.8"),nine=Download(root,"1.3.9"),ten=Download(root,"1.3.10"),beta=Download(root,"1.3.11-beta"),current=Download(root,"1.3.11");
        File.SetLastWriteTimeUtc(Path.Combine(old,"package.zip"),DateTime.UtcNow.AddHours(1));
        await DailyCacheStorage.CleanAsync(root);
        Check(!Directory.Exists(old)&&!Directory.Exists(nine)&&!Directory.Exists(ten),"count removes excess versions even on the same day; file timestamps cannot change semantic ordering");
        Check(Directory.Exists(beta)&&Directory.Exists(current),"two newest semantic versions retained");
        string lite=Download(root,"1.3.11",0,"Lite");await DailyCacheStorage.CleanAsync(root);
        Check(Directory.Exists(lite)&&Directory.Exists(beta),"Lite/Portable of the same release count as one version");

        root=Area("age");old=Download(root,"9.0.0",8);await DailyCacheStorage.CleanAsync(root);
        Check(!Directory.Exists(old),"age removes even the only/newest cache; count and age are OR limits");
        old=Download(root,"9.0.1",6);await DailyCacheStorage.CleanAsync(root);
        Check(Directory.Exists(old),"under both limits a prepared download survives");
        DateTime now=DateTime.UtcNow;
        string payload=Path.Combine(old,"package.zip");
        var exact=UpdateCacheRetention.Plan(Path.Combine(root,"updates"),[new(payload,7,now.AddDays(-7))],now);
        Check(exact.Length==1,"exactly seven days is expired");

        root=Area("downloads");old=Download(root,"1.0.0",20);Put(Path.Combine(old,"package.zip.partial"));Age(old,20);
        string unexpected=Put(Path.Combine(root,"updates","user-note","keep.txt"));
        string network=Put(Path.Combine(root,"updates","network.json"));
        await DailyCacheStorage.CleanAsync(root);
        Check(!Directory.Exists(old),"abandoned downloads and partial files without jobs expire");
        Check(File.Exists(unexpected)&&File.Exists(network),"unknown folders and root update/network state preserved");

        root=Area("completed");var complete=Install(root,"1.3.17","completed");
        await DailyCacheStorage.CleanAsync(root);
        Check(!File.Exists(complete.Helper)&&!File.Exists(complete.Staging),"confirmed update removes helper and staging immediately");
        Check(File.Exists(complete.Job)&&File.Exists(Path.Combine(complete.Backup,"transaction.json"))&&Directory.Exists(complete.Backup),"latest backup and receipt retained within limits");
        Check(!File.Exists(Path.Combine(complete.Directory,"package.zip"))&&File.Exists(Path.Combine(complete.Directory,"signed-feed.json")),"confirmed start discards used download but preserves signed recovery metadata");
        var retry=Install(root,"1.3.17","rolled_back",0,complete.Directory);
        string failure=Put(Path.Combine(retry.Attempt,"failure.json"),"startup failure fixture");
        Age(complete.Attempt,1);await DailyCacheStorage.CleanAsync(root);
        Check(File.Exists(Path.Combine(complete.Backup,"app","Dustweave.dll"))&&!File.Exists(Path.Combine(retry.Backup,"app","Dustweave.dll")),"failed retry does not replace the last confirmed upgrade backup with a duplicate of the restored installation");
        await DailyCacheStorage.CleanAsync(root);
        Check(File.ReadAllText(failure)=="startup failure fixture"&&File.Exists(retry.Job)&&File.Exists(Path.Combine(retry.Backup,"transaction.json"))&&File.Exists(Path.Combine(complete.Backup,"app","Dustweave.dll")),"repeated cleanup retains recent failure evidence and does not mistake a receipt-only attempt for a usable backup");

        root=Area("one-backup-per-install");
        var previous=Install(root,"1.5.10","completed",1);
        complete=Install(root,"1.5.11","completed");
        string original=File.ReadAllText(Path.Combine(complete.Backup,"app","Dustweave.dll"));
        await DailyCacheStorage.CleanAsync(root);
        Check(!File.Exists(Path.Combine(previous.Backup,"app","Dustweave.dll"))&&File.Exists(previous.Job)&&File.ReadAllText(Path.Combine(complete.Backup,"app","Dustweave.dll"))==original,"successive releases keep one byte-intact backup per installation and small diagnostic receipts, not one program copy per release");
        Download(root,"1.5.12");Download(root,"1.5.13");
        await DailyCacheStorage.CleanAsync(root);
        Check(Directory.Exists(complete.Backup),"new prepared downloads do not evict the installation recovery backup by semantic version count");
        Age(complete.Directory,8);await DailyCacheStorage.CleanAsync(root);
        Check(!Directory.Exists(complete.Directory),"retained installation backup still obeys the age limit");

        root=Area("separate-installs");
        previous=Install(root,"1.5.8","completed",1,installation:"portable-A");
        complete=Install(root,"1.5.9","completed",installation:"portable-B");
        Download(root,"1.5.10");Download(root,"1.5.11");
        await DailyCacheStorage.CleanAsync(root);
        Check(Directory.Exists(previous.Backup)&&Directory.Exists(complete.Backup),"different installation targets retain independent backups even outside the two-download window");

        root=Area("rollback-only");
        retry=Install(root,"1.5.11","rolled_back");
        await DailyCacheStorage.CleanAsync(root);
        Check(Directory.Exists(retry.Backup)&&File.Exists(Path.Combine(retry.Directory,"package.zip"))&&!File.Exists(retry.Helper),"failed-only release retains its retry download and one rollback backup");

        root=Area("completed-payloads");complete=Install(root,"1.5.11","completed");
        var payloadJob=DailyUpdateJson.TryRead<DailyUpdateJob>(complete.Job)!;
        DailyUpdateJson.Write(complete.Job,payloadJob with{DeltaFileName="used-delta.zip"});
        string delta=Put(Path.Combine(complete.Directory,"used-delta.zip")),partial=Put(Path.Combine(complete.Directory,"used-delta.zip.partial"));
        string unknownZip=Put(Path.Combine(complete.Directory,"unrelated.zip"));
        using(var download=new FileStream(Path.Combine(complete.Directory,"download.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            var locked=await DailyCacheStorage.CleanAsync(root);
            Check(locked.Skipped>0&&File.Exists(delta)&&File.Exists(complete.Helper),"legacy download lock protects early payload and attempt cleanup as well as whole-version expiry");
        }
        await DailyCacheStorage.CleanAsync(root);
        Check(!File.Exists(delta)&&!File.Exists(partial)&&File.Exists(unknownZip),"only exact completed payload names and their partials are removed early");

        root=Area("shared-payloads");complete=Install(root,"1.5.11","completed",1);
        var inFlight=Install(root,"1.5.11","awaiting_start",0,complete.Directory);
        await DailyCacheStorage.CleanAsync(root);
        Check(File.Exists(Path.Combine(complete.Directory,"package.zip"))&&File.Exists(inFlight.Helper)&&Directory.Exists(complete.Backup),"unfinished sibling transaction protects the shared download without losing the last completed backup");

        root=Area("quick-maintenance");complete=Install(root,"1.5.11","completed");
        string savedAccount=Put(Path.Combine(root,"accounts","saved.json"),"account fixture"),oldLog=Put(Path.Combine(root,"live","old.log"),"diagnostic fixture");
        File.SetLastWriteTimeUtc(oldLog,DateTime.UtcNow.AddDays(-20));
        string component=Put(Path.Combine(root,"suite-cache","old-generation","component.dll"));
        File.SetLastWriteTimeUtc(component,DateTime.UtcNow.AddDays(-20));
        var maintenance=new DailyStorageMaintenance(root,null,null,_=>false,()=>false);
        using(var update=DailyUpdateHost.AcquireStorage(root))
        {
            bool refused=false;try{await maintenance.CleanUpdateCacheAsync(new(false));}catch(IOException){refused=true;}
            Check(refused&&File.Exists(complete.Helper),"short idle cleanup respects active updater storage lease");
        }
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();bool cancelled=false;try{await maintenance.CleanUpdateCacheAsync(new(false),cancel.Token);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled&&File.Exists(complete.Helper),"cancelled short idle cleanup leaves the update untouched");
        }
        await maintenance.CleanUpdateCacheAsync(new(false));
        Check(!File.Exists(complete.Helper)&&!File.Exists(Path.Combine(complete.Directory,"package.zip"))&&File.Exists(oldLog)&&File.Exists(component)&&File.ReadAllText(savedAccount)=="account fixture",
            "short update cleanup runs independently of log expiry without touching accounts, logs or compiled components");

        root=Area("mixed");var pending=Install(root,"1.0.0","replacing",12);
        var terminal=Install(root,"1.0.0","completed",12,pending.Directory);
        Download(root,"1.1.0");Download(root,"1.2.0");
        await DailyCacheStorage.CleanAsync(root);
        Check(Directory.Exists(pending.Attempt)&&File.Exists(pending.Helper)&&File.Exists(Path.Combine(pending.Directory,"signed-feed.json")),"real unfinished recovery and its signed feed survive both limits");
        Check(!Directory.Exists(terminal.Attempt),"one pending retry does not retain completed sibling attempts");
        foreach(string state in new[]{"prepared","awaiting_start","rolling_back","unknown"})
        {
            root=Area("protected-"+state);var active=Install(root,"1.0.0",state,12);await DailyCacheStorage.CleanAsync(root);
            Check(File.Exists(active.Helper)&&File.Exists(active.Staging),"protects "+state+" recovery");
        }

        root=Area("marker");complete=Install(root,"1.0.0","completed",12);
        string marker=Path.Combine(Path.GetDirectoryName(complete.Target)!,DailyUpdateTransaction.MarkerName);
        DailyUpdateJson.Write(marker,new DailyUpdateMarker(complete.Job,Path.GetFileName(complete.Attempt),new string('0',64)));
        await DailyCacheStorage.CleanAsync(root);
        Check(File.Exists(complete.Job)&&File.Exists(complete.Helper),"terminal journal with uncleared launcher marker remains readable");
        File.Delete(marker);await DailyCacheStorage.CleanAsync(root);
        Check(!Directory.Exists(complete.Directory),"cleared recovery marker releases expired cache");

        root=Area("revalidate");complete=Install(root,"1.0.0","completed",12);
        var plan=await DailyCacheStorage.ScanAsync(root);var unit=plan.Units.Single();
        marker=Path.Combine(Path.GetDirectoryName(complete.Target)!,DailyUpdateTransaction.MarkerName);
        DailyUpdateJson.Write(marker,new DailyUpdateMarker(complete.Job,Path.GetFileName(complete.Attempt),new string('0',64)));
        Check(!UpdateCacheRetention.CanClean(unit),"a newly created marker invalidates an existing cleanup plan");
        File.Delete(marker);Put(marker,"broken");await DailyCacheStorage.CleanAsync(root);
        Check(Directory.Exists(complete.Directory),"unreadable marker fails closed");

        root=Area("early-failure");var early=Install(root,"1.0.0",null,12);
        await DailyCacheStorage.CleanAsync(root);
        Check(!Directory.Exists(early.Directory),"failed before backup/transaction creation can expire without terminal journal");
        early=Install(root,"1.0.1",null);
        await DailyCacheStorage.CleanAsync(root);
        Check(!Directory.Exists(early.Attempt)&&File.Exists(Path.Combine(early.Directory,"package.zip")),"abandoned pre-install attempt is reclaimed without discarding a recent download");
        string orphan=Put(Path.Combine(early.Directory,"attempts",Guid.NewGuid().ToString("N"),"Dustweave.Updater.exe"));
        await DailyCacheStorage.CleanAsync(root);Check(!File.Exists(orphan),"helper copied before a failed job creation does not accumulate");
        root=Area("damaged");var damaged=Install(root,"1.0.0","replacing",12);
        Put(Path.Combine(damaged.Backup,"transaction.json"),"broken");Age(damaged.Directory,12);
        await DailyCacheStorage.CleanAsync(root);
        Check(File.Exists(damaged.Helper),"damaged recovery records protected");
        root=Area("unknown-layout");old=Download(root,"1.0.0",12);Put(Path.Combine(old,"job.json"),"{}");Age(old,12);
        await DailyCacheStorage.CleanAsync(root);Check(Directory.Exists(old),"unknown recovery layout is not treated as an unused download");
        root=Area("nested-layout");old=Download(root,"1.0.0",12);
        string nested=Put(Path.Combine(old,"attempts",Guid.NewGuid().ToString("N"),"unknown","job.json"),"{}");Age(old,12);
        await DailyCacheStorage.CleanAsync(root);Check(File.Exists(nested),"unrecognized nested recovery layout is protected");

        root=Area("parent");early=Install(root,"1.0.0",null,12);
        var job=DailyUpdateJson.TryRead<DailyUpdateJob>(early.Job)!;
        using(var self=Process.GetCurrentProcess())DailyUpdateJson.Write(early.Job,job with{ParentPid=self.Id,ParentStartTicks=self.StartTime.ToUniversalTime().Ticks});
        Age(early.Directory,12);await DailyCacheStorage.CleanAsync(root);
        Check(File.Exists(early.Helper),"live parent handing over to updater blocks expiry");

        foreach(string state in new[]{"completed","rolled_back"})
        {
            root=Area("terminal-parent-"+state);complete=Install(root,"1.0.0",state,12);
            job=DailyUpdateJson.TryRead<DailyUpdateJob>(complete.Job)!;
            using(var self=Process.GetCurrentProcess())DailyUpdateJson.Write(complete.Job,job with{ParentPid=self.Id,ParentStartTicks=self.StartTime.ToUniversalTime().Ticks});
            Age(complete.Directory,12);await DailyCacheStorage.CleanAsync(root);
            Check(!Directory.Exists(complete.Directory),"cleared "+state+" transaction does not depend on historical parent PID");
        }

        root=Area("locking");old=Download(root,"1.0.0",12);
        using(var download=new FileStream(Path.Combine(old,"download.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            var result=await DailyCacheStorage.CleanAsync(root);
            Check(result.Skipped>0&&File.Exists(Path.Combine(old,"package.zip")),"legacy download lock protects the whole version without partial deletion");
        }
        using(var update=DailyUpdateHost.AcquireStorage(root))
        {
            bool refused=false;try{await DailyCacheStorage.CleanAsync(root);}catch(IOException){refused=true;}
            Check(refused&&File.Exists(Path.Combine(old,"package.zip")),"updater/download storage lease excludes cleanup");
        }
        using(var cleanup=DailyCacheStorage.Lease(root,true))
        {
            bool refused=false;try{using var update=DailyUpdateHost.AcquireStorage(root);}catch(IOException){refused=true;}
            Check(refused,"cleanup excludes a newly starting updater/download");
        }
        await DailyCacheStorage.CleanAsync(root);Check(!Directory.Exists(old),"released lock permits cleanup");

        root=Area("cancel");old=Download(root,"1.0.0",12);
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();bool cancelled=false;try{await DailyCacheStorage.CleanAsync(root,cancel.Token);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled&&File.Exists(Path.Combine(old,"package.zip")),"cancelled maintenance does not delete data");
        }
    }
}
