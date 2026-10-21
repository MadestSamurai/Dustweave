using Dustweave;
internal static class CacheStorageCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);cases.Add("cache-storage: "+label);}
        string root=Path.Combine(output,"storage");Directory.CreateDirectory(root);
        string Put(string relative,int size,int age=4){string file=Path.Combine(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllBytes(file,new byte[size]);File.SetLastWriteTimeUtc(file,DateTime.UtcNow.AddDays(-age));return file;}
        var durable=Put("accounts/session.json",75);Put("live/business/pending.json",25);
        Put("suite-cache/"+new string('a',64)+"/client/observer.dll",100,5);
        Put("suite-cache/"+new string('b',64)+"/client/observer.dll",100,4);
        Put("suite-cache/"+new string('c',64)+"/client/observer.dll",100,3);
        Put("suite-cache/modules/daily/a/client/module.dll",200,5);
        Put("suite-cache/modules/daily/b/client/module.dll",200,4);
        Put("suite-cache/modules/daily/c/client/module.dll",200,3);
        Put("trade/catalogs/a/tables/CookingTable.json",300,5);
        Put("trade/catalogs/b/tables/CookingTable.json",300,4);
        Put("trade/catalogs/c/tables/CookingTable.json",300,3);
        Put("rules/daily/a/table.json",20);Put("live/prepared/1/bridge.dll",20);Put("live/binding-cache/a.json",20);
        void Update(string version,string? state,int age)
        {
            string nonce=new string('a',32),directory=Path.Combine(root,"updates",version),attempt=Path.Combine(directory,"attempts",nonce);
            string jobPath=Put($"updates/{version}/attempts/{nonce}/job.json",0,age);
            var asset=new DailyUpdateAsset("Portable","package.zip",50,new string('0',64));
            var release=new DailyUpdateRelease(version.Replace("-Portable",""),null,2,[],[asset]);
            string target=Path.Combine(output,"update-install","Dustweave.exe");
            DailyJson.Write(jobPath,new DailyUpdateJob(directory,target,int.MaxValue,0,release,asset,nonce));
            File.SetLastWriteTimeUtc(jobPath,DateTime.UtcNow.AddDays(-age));
            Put($"updates/{version}/payload.zip",50,age);
            if(state!=null){string path=Put($"updates/{version}/attempts/{nonce}/backup/transaction.json",0,age);DailyJson.Write(path,new DailyUpdateJournal(Path.GetDirectoryName(target)!,Path.Combine(attempt,"backup"),Path.Combine(attempt,"staging"),[],state));File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddDays(-age));}
            else Put($"updates/{version}/attempts/{nonce}/backup/interrupted.bin",10,age);
        }
        Update("1.0.0-Portable","completed",10);Update("1.1.0-Portable","awaiting_start",9);Update("1.2.0-Portable",null,8);Update("1.3.0-Portable","rolled_back",7);Update("1.4.0-Portable","completed",1);
        var plan=await DailyCacheStorage.ScanAsync(root);
        long physical=Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Sum(f=>new FileInfo(f).Length);
        Check(plan.TotalBytes==physical,"counts all data, not only allowlisted logs");
        Check(plan.CacheBytes==physical-100 && plan.Units.Any(u=>u.Path.EndsWith(Path.Combine("trade","catalogs","a"))),"slash-delimited cache areas retain complete accounting and cleanup eligibility");
        Check(plan.Units.Count(u=>u.Path.Contains("updates"))==2,"old completed/rolled-back versions eligible; active and damaged recovery protected");
        Check(plan.Units.Count(u=>u.Path.Contains("suite-cache"))==2,"keeps two generations for each component");
        using(var inUse=new FileStream(Path.Combine(root,"trade/catalogs/a/tables/CookingTable.json"),FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            var clean=await DailyCacheStorage.CleanAsync(root);
            Check(clean.Bytes>0&&clean.Skipped>0&&File.Exists(inUse.Name),"cleanup skips files held by other processes");
        }
        using(var read1=DailyCacheStorage.Lease(root))using(var read2=DailyCacheStorage.Lease(root))
        {
            bool refused=false;try{await DailyCacheStorage.CleanAsync(root);}catch(IOException){refused=true;}
            Check(refused,"concurrent cache readers allowed; cleanup requires exclusive ownership");
        }
        await DailyCacheStorage.CleanAsync(root);
        Check(File.Exists(durable)&&File.Exists(Path.Combine(root,"live/business/pending.json")),"cleanup never touches identities or transaction evidence");
        var destination=Path.Combine(output,"migrated-cache");
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();bool cancelled=false;try{await DailyCacheStorage.MoveAsync(root,destination,cancel.Token);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled&&!Directory.Exists(destination)&&DailyCacheStorage.Location(root)==Path.GetFullPath(root),"cancelled migration retains original routing");
        }
        bool overlap=false;try{await DailyCacheStorage.MoveAsync(root,Path.Combine(root,"nested"));}catch(IOException){overlap=true;}
        Check(overlap,"nested destinations refused");
        var result=await DailyCacheStorage.MoveAsync(root,destination);
        Check(result.Bytes>0&&result.RetainedFiles==0&&DailyCacheStorage.Location(root)==destination,"verified copy switches routing and reclaims original cache");
        Check(!Directory.Exists(Path.Combine(root,"suite-cache"))&&File.Exists(durable)&&Directory.Exists(Path.Combine(root,"updates")),"migration leaves accounts and OTA recovery in place");
        Check(File.Exists(Path.Combine(DailyCacheStorage.Area(root,"rules"),"daily/a/table.json")),"all producers resolve migrated rules");
        plan=await DailyCacheStorage.ScanAsync(root);
        physical=new[]{root,destination}.Sum(d=>Directory.EnumerateFiles(d,"*",SearchOption.AllDirectories).Sum(f=>new FileInfo(f).Length));
        Check(plan.TotalBytes==physical,"combined accounting after migration has no omissions or duplicates");
        string foreign=Path.Combine(output,"foreign-cache");Directory.CreateDirectory(foreign);File.WriteAllText(Path.Combine(foreign,"keep.txt"),"unrelated");
        bool protectedFolder=false;try{await DailyCacheStorage.MoveAsync(root,foreign);}catch(IOException){protectedFolder=true;}
        Check(protectedFolder&&File.ReadAllText(Path.Combine(foreign,"keep.txt"))=="unrelated","existing destination contents are never replaced");
        bool exists=false;try{await DailyCacheStorage.MoveAsync(root,output);}catch(IOException){exists=true;}
        Check(exists,"existing unrelated destination not overwritten");
        // Hold a source against reads to fail midway; a failed copy cannot publish a new location.
        var locked=Directory.EnumerateFiles(destination,"*.dll",SearchOption.AllDirectories).First();
        using(var file=new FileStream(locked,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            bool failed=false;try{await DailyCacheStorage.MoveAsync(root,Path.Combine(output,"failed-cache"));}catch(IOException){failed=true;}
            Check(failed&&DailyCacheStorage.Location(root)==destination&&!Directory.Exists(Path.Combine(output,"failed-cache")),"copy failure keeps previous location and cleans its staging directory");
        }
        using(var cancel=new CancellationTokenSource())
        {
            var progress=new CancelProgress(cancel);
            bool cancelled=false;try{await DailyCacheStorage.MoveAsync(root,Path.Combine(output,"cancelled-cache"),cancel.Token,progress);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled&&DailyCacheStorage.Location(root)==destination&&!Directory.GetDirectories(output,"cancelled-cache*").Any(),"mid-copy cancellation preserves sources and removes temporary copy");
        }
        string second=Path.Combine(output,"second-cache");await DailyCacheStorage.MoveAsync(root,second);
        string late=Path.Combine(destination,"rules","late.json");Directory.CreateDirectory(Path.GetDirectoryName(late)!);File.WriteAllText(late,"late source writer");
        plan=await DailyCacheStorage.ScanAsync(root);
        Check(plan.Units.Any(u=>u.Files.Any(f=>f.Path==late)),"retired external locations remain visible and reclaimable after repeated migration");
        await DailyCacheStorage.CleanAsync(root);
        Check(!File.Exists(late)&&Directory.Exists(Path.Combine(second,"rules")),"retired source cleanup does not touch the active cache");
        // Complete inventory transactions stage at the version root, not inside
        // the individual attempt. Terminal repair journals must remain cleanable.
        foreach(bool repair in new[]{true,false})
        {
            string version=repair?"1.4.15-Lite":"1.4.16-Lite",nonce=new string('b',32);
            string directory=Path.Combine(root,"updates",version),attempt=Path.Combine(directory,"attempts",nonce);
            string target=Path.Combine(output,"inventory-install","Dustweave.exe");Directory.CreateDirectory(Path.Combine(attempt,"backup"));
            var asset=new DailyUpdateAsset("Lite","package.zip",50,new string('0',64));
            var release=new DailyUpdateRelease(version[..^5],null,2,[],[asset]);
            DailyJson.Write(Path.Combine(attempt,"job.json"),new DailyUpdateJob(directory,target,int.MaxValue,0,release,asset,nonce){Repair=repair,InventoryUpgrade=!repair});
            string journalPath=Path.Combine(attempt,"backup","transaction.json");
            var journal=new DailyUpdateJournal(Path.GetDirectoryName(target)!,Path.Combine(attempt,"backup"),Path.Combine(directory,"repair-staging"),[],"awaiting_start");
            var unit=new DailyCacheUnit(directory,[]){UpdateAttempts=[attempt]};
            DailyJson.Write(journalPath,journal);Check(!UpdateCacheRetention.CanClean(unit),"pending inventory recovery remains protected");
            DailyJson.Write(journalPath,journal with {State="completed"});Check(UpdateCacheRetention.CanClean(unit),"completed inventory recovery follows retention limits");
        }
    }
    private sealed class CancelProgress(CancellationTokenSource source):IProgress<long>{public void Report(long value)=>source.Cancel();}
}
