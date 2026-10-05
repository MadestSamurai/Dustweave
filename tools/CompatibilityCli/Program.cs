using Mono.Cecil;
using BD2Daily.Compatibility;
using System.Text.Json;

if(args.Length==3&&args[0]=="binding-probe"){GuildBindingProbe.Prepare(args[1],args[2]);Console.WriteLine("Read-only Mono binding probe prepared.");return;}

if(args.Length==3&&args[0]=="generate-startup"){File.WriteAllText(args[2],JsonSerializer.Serialize(StartupHookContract.Generate(args[1]),new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("Startup contract generated; no game operation.");return;}

if(args.Length==3&&args[0]=="generate-guild"){File.WriteAllText(args[2],JsonSerializer.Serialize(GuildHookContract.Generate(args[1]),new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("Guild contract generated; no game operation.");return;}

if(args.Length==3&&args[0]=="bootstrap-host"){MonoBootstrapHost.Run(args[1],args[2]);return;}
if(args.Length==3&&args[0]=="bootstrap"){BootstrapChecks.Prepare(args[1],args[2]);Console.WriteLine("Isolated Mono bootstrap fixtures generated.");return;}

if(args.Length==3 && args[0]=="generate")
{
    File.WriteAllText(args[2],JsonSerializer.Serialize(DailyHookCompiler.Generate(args[1]),new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine("Identity contract generated; no game operation.");return;
}
if(args.Length==3 && args[0]=="check")
{
    var prepared=DailyHookCompiler.Prepare(args[1]);Directory.CreateDirectory(args[2]);
    File.WriteAllBytes(Path.Combine(args[2],"BD2Daily.Runtime4.dll"),prepared.Payload);
    File.WriteAllText(Path.Combine(args[2],"compatibility.json"),JsonSerializer.Serialize(prepared.Report,new JsonSerializerOptions{WriteIndented=true}));
    File.WriteAllText(Path.Combine(args[2],"guild-compatibility.json"),JsonSerializer.Serialize(prepared.GuildReport,new JsonSerializerOptions{WriteIndented=true}));
    File.WriteAllText(Path.Combine(args[2],"startup-compatibility.json"),JsonSerializer.Serialize(prepared.StartupReport,new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine(JsonSerializer.Serialize(new{identity=prepared.Report,guild=prepared.GuildReport,startup=prepared.StartupReport}));return;
}

if(args.Length==2 && args[0]=="inspect-neon")
{
    using var index=new MetadataIndex(Path.Combine(args[1],"Neo.Unity.Neon.dll"));
    foreach(var m in index.Types.SelectMany(MetadataIndex.Members).Where(m=>m is PropertyDefinition p && p.PropertyType.FullName=="Neo.Unity.Neon.NeonAuth" || m is FieldDefinition f && f.FieldType.FullName=="Neo.Unity.Neon.NeonAuth"))
        Console.WriteLine("Auth access: "+m.FullName+" ["+MetadataIndex.Signature(m)+"]");
    foreach(var t in index.Types.Where(t=>t.FullName.Contains("Member",StringComparison.OrdinalIgnoreCase) || t.FullName.EndsWith("Neon") || t.FullName.EndsWith("Auth")))
    {
        Console.WriteLine(t.FullName);
        foreach(var m in MetadataIndex.Members(t).Where(m=>m.Name.Contains("Member",StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Instance") || m.Name.Contains("Status") || m.Name.Contains("Id") || m.Name.Contains("Auth")))
            Console.WriteLine("  "+m.FullName+" ["+MetadataIndex.Signature(m)+"]");
    }
    return;
}
if(args.Length==2 && args[0]=="inspect-game")
{
    using var index=new MetadataIndex(Path.Combine(args[1],"Assembly-CSharp.dll"));
    foreach(var name in new[]{"BatchManager","GameCameraManager","ὨὬὣὫὩὯὩὩὣὠὧ","Proto.Net.UserDBInfo"})
    {
        var t=index.Types.SingleOrDefault(t=>t.FullName==name || t.Name==name);if(t==null){Console.WriteLine("Missing "+name);continue;}Console.WriteLine(t.FullName);
        foreach(var m in t.Properties.Cast<IMemberDefinition>().Concat(t.Fields).Where(m=>t.Name!="UserDBInfo" || m.Name.Contains("OwnerIndex") || m.Name.Contains("UserId") || m.Name.Contains("Server")))
            Console.WriteLine("  "+m.FullName+" ["+MetadataIndex.Signature(m)+"]");
    }
    return;
}
throw new ArgumentException("Unsupported inspection command");

