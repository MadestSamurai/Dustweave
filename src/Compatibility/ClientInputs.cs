using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;

namespace Dustweave.Compatibility;

public sealed record ClientInputFile(string Name, long Bytes, string Sha256);
public sealed record ClientInputSnapshot(string Key, string MainMvid, IReadOnlyList<ClientInputFile> Files);

// All compiler references participate, including login SDK, Unity and mscorlib.
// This is run only when preparing/connecting, never in the observation loop.
public static class ClientInputs
{
    public static ClientInputSnapshot ReadManaged(string managed)
    {
        managed = Path.GetFullPath(managed);
        var before = Stamp(managed);
        if (!before.Any(x => x.Name.Equals("Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase)) ||
            !before.Any(x => x.Name.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("client_update.incomplete");
        var files = before.Select(x => HashFile(Path.Combine(managed,x.Name),x.Name)).ToArray();
        string mvid;
        using (var module = ModuleDefinition.ReadModule(Path.Combine(managed,"Assembly-CSharp.dll"))) mvid = module.Mvid.ToString("N");
        if (!before.SequenceEqual(Stamp(managed))) throw new IOException("client_update.changing");
        return new(Key(files),mvid,files);
    }

    public static string ReadGame(string executable)
    {
        executable = Path.GetFullPath(executable);
        if (!Path.GetFileName(executable).Equals("BrownDust II.exe",StringComparison.OrdinalIgnoreCase) || !File.Exists(executable))
            throw new InvalidDataException("client_update.incomplete");
        string directory = Path.GetDirectoryName(executable)!;
        var managed = ReadManaged(Path.Combine(directory,"BrownDust II_Data","Managed"));
        var files = managed.Files.Select(x => x with { Name="Managed/"+x.Name }).ToList();
        foreach (string name in new[]{"BrownDust II.exe","UnityPlayer.dll","MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll"})
        {
            string path=Path.Combine(directory,name.Replace('/',Path.DirectorySeparatorChar));
            if (!File.Exists(path)) throw new InvalidDataException("client_update.incomplete");
            files.Add(HashFile(path,name));
        }
        return Key(files);
    }

    public static void RequireSame(string expected,string actual)
    {
        if (expected.Length!=64 || !expected.Equals(actual,StringComparison.Ordinal))
            throw new InvalidDataException("client_update.isolated_mismatch");
    }
    public static void RequireStable(string managed,string expected)
    {
        if(ReadManaged(managed).Key!=expected)throw new IOException("client_update.changing");
    }
    public static void RequireRunning(string previous,int previousId,long previousStart,string current,int id,long start)
    {
        if(previous.Length>0 && previousId==id && previousStart==start && previous!=current)
            throw new InvalidOperationException("client_update.running_changed");
    }
    private static ClientInputFile HashFile(string path,string name)
    {
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        return new(name,file.Length,Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant());
    }
    private static string Key(IEnumerable<ClientInputFile> files)
    {
        string text="client-inputs-v2\n"+string.Join("\n",files.OrderBy(x=>x.Name,StringComparer.OrdinalIgnoreCase)
            .Select(x=>x.Name.ToLowerInvariant()+"|"+x.Bytes+"|"+x.Sha256));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
    private sealed record FileStamp(string Name,long Bytes,long Modified);
    private static FileStamp[] Stamp(string directory) => Directory.EnumerateFiles(directory,"*.dll")
        .Select(x=>new FileInfo(x)).Select(x=>new FileStamp(x.Name,x.Length,x.LastWriteTimeUtc.Ticks))
        .OrderBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).ToArray();
}
