extern alias liveUtility;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
using Dustweave;
using Dustweave.Compatibility;
using BD2Daily;
using BD2.LocalIpc;
using SharpMonoInjector;
using LiveBindings=liveUtility::Dustweave.Connection.LiveClientBindings;
using LiveContract=liveUtility::Dustweave.Connection.LiveBindingContract;
namespace Dustweave.Soul;

internal static class SoulHookCompiler
{
    internal static async Task Prepare(string folder)
    {
        Directory.CreateDirectory(folder);
        var assembly=typeof(liveUtility::Dustweave.Connection.LiveEntry).Assembly;
        var sources=new List<SyntaxTree>();
        foreach(var name in assembly.GetManifestResourceNames().Where(n=>n.StartsWith("Live.")&&n.EndsWith(".cs")))
        {
            using var stream=assembly.GetManifestResourceStream(name)!;using var reader=new StreamReader(stream);
            var text=reader.ReadToEnd();
            if(name=="Live.Contracts.cs")
            {
                text=ReplaceOnce(text,"&&!NativeBattleKind(c.Kind))return \"unsupported_command\";","&&!NativeBattleKind(c.Kind)&&!SoulResearchNative.Kind(c.Kind))return \"unsupported_command\";");
                text=ReplaceOnce(text,"   if(NativeBattleKind(c.Kind)){","   if(SoulResearchNative.Kind(c.Kind))return SoulResearchNative.Validate(c,f,ui);\n   if(NativeBattleKind(c.Kind)){");
            }
            if(name=="Live.Bridge.cs"){
                text=ReplaceOnce(text,"else if(LivePolicy.ExtensionKind(c.Kind))","else if(SoulResearchNative.Kind(c.Kind))SoulResearchNative.Execute(c,u);\n     else if(LivePolicy.ExtensionKind(c.Kind))");
                // Current InventoryBattleResultUI.OnClickUI has no UIBase readiness
                // gate; its buttons are populated only after the stage-end response.
                text=ReplaceOnce(text,"internal static bool InputReady(UIBase u){","internal static bool InputReady(UIBase u){\n   if(u is InventoryBattleResultUI)return SoulResearchNative.ResultReady(u);");
                text=ReplaceOnce(text,"private static void Stop(){","private static void Stop(){SoulResearchTrace.Stop();");
            }
            if(name=="Live.Evidence.cs")text=ReplaceOnce(text,"var type=typeof(UIBase).Assembly.GetType(r.Type,true);","if(r.Id==\"soul.Capabilities\"){readings.Add(new Reading{Id=r.Id,Values=new[]{new Value{Path=\"$self\",Json=Scalar(SoulResearchNative.Capabilities())}}});continue;}if(r.Id==\"soul.Progress\"){readings.Add(new Reading{Id=r.Id,Values=new[]{new Value{Path=\"$self\",Json=Scalar(SoulResearchNative.Progress())}}});continue;}var type=typeof(UIBase).Assembly.GetType(r.Type,true);");
            sources.Add(CSharpSyntaxTree.ParseText(text,path:name));
        }
        using var contractResource=assembly.GetManifestResourceStream("Live.BindingContract.json")!;
        var contract=JsonSerializer.Deserialize<LiveContract>(contractResource)!;
        var managed=Path.GetDirectoryName(SoulMetadata.Client)!;
        sources=LiveBindings.Adapt(managed,sources,contract,Path.Combine(folder,"bindings.json")).ToList();
        var metadata=JsonNode.Parse(await File.ReadAllTextAsync(SoulAssets.MetadataPath))!;
        var owner=metadata["owners"]!.AsArray().Single(o=>o!["properties"]!.AsArray().Any(p=>p!["type"]!.GetValue<string>()=="Proto.Net.IbPlayDBInfo"))!;
        string Property(string dto)=>owner["properties"]!.AsArray().Single(p=>p!["type"]!.GetValue<string>().Contains("Proto.Net."+dto))!["name"]!.GetValue<string>();
        int Token(string dto)=>owner["requests"]!.AsArray().Single(r=>r!["request"]!.AsArray().Any(t=>t!.GetValue<string>()=="Proto.Net."+dto))!["token"]!.GetValue<int>();
        var binding="namespace BD2Daily.Live { internal static class SoulResearchBindings {";
        var unitIds=JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(SoulAssets.Tables,"IBItemTable.json")))!.AsArray()
            .Where(n=>n?["itemType"]?.GetValue<int>()==1).Select(n=>n!["id"]!.GetValue<int>()).Order();
        binding+="internal static readonly int[] UnitItemIds=new int[]{"+string.Join(',',unitIds)+"};";
        foreach(var p in new Dictionary<string,string>{["Owner"]=owner["type"]!.GetValue<string>(),["Mvid"]=metadata["mvid"]!.GetValue<string>(),["Play"]=Property("IbPlayDBInfo"),["Items"]=Property("IbItemDBInfo"),["Deck"]=Property("IbDeckDBInfo"),["Shop"]=Property("IbShopItemDBInfo")})binding+="internal const string "+p.Key+"="+JsonSerializer.Serialize(p.Value)+";";
        foreach(var p in new Dictionary<string,string>{["Buy"]="IbShopBuyRequest",["Upgrade"]="IbItemUpgradeRequest",["Save"]="IbDeckSaveRequest",["MainInfo"]="IbMainInfoRequest"})binding+="internal const int "+p.Key+"="+Token(p.Value)+";";
        using(var current=ModuleDefinition.ReadModule(SoulMetadata.Client)){
            foreach(var p in new Dictionary<string,string>{["PackRow"]="Proto.Design.common.PackTable",["OwnedPack"]="Proto.Net.PackDBInfo"})
                binding+="internal const int "+p.Key+"="+current.Types.SelectMany(t=>t.Methods).Single(m=>m.IsStatic&&m.Parameters.Count==1&&m.Parameters[0].ParameterType.FullName=="System.Int32"&&m.ReturnType.FullName==p.Value).MetadataToken.ToInt32()+";";
            binding+="internal const int Sell="+Token("IbItemSellRequest")+";";
            binding+="internal const int ApplyLevel="+current.Types.Single(t=>t.FullName=="InventoryBattleManager").Methods.Single(m=>m.Name=="ὨὡὢὧὥὯὠὥὤὬὣ"&&m.Parameters.Count==2).MetadataToken.ToInt32()+";";
            foreach(var p in new Dictionary<string,string>{["Season"]="ὫὭὭὠὨὬὢὪὯὮὤ",["Cleared"]="ὠὨὩὦὡὢὩὯὯὫὠ",["LobbyDungeons"]="ὤὣὩὩὬὪὥὥὯὠὤ",["LobbySelected"]="ὫὡὨὯὦὢὯὠὩὥὠ"})binding+="internal const string "+p.Key+"="+JsonSerializer.Serialize(p.Value)+";";
            binding+="internal const int ShopRefresh="+Token("IbShopRefreshRequest")+";";
            binding+="internal const int RefreshPrice="+current.Types.Single(t=>t.FullName==owner["type"]!.GetValue<string>()).Methods.Single(m=>m.Name=="ὥὧὣὥὣὯὣὮὧὢὭ"&&m.Parameters.Count==0).MetadataToken.ToInt32()+";";
            // Bound to the audited current-client methods, additionally MVID-gated.
            var tableOwner=current.Types.Single(t=>t.FullName=="ὡὭὣὦὫὪὫὧὨὨὧ");
            binding+="internal const int StageRows="+tableOwner.Methods.Single(m=>m.Name=="ὨὭὡὡὮὨὣὪὧὪὨ"&&m.Parameters.Count==1).MetadataToken.ToInt32()+";";
            foreach(var p in new Dictionary<string,string>{["ItemTable"]="ὧὧὣὠὤὪὮὮὫὭὮ",["UnitLevels"]="ὭὫὫὫὠὭὡὪὯὮὤ",["EquipLevels"]="ὮὠὭὨὢὠὣὮὣὢὩ"})binding+="internal const int "+p.Key+"="+tableOwner.Methods.Single(m=>m.Name==p.Value&&m.Parameters.Count==1).MetadataToken.ToInt32()+";";
            binding+="internal const int Costume="+current.Types.Single(t=>t.FullName=="ὠὨὭὫὩὬὫὡὣὣὦ").Methods.Single(m=>m.Name=="ὦὡὩὤὬὫὫὨὯὦὠ"&&m.Parameters.Count==3).MetadataToken.ToInt32()+";";
        }
        binding+="}}";
        sources.Add(CSharpSyntaxTree.ParseText(binding,path:"SoulResearchBindings.cs"));
        sources.Add(CSharpSyntaxTree.ParseText(SoulAssets.Text("Soul.runtime.SoulResearchNative.cs"),path:"SoulResearchNative.cs"));
        sources.Add(CSharpSyntaxTree.ParseText(SoulAssets.Text("Soul.runtime.SoulResearchTrace.cs"),path:"SoulResearchTrace.cs"));
        sources.Add(CSharpSyntaxTree.ParseText(SoulAssets.Text("Soul.runtime.InventoryBattleRuntimeShapeResolver.cs"),path:"InventoryBattleRuntimeShapeResolver.cs"));
        sources.Add(CSharpSyntaxTree.ParseText(SoulAssets.Text("Soul.runtime.RuntimeCharacterShapeResolver.cs"),path:"RuntimeCharacterShapeResolver.cs"));
        var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",sources.Select(s=>s.ToString())))));
        sources.Add(CSharpSyntaxTree.ParseText("namespace BD2.LocalIpc { public static class Build { public const string Fingerprint=\""+fingerprint+"\"; public const string PluginFingerprint=\"\"; }}"));
        var refs=new List<MetadataReference>();foreach(var file in Directory.EnumerateFiles(managed,"*.dll"))try{refs.Add(MetadataReference.CreateFromFile(file));}catch(BadImageFormatException){}
        var harmony=DailyHookCompiler.Resource("BD2Daily.Harmony.dll");refs.Add(MetadataReference.CreateFromImage(harmony));
        var compilation=CSharpCompilation.Create("BD2Daily.SoulResearch."+fingerprint[..12],sources,refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,optimizationLevel:OptimizationLevel.Release,platform:Platform.X64,deterministic:true));
        using var bytes=new MemoryStream();var result=compilation.Emit(bytes,manifestResources:new[]{new ResourceDescription("BD2Daily.Harmony.dll",()=>new MemoryStream(harmony),true)});
        if(!result.Success)throw new InvalidOperationException(string.Join("\n",result.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error)));
        await File.WriteAllBytesAsync(Path.Combine(folder,"bridge.dll"),bytes.ToArray());
        await File.WriteAllTextAsync(Path.Combine(folder,"bridge.json"),JsonSerializer.Serialize(new{fingerprint,clientMvid=metadata["mvid"]!.GetValue<string>(),sha256=Convert.ToHexString(SHA256.HashData(bytes.ToArray())),researchOnly=true}));
        Console.WriteLine("Research native bridge compiled: "+fingerprint);
    }
    private static string ReplaceOnce(string text,string from,string to)
    {
        int index=text.IndexOf(from,StringComparison.Ordinal);
        if(index<0||text.IndexOf(from,index+from.Length,StringComparison.Ordinal)>=0)throw new InvalidOperationException("Research integration anchor is not unique");
        return text[..index]+to+text[(index+from.Length)..];
    }
    internal static async Task Attach(string folder,GameInstance game,DailyGameHost host,string accountKey,bool restore=false)
    {
        using var owner=new DailyControlOwner(DailyIdentity.DataRoot);owner.Acquire();
        if(host.ReadIdentity(game).Snapshot?.AccountKey!=accountKey)throw new InvalidOperationException("Game account changed");
        var pipe=new PipeClient(Path.Combine(DailyIdentity.DataRoot,"live"),game.ProcessId,game.StartTicks);
        var originalFingerprint=pipe.Fingerprint();
        var restorePath=Path.Combine(folder,"original-bridge.dll");
        if(!File.Exists(restorePath))
        {
            string cache=DailyCacheStorage.Area(DailyIdentity.DataRoot,"suite-cache");
            string? original=null;
            foreach(var path in Directory.EnumerateFiles(Path.Combine(cache,"modules","daily"),"module.dll",SearchOption.AllDirectories))
            {
                using var module=ModuleDefinition.ReadModule(path);
                var build=module.Types.SingleOrDefault(t=>t.FullName=="BD2.LocalIpc.Build");
                if(build?.Fields.SingleOrDefault(f=>f.Name=="Fingerprint")?.Constant as string==originalFingerprint){original=path;break;}
            }
            if(original is null)throw new InvalidOperationException("Cannot identify original bridge for restoration");
            File.Copy(original,restorePath);
            await File.WriteAllTextAsync(Path.Combine(folder,"original.json"),JsonSerializer.Serialize(new{path=original,fingerprint=originalFingerprint,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original)))}));
        }
        if(pipe.Read("command.json") is not null)throw new InvalidOperationException("Pending native command; preserve original component");
        var bytes=await File.ReadAllBytesAsync(Path.Combine(folder,restore?"original-bridge.dll":"bridge.dll"));
        var manifest=JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder,restore?"original.json":"bridge.json")))!;
        if(!restore){using var client=ModuleDefinition.ReadModule(SoulMetadata.Client);if(client.Mvid.ToString()!=manifest["clientMvid"]!.GetValue<string>())throw new InvalidOperationException("Current client changed");}
        if(Convert.ToHexString(SHA256.HashData(bytes))!=manifest["sha256"]!.GetValue<string>())throw new InvalidOperationException("Bridge hash mismatch");
        using(var injector=new Injector(game.ProcessId))injector.Inject(bytes,"BD2Daily.Live","Bridge","Load");
        for(int i=0;i<70;i++)
        {
            await Task.Delay(500);
            if(host.Find()!=game)throw new InvalidOperationException("Game process changed during component handoff");
            if(pipe.Fingerprint()==manifest["fingerprint"]!.GetValue<string>()){Console.WriteLine(restore?"Original bridge restored through the shared handoff":"Research bridge is attached through the shared handoff");return;}
        }
        throw new TimeoutException("Research bridge handoff did not complete");
    }
}
