using System.Security.Cryptography;
using Microsoft.Win32;
using Dustweave;
using Dustweave.Accounts;
internal static class StorageMigrationNativeCases
{
    // Explicit opt-in, empty disposable sandbox; never opens the game or imports an account.
    internal static void Child(string box,string file,string mode)
    {
        if(!box.StartsWith("DWMoveTest_",StringComparison.Ordinal))throw new Exception("Fixture box required");
        SandboxProcessScope.Require(box);
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\DustweaveMigrationFixture");
        if(mode=="write") {key.SetValue("nonce",box);Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,box);}
        else if(key.GetValue("nonce") as string!=box||File.ReadAllText(file)!=box)throw new Exception("Fixture hive/data mismatch");
        File.WriteAllText(file+".result",mode);
    }
    internal static async Task Run(string output,List<string> cases)
    {
        if(Environment.GetEnvironmentVariable("DUSTWEAVE_NATIVE_STORAGE_TEST")!="1")return;
        string installation=DailySandbox.Installation()??throw new Exception("Sandboxie required for explicit native fixture");
        string id=Guid.NewGuid().ToString("N"),box="DWMoveTest_"+id[..16];
        string account=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
        string basis=Path.Combine(output,"native-storage"),root=Path.Combine(basis,"host"),store=Path.Combine(basis,"instances"),target=Path.Combine(basis,"moved");
        string state=Path.Combine(store,DailySandbox.CanonicalBoxName(account)),boxRoot=Path.Combine(state,"root");
        string file=Path.Combine(basis,"virtual","probe.txt");
        var transport=new StorageSandboxTransport();
        if(transport.Boxes().Contains(box,StringComparer.OrdinalIgnoreCase))throw new Exception("Fixture collision");
        Directory.CreateDirectory(root);
        async Task Launch(string mode)
        {
            await DailySandbox.Run(installation,"Start.exe",["/box:"+box,"/silent","/wait",Environment.ProcessPath!,"--storage-hive-child",box,file,mode],CancellationToken.None,TimeSpan.FromSeconds(45));
            for(int i=0;i<40&&!transport.Idle(box);i++)await Task.Delay(100);
            if(!transport.Idle(box))throw new Exception("Fixture still running");
        }
        try
        {
            await transport.Set(box,"FileRootPath",boxRoot);await transport.Set(box,"Enabled","y");
            DailyJson.Write(Path.Combine(state,"instance.json"),new DailySandboxRequest(box,1,account,"Synthetic",Path.Combine(basis,"BrownDust II.exe"),DateTimeOffset.UtcNow));
            await Launch("write");
            string Overlay(string from,string path)=>Path.Combine(from,"drive",path[..1].ToLowerInvariant(),path[3..]);
            if(!File.Exists(Overlay(boxRoot,file))||File.ReadAllText(Overlay(boxRoot,file)+".result")!="write")throw new Exception("Fixture write not isolated");
            if(File.Exists(file))throw new Exception("Fixture leaked onto host");
            await new DailyStorageMigration(root,store,transport).MoveAsync(target);
            string relocated=transport.Read(box,"FileRootPath");
            if(!relocated.StartsWith(target,StringComparison.OrdinalIgnoreCase))throw new Exception("Native root did not switch");
            await Launch("read");
            if(File.ReadAllText(Overlay(relocated,file)+".result")!="read")throw new Exception("Native hive did not survive move");
            cases.Add("storage-migration-native: real Sandboxie relocation preserves synthetic registry hive and writable data; no game or accounts accessed");
        }
        finally
        {
            if(!transport.Idle(box))throw new Exception("Retain running fixture for inspection: "+box);
            // Only the random test registration, never the user's sandbox definitions.
            await DailySandbox.Run(installation,"SbieIni.exe",["set",box,"*",""],CancellationToken.None);
            if(transport.Boxes().Contains(box,StringComparer.OrdinalIgnoreCase))throw new Exception("Fixture registration not removed");
        }
    }
}
