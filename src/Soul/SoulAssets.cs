using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Dustweave.Soul;

internal static class SoulAssets
{
    internal const string ClientMvid = "6ef9a350-0ae8-4c96-ad55-9ad3e2d26a9c";
    internal static string Root { get; private set; } = "";
    internal static string Tables => Path.Combine(Root,"tables");
    internal static string Library => Path.Combine(Root,"bd2_inventory_battle.dll");
    internal static string MetadataPath => Path.Combine(Root,"client-metadata.json");
    internal static string ClientPath { get; private set; } = "";
    internal static string Text(string name) { using var stream=typeof(SoulAssets).Assembly.GetManifestResourceStream(name)??throw new FileNotFoundException(name);using var reader=new StreamReader(stream);return reader.ReadToEnd(); }
    internal static void Prepare(string root,string client)
    {
        using(var module=Mono.Cecil.ModuleDefinition.ReadModule(client))
            if(module.Mvid.ToString()!=ClientMvid)throw new InvalidDataException("死魂之盘的游戏规则已更新，请更新工具后再开始。 / Soul rules changed; update Dustweave first.");
        ClientPath=client;
        Root=Path.Combine(DailyCacheStorage.Area(root,"suite-cache"),"soul",typeof(SoulAssets).Module.ModuleVersionId.ToString("N"));
        Directory.CreateDirectory(Tables);
        var assembly=typeof(SoulAssets).Assembly;
        foreach(var name in assembly.GetManifestResourceNames().Where(n=>n.StartsWith("Soul.assets.")||n.StartsWith("Soul.tables.")))
        {
            using var stream=assembly.GetManifestResourceStream(name)!;using var bytes=new MemoryStream();stream.CopyTo(bytes);var content=bytes.ToArray();
            string path=Path.Combine(name.StartsWith("Soul.tables.")?Tables:Root,name[12..]);
            if(File.Exists(path)&&SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(SHA256.HashData(content)))continue;
            File.WriteAllBytes(path+".pending",content);File.Move(path+".pending",path,true);
        }
        using(var compressed=File.OpenRead(Path.Combine(Root,"campaign-library.json.gz")))
        using(var gz=new GZipStream(compressed,CompressionMode.Decompress))
        using(var output=File.Create(Path.Combine(Root,"campaign-library-active.json")))gz.CopyTo(output);
        SoulMetadata.Run();
    }
    internal static string TableFingerprint(string root)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(var file in Directory.GetFiles(root,"IB*.json").OrderBy(Path.GetFileName,StringComparer.Ordinal))
        {hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(file)));hash.AppendData(File.ReadAllBytes(file));}
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
