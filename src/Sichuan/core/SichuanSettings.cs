using System.Text.Json;
namespace BD2Sichuan;
public sealed class SichuanSettings
{
    public int IntervalMilliseconds {get;set;}=1000;
    public static SichuanSettings Read(string path)
    {try{var s=JsonSerializer.Deserialize<SichuanSettings>(File.ReadAllText(path));return s!=null&&SichuanRunEngine.ValidInterval(s.IntervalMilliseconds)?s:new();}catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){return new();}}
    public void Save(string path)
    {
        if(!SichuanRunEngine.ValidInterval(IntervalMilliseconds))throw new ArgumentOutOfRangeException(nameof(IntervalMilliseconds),"请输入 50–60000 毫秒之间的整数。");
        SichuanJson.Write(path,this);
    }
}
public static class SichuanJson
{
    public static T? Read<T>(string path) where T:class
    {try{if(BD2.LocalIpc.DesktopFiles.Read(path,out var live))return live==null?null:JsonSerializer.Deserialize<T>(live);using var s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);return JsonSerializer.Deserialize<T>(s);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){return null;}}
    public static void Write<T>(string path,T value)
    {if(BD2.LocalIpc.DesktopFiles.Write(path,JsonSerializer.SerializeToUtf8Bytes(value)))return;var directory=Path.GetDirectoryName(path)!;Directory.CreateDirectory(directory);var temp=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");try{File.WriteAllText(temp,JsonSerializer.Serialize(value));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
}
