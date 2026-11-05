using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dustweave.Soul;

internal static class SoulAssets
{
    internal static string Root { get; private set; } = "";
    private static string? currentTables;
    internal static string Tables => currentTables??Path.Combine(Root,"tables");
    internal static string Library => Path.Combine(Root,"bd2_inventory_battle.dll");
    internal static string MetadataPath => Path.Combine(Root,"client-metadata.json");
    internal static string ClientPath { get; private set; } = "";
    internal static string Text(string name) { using var stream=typeof(SoulAssets).Assembly.GetManifestResourceStream(name)??throw new FileNotFoundException(name);using var reader=new StreamReader(stream);return reader.ReadToEnd(); }
    internal static void Prepare(string root,string client)
    {
        ClientPath=client;
        PrepareModel(root);
        SoulMetadata.Run();
    }
    internal static void PrepareModel(string root)
    {
        currentTables=null;
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
    }
    // A client rebuild is not a rules change. Read the installed database with
    // the same exporter used by daily tasks, and bind the cache to both inputs.
    internal static async Task PrepareCurrentTablesAsync(string package,CancellationToken cancellation)
    {
        string database=DailyTradeData.Database();
        string clientHash=DailyTradeCatalog.Hash(ClientPath),databaseHash=DailyTradeCatalog.Hash(database);
        string cache=Path.Combine(Root,"current",clientHash+"-"+databaseHash);
        Directory.CreateDirectory(cache);
        string[] names=typeof(SoulAssets).Assembly.GetManifestResourceNames().Where(n=>n.StartsWith("Soul.tables.",StringComparison.Ordinal)).Select(n=>Path.GetFileNameWithoutExtension(n[12..])).Order().ToArray();
        string output=Path.Combine(cache,"tables"),proof=Path.Combine(cache,"verified.json");
        bool valid=false;
        if(File.Exists(proof))try{
            var hashes=DailyRuleData.ValidateExport(output,clientHash,databaseHash,names);
            valid=JsonNode.DeepEquals(hashes,JsonNode.Parse(File.ReadAllText(proof)));
        }catch(Exception e)when(e is IOException or InvalidDataException or JsonException or InvalidOperationException or ArgumentException){ }
        if(!valid){
            string scratch=Path.Combine(cache,"export-"+Guid.NewGuid().ToString("N"));
            var start=DailyTools.StartInfo(package,"exporter");
            string gameRoot=Directory.GetParent(Path.GetDirectoryName(ClientPath)!)!.Parent!.FullName;
            foreach(var arg in new[]{"--game-root",gameRoot,"--tables",string.Join(',',names),"--simulator-compatible","--output",scratch})start.ArgumentList.Add(arg);
            if(Environment.GetEnvironmentVariable("BD2_DATA_ROOT") is {Length:>0} data){start.ArgumentList.Add("--data-root");start.ArgumentList.Add(data);}
            using var process=Process.Start(start)??throw new IOException("Soul table reader did not start.");
            var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(90));
            try{await process.WaitForExitAsync(timeout.Token);}
            catch{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}throw;}
            finally{File.WriteAllText(Path.Combine(cache,"export.log"),await stdout+await stderr);}
            if(process.ExitCode!=0)throw new InvalidDataException("无法读取当前魂盘规则，请更新游戏资源后重试。 / Could not read current Soul rules; finish the game resource update and retry.");
            var hashes=DailyRuleData.ValidateExport(scratch,clientHash,databaseHash,names);
            if(DailyTradeCatalog.Hash(ClientPath)!=clientHash||DailyTradeCatalog.Hash(database)!=databaseHash)throw new InvalidDataException("Soul rule inputs changed during preparation; reconnect.");
            Directory.CreateDirectory(output);
            foreach(var file in Directory.GetFiles(scratch)){File.Copy(file,Path.Combine(output,Path.GetFileName(file)),true);File.Delete(file);}Directory.Delete(scratch);
            File.WriteAllText(proof,hashes.ToJsonString());
        }
        cancellation.ThrowIfCancellationRequested();
        if(DailyTradeCatalog.Hash(ClientPath)!=clientHash||DailyTradeCatalog.Hash(database)!=databaseHash)throw new InvalidDataException("Soul rule inputs changed during preparation; reconnect.");
        currentTables=output;
        // Animation-only table updates do not invalidate calibrated opening
        // candidates. Never reuse them when a battle, cost or layout value changed.
        if(names.All(name=>EquivalentDecisionTable(name,JsonNode.Parse(File.ReadAllText(Path.Combine(Root,"tables",name+".json")))!,JsonNode.Parse(File.ReadAllText(Path.Combine(output,name+".json")))!))){
            string library=Path.Combine(Root,"campaign-library-active.json");
            var model=JsonNode.Parse(File.ReadAllText(library))!;
            if(model["tableSha256"]?.ToString()==TableFingerprint(Path.Combine(Root,"tables"))){model["tableSha256"]=TableFingerprint(output);File.WriteAllText(library,model.ToJsonString());}
        }
    }
    internal static bool EquivalentDecisionTable(string name,JsonNode before,JsonNode after){
        JsonNode Normalize(JsonNode input){
            var copy=input.DeepClone();
            if(copy is JsonArray rows)foreach(var row in rows.OfType<JsonObject>()){
                if(name=="IBEffectConfigTable")row.Remove("useAnimation");
                if(name=="IBUnitItemTable")row.Remove("coolTimeUseAnimation");
            }
            return copy;
        }
        return JsonNode.DeepEquals(Normalize(before),Normalize(after));
    }
    internal static string TableFingerprint(string root)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(var file in Directory.GetFiles(root,"IB*.json").OrderBy(Path.GetFileName,StringComparer.Ordinal))
        {hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(file)));hash.AppendData(File.ReadAllBytes(file));}
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
