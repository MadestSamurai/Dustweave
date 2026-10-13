using System.Text.Json;
namespace Dustweave;

// Update journals must work before loading business assemblies and without IPC.
public static class DailyUpdateJson
{
    public static readonly JsonSerializerOptions Options = new() { IncludeFields = true, WriteIndented = true };
    public static T? TryRead<T>(string path) where T : class
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return JsonSerializer.Deserialize<T>(stream, Options); }
            catch (JsonException) { return null; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { if (attempt == 5 || (error.HResult & 0xffff) is not (2 or 5 or 32 or 33)) return null; Thread.Sleep(5 * (attempt + 1)); }
        }
        return null;
    }
    public static void Write<T>(string path, T value)
    {
        DailyUpdates.EnsureNoLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, value, Options); stream.Flush(true); }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try { if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); return; }
            catch (Exception error) when ((error is IOException or UnauthorizedAccessException) && (error.HResult & 0xffff) is (5 or 32 or 33 or 1175) && watch.ElapsedMilliseconds < 2000)
            { Thread.Sleep(20); }
        }
    }
}
