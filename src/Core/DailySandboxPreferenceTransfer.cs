using System.Diagnostics;
using System.Security.Cryptography;
using Dustweave.Accounts;
using Dustweave.Compatibility;

namespace Dustweave;

public enum GamePreferenceDirection { ToSandbox, ToComputer }
public sealed record GamePreferenceTransferResult(int Tables,int Settings,string BackupDirectory);
public static class DailySandboxPreferenceTransfer
{
    public const string HelperSwitch="--sandbox-game-preferences";
    private sealed record Request(int Schema,string Nonce,string Account,string Player,string Game,GamePreferenceParts Parts,GamePreferenceSnapshot? Snapshot);
    private sealed record Response(string Nonce,GamePreferenceSnapshot? Snapshot,GamePreferenceTransferResult? Result,string Error);
    private static string LocalRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2DailyAssistant","game-preferences");
    private static string Notice(string account)=>Path.Combine(DailySandbox.StateDirectory(account),"game-preferences-notice.json");
    public static bool ShouldSuggest(string account)=>DailyProfiles.ValidKey(account)&&!File.Exists(Notice(account));
    public static void DismissSuggestion(string account)
    {
        DailySandbox.RequireHost();if(!DailyProfiles.ValidKey(account))throw new InvalidDataException("sandbox.preferences.identity");
        DailyJson.Write(Notice(account),new{schema=1,atUtc=DateTimeOffset.UtcNow});
    }
    public static bool HasKnownPlayer(string account)=>DailyProfiles.ValidKey(Player(account));
    private static string Player(string account)=>new DailyProfiles(DailyIdentity.DataRoot).Read().SingleOrDefault(p=>p.AccountKey==account)?.PlayerKey??"";
    public static async Task<GamePreferenceTransferResult> CopyAsync(DailyAccount account,GamePreferenceDirection direction,GamePreferenceParts parts,string executable,CancellationToken token)
    {
        DailySandbox.RequireHost();if(!account.Valid||!Enum.IsDefined(direction))throw new InvalidDataException("sandbox.preferences.identity");
        if(parts==0||((int)parts&~7)!=0)throw new InvalidDataException("sandbox.preferences.invalid");
        string player=Player(account.AccountKey);
        if(parts.HasFlag(GamePreferenceParts.Personal)&&!DailyProfiles.ValidKey(player))throw new InvalidDataException("sandbox.preferences.identity");
        RequireGamesClosed(account.AccountKey);
        // Create the managed space only after the user requests a transfer. This
        // prepares directories/rules; it never signs in, starts a game or launches a queue.
        var info=await DailySandbox.InspectSpaceAsync(account.AccountKey,token);
        if(!info.Exists)await DailySandbox.CreateSpaceAsync(account,executable,_=>{},token);
        await DailySandbox.ConfigurationGate.WaitAsync(token);
        try {
            info=await DailySandbox.InspectSpaceAsync(account.AccountKey,token);
            if(!info.Exists||info.Running||info.Issue.Length!=0)throw new InvalidOperationException(info.Issue.Length>0?info.Issue:"sandbox.preferences.busy");
            RequireGamesClosed(account.AccountKey);
            string[] privatePaths=(await DailySandbox.Run(DailySandbox.Installation()!,"SbieIni.exe",["query",info.Box,"WriteFilePath"],token))
                .Split(['\r','\n'],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
            if(!privatePaths.Contains(LocalRoot,StringComparer.OrdinalIgnoreCase))
                await DailySandbox.Run(DailySandbox.Installation()!,"SbieIni.exe",["append",info.Box,"WriteFilePath",LocalRoot],token);
            string game=GameLauncher.ResolveExecutable(),managed=Path.Combine(Path.GetDirectoryName(game)!,"BrownDust II_Data","Managed");
            string nonce=Guid.NewGuid().ToString("N"),work=Path.Combine(LocalRoot,"transfers",nonce);
            DailyCacheStorage.NoLinks(work);Directory.CreateDirectory(work);
            try {
                GamePreferenceSnapshot snapshot;
                if(direction==GamePreferenceDirection.ToSandbox) {
                    using(var registry=DailyGamePreferenceStore.OpenRegistry(false))snapshot=DailyGamePreferenceStore.Export(account.AccountKey,player,parts,managed,DailyGamePreferenceStore.DataDirectory,work,registry);
                    var result=await Remote(info,new(1,nonce,account.AccountKey,player,game,parts,snapshot),executable,token);
                    return result.Result??throw new IOException("sandbox.preferences.failed");
                }
                else {
                    var result=await Remote(info,new(1,nonce,account.AccountKey,player,game,parts,null),executable,token);
                    snapshot=result.Snapshot??throw new IOException("sandbox.preferences.failed");
                    if(snapshot.Account!=account.AccountKey||snapshot.Player!=player||snapshot.Parts!=parts)throw new InvalidDataException("sandbox.preferences.identity");
                    token.ThrowIfCancellationRequested();RequireGamesClosed(account.AccountKey);
                    using var registry=DailyGamePreferenceStore.OpenRegistry(true);
                    return Apply(snapshot,managed,DailyGamePreferenceStore.DataDirectory,work,registry,()=>RequireGamesClosed(account.AccountKey));
                }
            } finally {CleanWork(work);}
        } finally {DailySandbox.ConfigurationGate.Release();}
    }
    private static async Task<Response> Remote(DailySandboxInfo info,Request request,string executable,CancellationToken token)
    {
        string relative=Path.Combine("user","current","AppData","Local","BD2DailyAssistant","game-preferences","transfers",request.Nonce);
        string directory=Path.Combine(info.Root,relative);DailyCacheStorage.NoLinks(directory);Directory.CreateDirectory(directory);
        string input=Path.Combine(directory,"request.json"),output=Path.Combine(directory,"response.json");DailyJson.Write(input,request);
        try {
            token.ThrowIfCancellationRequested();
            // Once dispatched, await the receipt even if the window is closing.
            // Killing the relay midway through a write would leave the result unknown.
            await DailySandbox.Run(DailySandbox.Installation()!,"Start.exe",["/box:"+info.Box,"/silent","/wait",Path.GetFullPath(executable),HelperSwitch,request.Account,request.Nonce],CancellationToken.None,TimeSpan.FromMinutes(2));
            var response=DailyJson.TryRead<Response>(output);
            if(response?.Nonce!=request.Nonce)throw new IOException("sandbox.preferences.failed");
            if(response.Error.Length>0)throw new IOException(response.Error);
            return response;
        } finally {CleanWork(directory);}
    }
    public static void RunHelper(string account,string nonce)
    {
        if(!DailyProfiles.ValidKey(account)||!Guid.TryParseExact(nonce,"N",out _))throw new InvalidDataException("sandbox.preferences.invalid");
        SandboxProcessScope.Require(DailySandbox.BoxName(account));
        string work=Path.Combine(LocalRoot,"transfers",nonce),input=Path.Combine(work,"request.json"),output=Path.Combine(work,"response.json");
        DailyCacheStorage.NoLinks(input);
        if(!File.Exists(input)||new FileInfo(input).Length>32*1024*1024)throw new InvalidDataException("sandbox.preferences.invalid");
        var request=DailyJson.TryRead<Request>(input)??throw new InvalidDataException("sandbox.preferences.invalid");
        Response response;
        try {
            if(request.Schema!=1||request.Account!=account||request.Nonce!=nonce||!File.Exists(request.Game))throw new InvalidDataException("sandbox.preferences.identity");
            RequireGamesClosed(account);string managed=Path.Combine(Path.GetDirectoryName(request.Game)!,"BrownDust II_Data","Managed");
            if(request.Snapshot is {} snapshot) {
                if(snapshot.Account!=account||snapshot.Player!=request.Player||snapshot.Parts!=request.Parts)throw new InvalidDataException("sandbox.preferences.identity");
                using var registry=DailyGamePreferenceStore.OpenRegistry(true);
                response=new(nonce,null,Apply(snapshot,managed,DailyGamePreferenceStore.DataDirectory,work,registry,()=>RequireGamesClosed(account)),"");
            } else {
                using var registry=DailyGamePreferenceStore.OpenRegistry(false);
                response=new(nonce,DailyGamePreferenceStore.Export(account,request.Player,request.Parts,managed,DailyGamePreferenceStore.DataDirectory,work,registry),null,"");
            }
        } catch(Exception error) {response=new(nonce,null,null,error.Message.StartsWith("sandbox.preferences.",StringComparison.Ordinal)?error.Message:"sandbox.preferences.failed");}
        DailyJson.Write(output,response);
    }
    internal static GamePreferenceTransferResult Apply(GamePreferenceSnapshot source,string managed,string data,string work,Microsoft.Win32.RegistryKey registry,Action requireIdle,string? backupRoot=null)
    {
        DailyGamePreferenceStore.Validate(source);requireIdle();
        byte[]? before=null,after=null;string? target=null;
        if(source.Parts.HasFlag(GamePreferenceParts.Personal)) {
            var codec=GamePreferenceCodec.Read(managed);target=Path.Combine(data,codec.FileName);
            DailyGamePreferenceStore.RequireClosedDatabase(target);before=DailyGamePreferenceStore.Read(target);
            after=DailyGamePreferenceStore.PrepareDatabase(source,before,codec,work);
        }
        var oldValues=DailyGamePreferenceStore.ReadValues(registry,source.Parts,source.Owner);
        var previous=source with{Tables=[],Values=oldValues};
        string backup=Path.Combine(backupRoot??Path.Combine(LocalRoot,"backups"),source.Account,DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N"));
        DailyCacheStorage.NoLinks(backup);Directory.CreateDirectory(backup);
        DailyJson.Write(Path.Combine(backup,"settings.json"),previous);
        if(before!=null)File.WriteAllBytes(Path.Combine(backup,"database.bin"),before);
        requireIdle();
        if(!DailyGamePreferenceStore.ValuesEqual(oldValues,DailyGamePreferenceStore.ReadValues(registry,source.Parts,source.Owner))||
            target!=null&&!DailyGamePreferenceStore.Read(target).AsSpan().SequenceEqual(before))throw new IOException("sandbox.preferences.changed");
        bool databaseWritten=false,registryStarted=false;
        try {
            if(target!=null){databaseWritten=true;Replace(target,after!);}
            registryStarted=true;DailyGamePreferenceStore.WriteValues(registry,source);
            DailyJson.Write(Path.Combine(backup,"result.json"),new{state="completed",atUtc=DateTimeOffset.UtcNow,source.Parts,tables=source.Tables.Length,settings=source.Values.Length});
            return new(source.Tables.Length,source.Values.Length,backup);
        } catch {
            try {
                if(databaseWritten)Replace(target!,before!);
                if(registryStarted&&!DailyGamePreferenceStore.ValuesEqual(oldValues,DailyGamePreferenceStore.ReadValues(registry,source.Parts,source.Owner)))
                    DailyGamePreferenceStore.WriteValues(registry,previous);
                DailyJson.Write(Path.Combine(backup,"result.json"),new{state="rolled-back",atUtc=DateTimeOffset.UtcNow});
            }catch {throw new IOException("sandbox.preferences.restore_failed");}
            throw;
        }
    }
    private static void Replace(string path,byte[] bytes)
    {
        DailyCacheStorage.NoLinks(path);string temp=path+".dustweave-"+Guid.NewGuid().ToString("N");
        try {using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes);stream.Flush(true);}
            File.Move(temp,path,true);
            if(!DailyGamePreferenceStore.Read(path).AsSpan().SequenceEqual(bytes))throw new IOException("sandbox.preferences.verify");
        }finally {if(File.Exists(temp))File.Delete(temp);}
    }
    private static void RequireGamesClosed(string account)
    {
        string expected=DailySandbox.BoxName(account);
        foreach(string name in new[]{"BrownDust II","Browndust2Starter"})foreach(var p in Process.GetProcessesByName(name))using(p) {
            string scope;try {scope=SandboxProcessScope.BoxOf(p.Id);}catch(IOException)when(p.HasExited){continue;}
            if(scope.Length==0||scope.Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("sandbox.preferences.busy");
        }
    }
    private static void CleanWork(string directory)
    {
        // Only fixed transfer leaves owned by this operation; no recursive cleanup.
        foreach(string name in new[]{"request.json","response.json","source.db","target.db","target.db-journal"}) {
            string file=Path.Combine(directory,name);try{DailyCacheStorage.NoLinks(file);if(File.Exists(file))File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        try{if(Directory.Exists(directory)&&!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
