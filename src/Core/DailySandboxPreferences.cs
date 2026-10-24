using System.Text.Json;

namespace Dustweave;

public enum DailySandboxFileMode { Synchronized, Compatible }

// One host preference for every account and launch entry. Isolated windows do
// not own or edit this setting.
public sealed record DailySandboxPreferences(int Schema = 1, DailySandboxFileMode Mode = DailySandboxFileMode.Synchronized)
{
    internal static string FilePath(string state) => Path.Combine(state,"launch-mode.json");
    private static readonly object gate=new();
    public static DailySandboxPreferences Read(string? dataRoot=null,string? legacyStore=null) => ReadGlobal(dataRoot??DailyIdentity.DataRoot,legacyStore??DailySandbox.Store);
    internal static DailySandboxPreferences ReadGlobal(string dataRoot,string legacyStore)
    {
        string state=Path.Combine(dataRoot,"sandbox");
        lock(gate) {
            if(File.Exists(FilePath(state)))return ReadDirectory(state);
            // The first local build stored account choices. Preserve an explicit
            // compatibility choice once, then the global file always wins.
            var result=new DailySandboxPreferences();
            if(Directory.Exists(legacyStore))foreach(string directory in Directory.EnumerateDirectories(legacyStore,"Dustweave_*")) {
                if(!File.Exists(FilePath(directory)))continue;
                if(ReadDirectory(directory).Mode==DailySandboxFileMode.Compatible)result=result with{Mode=DailySandboxFileMode.Compatible};
            }
            result.SaveDirectory(state);return result;
        }
    }
    public void Save(string? dataRoot=null)
    {
        DailySandbox.RequireHost();lock(gate)SaveDirectory(Path.Combine(dataRoot??DailyIdentity.DataRoot,"sandbox"));
    }
    internal static DailySandboxPreferences ReadDirectory(string state)
    {
        string path=FilePath(state);
        if (!File.Exists(path)) return new();
        DailyCacheStorage.NoLinks(path);
        try {
            if(new FileInfo(path).Length>4096) throw new InvalidDataException();
            var value=JsonSerializer.Deserialize<DailySandboxPreferences>(File.ReadAllText(path),DailyJson.Options);
            if(value?.Schema!=1 || !Enum.IsDefined(value.Mode))throw new InvalidDataException();
            return value;
        } catch(Exception error) when(error is JsonException or InvalidDataException) { throw new InvalidDataException("sandbox.manage.mode_invalid",error); }
    }
    internal void SaveDirectory(string state)
    {
        if(Schema!=1 || !Enum.IsDefined(Mode))throw new InvalidDataException("sandbox.manage.mode_invalid");
        DailyCacheStorage.NoLinks(state);Directory.CreateDirectory(state);
        string path=FilePath(state),temp=path+"."+Guid.NewGuid().ToString("N");
        DailyCacheStorage.NoLinks(path);
        try { using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {JsonSerializer.Serialize(file,this,DailyJson.Options);file.Flush(true);}File.Move(temp,path,true); }
        finally { if(File.Exists(temp))File.Delete(temp); }
    }
}
