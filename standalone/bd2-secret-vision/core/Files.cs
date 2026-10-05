using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace BD2SecretVision;

public sealed class LocalFileException : IOException
{
    public string Operation { get; }
    public string FilePath { get; }
    public LocalFileException(string operation, string path, Exception cause)
        : base($"{operation}: {path}\n{cause.GetType().Name} (0x{cause.HResult:X8}): {cause.Message}", cause)
    { Operation=operation; FilePath=path; HResult=cause.HResult; }
}

public static class Files
{
    public const string LiveEntries="state.json|error.json|control.json|command.json|runtime.json|stop|receipt-*";
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BD2SecretVisionAssistant");
    public static readonly JsonSerializerOptions Options = new(){WriteIndented=true, PropertyNameCaseInsensitive=true};
    private static readonly object noteLock = new();
    public static bool IsFileError(Exception error) => error is IOException or UnauthorizedAccessException;
    private static bool Retryable(Exception error) => IsFileError(error) && (error.HResult & 0xffff) is 5 or 32 or 33 or 80 or 183 or 1175;
    public static T? Read<T>(string path)
    {
        try {
            if(BD2.LocalIpc.DesktopFiles.Read(path,out var live))return live==null?default:JsonSerializer.Deserialize<T>(live,Options);
            using var file=new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite|FileShare.Delete);
            return JsonSerializer.Deserialize<T>(file, Options);
        } catch(Exception error) when(IsFileError(error) || error is JsonException) { return default; }
    }
    // A reader may still hold the old version. Move(overwrite:true) can return
    // ERROR_ACCESS_DENIED even when the reader allows delete sharing on Windows.
    // Replace preserves atomic publication; never delete the old file first.
    public static void Write<T>(string path, T value)
    {
        if(BD2.LocalIpc.DesktopFiles.Write(path,JsonSerializer.SerializeToUtf8Bytes(value,Options)))return;
        path=Path.GetFullPath(path);
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        string operation="create temporary file";
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using(var file=new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                JsonSerializer.Serialize(file, value, Options); file.Flush(true);
            }
            operation="publish file";
            var elapsed=Stopwatch.StartNew();
            while(true) {
                try {
                    if(File.Exists(path)) File.Replace(temp, path, null);
                    else File.Move(temp, path); // create only; a racing publisher is retried
                    break;
                } catch(FileNotFoundException) when(File.Exists(temp) && !File.Exists(path) && elapsed.ElapsedMilliseconds<600) {
                    // Destination disappeared between Exists and Replace.
                } catch(Exception error) when(Retryable(error) && File.Exists(temp) && elapsed.ElapsedMilliseconds<600) {
                    Thread.Sleep(20);
                }
            }
        } catch(Exception error) when(IsFileError(error)) {
            throw new LocalFileException(operation, path, error);
        } finally {
            // A cleanup failure must not hide the publication failure.
            try { File.Delete(temp); } catch(Exception error) when(IsFileError(error)) { }
        }
    }
    // Diagnostics are best effort and must never terminate gameplay or renewal.
    public static bool Note(string path, object value)
    {
        lock(noteLock) {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                if(File.Exists(path) && new FileInfo(path).Length>4_000_000) {
                    if(File.Exists(path+".previous")) File.Replace(path,path+".previous",null);
                    else File.Move(path,path+".previous");
                }
                using var file=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read|FileShare.Delete);
                using var writer=new StreamWriter(file);
                writer.WriteLine(JsonSerializer.Serialize(value));
                return true;
            } catch(Exception error) when(IsFileError(error)) { return false; }
        }
    }
    public static void Diagnose(string root,string phase,Exception error) => Note(Path.Combine(root,"diagnostics.jsonl"),new {
        AtUtc=DateTimeOffset.UtcNow,Phase=phase,Type=error.GetType().FullName,
        HResult=$"0x{error.HResult:X8}",Path=(error as LocalFileException)?.FilePath,
        Operation=(error as LocalFileException)?.Operation,Error=error.ToString()
    });
}

public static class ErrorKind
{
    public static string Status(Exception error)
    {
        if(Files.IsFileError(error)) return "file-access";
        for(Exception? current=error;current!=null;current=current.InnerException)
            if(current is System.ComponentModel.Win32Exception {NativeErrorCode:5}) return "permission";
        return "blocked";
    }
}
public static class JsonExtensions {public static string S(this JsonNode? n,string k)=>n?[k]?.ToString()??"";public static double D(this JsonNode? n,string k)=>double.TryParse(n.S(k),System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out var v)?v:0;public static bool B(this JsonNode? n,string k)=>bool.TryParse(n.S(k),out var v)&&v;}
