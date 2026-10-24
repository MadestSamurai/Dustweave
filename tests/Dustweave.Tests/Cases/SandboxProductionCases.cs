using Dustweave;

static class SandboxProductionCases
{
    public static async Task Run(string output,List<string> cases)
    {
        void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);cases.Add(message);}
        async Task Reject(Func<Task> operation,string message){try{await operation();}catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException){cases.Add(message);return;}throw new Exception(message);}
        string directory=Path.Combine(output,"sandbox-production"),host=Path.Combine(directory,"游戏"),exe=Path.Combine(host,"BrownDust II.exe"),root=Path.Combine(directory,"box","root"),backup=Path.Combine(directory,"box","program-backup");
        Directory.CreateDirectory(host);File.WriteAllText(exe,"new exe");
        string data=Path.Combine(host,"BrownDust II_Data");Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"level7.resS"),"shipped");
        var configuration=DailySandboxGameFiles.LegacyRules(exe).Append(@"C:\Other\*").ToList();
        Task<string> Read()=>Task.FromResult(string.Join('\n',configuration));
        Task Append(string rule){configuration.Add(rule);return Task.CompletedTask;}
        Task Remove(string rule){configuration.RemoveAll(x=>x.Equals(rule,StringComparison.OrdinalIgnoreCase));return Task.CompletedTask;}
        Task Apply(DailySandboxFileMode mode,Func<Task<bool>>? idle=null,Func<string,Task>? remove=null)=>DailySandboxGameFiles.EnsureAsync(exe,Read,idle??(()=>Task.FromResult(true)),Append,_=>{},default,remove??Remove,mode);
        await Reject(()=>Apply(DailySandboxFileMode.Synchronized,()=>Task.FromResult(false)),"Legacy wide cache rule migration waits for an idle sandbox");
        Check(configuration.Contains(Path.Combine(data,"*")),"A busy migration has not partially altered running rules");
        await Apply(DailySandboxFileMode.Synchronized);
        Check(!configuration.Contains(Path.Combine(data,"*"))&&configuration.Contains(Path.Combine(data,"level7.resS")),"1.4.17 wide rule removed; only exact shipped asset rules retained");
        Check(configuration.Contains(@"C:\Other\*"),"Migration preserves unrelated configuration");
        Check(!DailySandboxGameFiles.Rules(exe).Any(x=>x.Contains("Addressable",StringComparison.OrdinalIgnoreCase)),"Synchronized mode leaves writable Addressable entirely outside read-only mapping");
        await Apply(DailySandboxFileMode.Compatible);
        Check(configuration.SequenceEqual(new[]{@"C:\Other\*"}),"Compatibility removes all our mappings rather than layering another conflicting rule");
        await Apply(DailySandboxFileMode.Synchronized);
        await Apply(DailySandboxFileMode.Synchronized,()=>throw new Exception("unnecessary rewrite"));
        cases.Add("Repeated launch with unchanged rules avoids unnecessary reconfiguration");
        await Reject(()=>Apply(DailySandboxFileMode.Compatible,remove:_=>Task.CompletedTask),"Lost deletion is detected before a game can start");
        string preferences=Path.Combine(directory,"preferences");
        Check(DailySandboxPreferences.ReadDirectory(preferences).Mode==DailySandboxFileMode.Synchronized,"New accounts default to fixed synchronized mode");
        new DailySandboxPreferences(Mode:DailySandboxFileMode.Compatible).SaveDirectory(preferences);
        Check(DailySandboxPreferences.ReadDirectory(preferences).Mode==DailySandboxFileMode.Compatible,"Account mode survives a process restart");
        File.WriteAllText(DailySandboxPreferences.FilePath(preferences),"{\"Schema\":1,\"Mode\":78}");
        await Reject(()=>Task.Run(()=>DailySandboxPreferences.ReadDirectory(preferences)),"Unknown preference mode never silently selects a different isolation strategy");
        string global=Path.Combine(directory,"global-preferences"),legacy=Path.Combine(directory,"legacy-instances");
        Check(DailySandboxPreferences.ReadGlobal(global,legacy).Mode==DailySandboxFileMode.Synchronized,"Global sandbox mode defaults to synchronized without any account");
        string migrated=Path.Combine(directory,"migrated-preferences"),legacyAccount=Path.Combine(legacy,"Dustweave_legacy");
        new DailySandboxPreferences(Mode:DailySandboxFileMode.Compatible).SaveDirectory(legacyAccount);
        Check(DailySandboxPreferences.ReadGlobal(migrated,legacy).Mode==DailySandboxFileMode.Compatible,"Existing compatibility choice is preserved once as a global preference");
        new DailySandboxPreferences().SaveDirectory(Path.Combine(migrated,"sandbox"));
        Check(DailySandboxPreferences.ReadGlobal(migrated,legacy).Mode==DailySandboxFileMode.Synchronized,"Old per-account modes cannot override a later global choice");
        File.WriteAllText(DailySandboxPreferences.FilePath(Path.Combine(migrated,"sandbox")),"broken");
        await Reject(()=>Task.Run(()=>DailySandboxPreferences.ReadGlobal(migrated,legacy)),"Invalid global preferences are reported, not replaced by old account modes");
        new DailySandboxPreferences(Mode:DailySandboxFileMode.Compatible).SaveDirectory(Path.Combine(migrated,"sandbox"));
        Check(DailySandboxPreferences.ReadGlobal(migrated,legacy).Mode==DailySandboxFileMode.Compatible,"Explicit mode selection repairs corrupt global settings");
        Check(Dustweave.Accounts.SandboxConfiguration.ReadProcesses(buffer=>0).Length==0,"Native process probe accepts a verified empty sandbox");
        Check(Dustweave.Accounts.SandboxConfiguration.ReadProcesses(buffer=>{buffer[0]=2;buffer[1]=100;buffer[2]=200;return 0;}).SequenceEqual(new[]{100,200}),"Native process probe excludes the count slot");
        foreach(int invalid in new[]{-1,511,512})await Reject(()=>Task.Run(()=>Dustweave.Accounts.SandboxConfiguration.ReadProcesses(buffer=>{buffer[0]=invalid;return 0;})),"Invalid or saturated native process count cannot authorize mutation: "+invalid);
        await Reject(()=>Task.Run(()=>Dustweave.Accounts.SandboxConfiguration.ReadProcesses(buffer=>-1)),"Failed native process query must not be treated as idle");
        await Reject(()=>Task.Run(()=>Dustweave.Accounts.SandboxConfiguration.ReadProcesses(buffer=>{buffer[0]=1;return 0;})),"Missing native process ID cannot authorize mutation");
        await Reject(()=>Task.Run(()=>Dustweave.Accounts.SandboxConfiguration.ReadProcesses(buffer=>{buffer[0]=2;buffer[1]=100;buffer[2]=100;return 0;})),"Duplicate native process IDs are rejected");
        Check(DailySandboxProcesses.Parse("\uFEFF0\r\n").Length==0,"Idle process response handles BOM and newlines");
        Check(DailySandboxProcesses.Parse("2\r\n100\r\n200\r\n").SequenceEqual(new[]{100,200}),"Process parser does not mistake the count for a process ID");
        foreach(string malformed in new[]{"","0\n7","2\n100","1\n-1","1\nword","2\n100\n100"})
            await Reject(()=>Task.Run(()=>DailySandboxProcesses.Parse(malformed)),"Malformed process response cannot authorize changes: "+malformed.Replace('\n','/'));
        Check(DailySandboxProgramSync.IsProgram(@"BrownDust II_Data\Managed\Assembly-CSharp.dll")&&DailySandboxProgramSync.IsProgram(@"BrownDust II_Data\level22.resS"),"Program classification covers assemblies and future numbered Unity levels");
        Check(DailySandboxProgramSync.IsProgram(@"BrownDust II_Data\StreamingAssets\aa\settings.json")&&DailySandboxProgramSync.IsProgram(@"BrownDust II_Data\StreamingAssets\DefaultDB.db"),"Cross-version synchronization includes shipped streaming catalogs and initial database");
        Check(!DailySandboxProgramSync.IsProgram(@"BrownDust II_Data\StreamingAssets\crashreporter.info"),"Runtime crash reporter metadata stays writable");
        foreach(string relative in new[]{@"BrownDust II_Data\Addressable\__lock",@"BrownDust II_Data\Addressable\__info","launcher.settings",@"LocalLow\Neo\token","game.log"})
            Check(!DailySandboxProgramSync.IsProgram(relative),"Compatibility keeps mutable path: "+relative);
        string overlay=DailySandboxProgramSync.Overlay(root,exe);
        void Put(string baseDir,string relative,string value){string p=Path.Combine(baseDir,relative);Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllText(p,value);}
        Put(overlay,"BrownDust II.exe","stale exe");Put(overlay,@"BrownDust II_Data\Managed\removed.dll","old assembly");
        Put(overlay,@"BrownDust II_Data\Addressable\__lock","lock");Put(overlay,"launcher.settings","private setting");
        Put(overlay,@"BrownDust II_Data\level7.resS","old asset");
        await Reject(()=>DailySandboxProgramSync.SynchronizeAsync(exe,root,backup,()=>Task.FromResult(false),_=>{},default),"Stale program overlay is never changed while the box runs");
        int count=await DailySandboxProgramSync.SynchronizeAsync(exe,root,backup,()=>Task.FromResult(true),_=>{},default);
        Check(count==3&&!File.Exists(Path.Combine(overlay,"BrownDust II.exe"))&&!File.Exists(Path.Combine(overlay,@"BrownDust II_Data\Managed\removed.dll")),"Compatibility exposes host updates and removes stale deleted assemblies");
        Check(File.ReadAllText(Path.Combine(backup,"BrownDust II.exe"))=="stale exe"&&File.ReadAllText(exe)=="new exe","Retirement retains a backup without writing to the host installation");
        Check(File.ReadAllText(Path.Combine(overlay,@"BrownDust II_Data\Addressable\__lock"))=="lock"&&File.ReadAllText(Path.Combine(overlay,"launcher.settings"))=="private setting","Compatibility preserves private cache locks and channel settings");
        Check(await DailySandboxProgramSync.SynchronizeAsync(exe,root,backup,()=>throw new Exception("not idle needed"),_=>{},default)==0,"Second launch performs no repeated retirement");
        string key=new('c',64),owned=DailySandbox.StateDirectory(key);
        var request=new DailySandboxRequest(DailySandbox.CanonicalBoxName(key),1,key,"Synthetic account",exe,DateTimeOffset.UtcNow);
        DailySandbox.ValidateManagedRoot(key,owned,Path.Combine(owned,"root"),request);
        await Reject(()=>Task.Run(()=>DailySandbox.ValidateManagedRoot(key,owned,host,request)),"Removal rejects a root outside the account's managed directory");
        await Reject(()=>Task.Run(()=>DailySandbox.ValidateManagedRoot(key,owned,Path.Combine(owned,"root"),request with{Account=new string('d',64)})),"Removal rejects another account's ownership marker");
        await Reject(()=>Task.Run(()=>DailySandbox.ValidateManagedRoot(key,owned,Path.Combine(owned,"root"),request with{Box="AnotherBox"})),"Removal rejects a marker belonging to a different box");
    }
}
