using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Dustweave.Desktop;

public partial class MainWindow
{
    void CheckOwnedStorageForSmoke(Action<bool,string> check)
    {
        var previous=AppDomain.CurrentDomain.GetData("BD2.LocalIpc.SuiteDataRoot.v1");
        string hostRoot=Path.Combine(smoke!,"storage-host");
        AppDomain.CurrentDomain.SetData("BD2.LocalIpc.SuiteDataRoot.v1",hostRoot);
        try
        {
            foreach(string name in new[]{"Fishing","Territory"})
            {
                string id=name.ToLowerInvariant(), legacy=Path.Combine(smoke!,"storage-legacy-"+id);
                Directory.CreateDirectory(legacy);
                var assembly=Assembly.Load("BD2"+name+".Core");
                var storage=assembly.GetType("BD2"+name+"."+name+"Storage")!;
                void Call(string method)=>storage.GetMethod(method)!.Invoke(null,[legacy]);
                string data=(string)storage.GetProperty("Data")!.GetValue(null)!;
                string source=Path.Combine(legacy,"settings.json"),target=Path.Combine(data,"settings.json");
                string settings=name=="Fishing"
                    ? """{"NextCastMilliseconds":250,"CastGauge":0.8,"Retention":{"KeepLegendary":false,"KeepLocked":true,"KeepUnknown":false},"AutoSell":true}"""
                    : """{"MinimumTreeStage":2,"Cooking":true,"SellCookingBatches":true,"CookingSaleBatch":321,"PlantingBudget":1234,"SellThreshold":9000}""";
                File.WriteAllText(source,"{broken");Call("ImportSettings");
                check(!File.Exists(target),id+": corrupt legacy settings do not create a new default file");
                File.WriteAllText(source,settings);Call("ImportSettings");
                var value=JsonNode.Parse(File.ReadAllText(target))!;
                check(data==Path.Combine(hostRoot,"tools",id),id+": preferences use host cache location");
                check(name=="Fishing"?value["NextCastMilliseconds"]!.GetValue<int>()==250&&!value["Retention"]!["KeepLegendary"]!.GetValue<bool>()
                    :value["MinimumTreeStage"]!.GetValue<int>()==2&&value["CookingSaleBatch"]!.GetValue<int>()==321&&value["PlantingBudget"]!.GetValue<long>()==1234,
                    id+": imported preferences preserve independent protection and planting options");
                string saved=File.ReadAllText(target);
                File.WriteAllText(source,"{}");Call("ImportSettings");
                check(saved==File.ReadAllText(target),id+": existing host settings are never overwritten");
                File.WriteAllText(Path.Combine(legacy,"runtime.log"),"old diagnostic");
                File.WriteAllText(Path.Combine(legacy,"sales-account.json"),"pending");
                File.WriteAllText(Path.Combine(legacy,"progress-account.json"),"unconfirmed planting");
                Call("ImportLegacyLogs");
                check(!File.Exists(Path.Combine(legacy,"runtime.log"))&&Directory.GetFiles(Path.Combine(data,"diagnostics"),"*runtime.log").Length==1,id+": legacy log migrates once");
                check(File.Exists(Path.Combine(legacy,"sales-account.json"))&&File.Exists(Path.Combine(legacy,"progress-account.json")),id+": pending business records remain intact");
                Call("ImportLegacyLogs");
                check(Directory.GetFiles(Path.Combine(data,"diagnostics"),"*runtime.log").Length==1,id+": log migration is idempotent");
                var policy=new DailyLogPolicy(true,7,512);
                string diagnostic=Directory.GetFiles(Path.Combine(data,"diagnostics"),"*runtime.log")[0];
                File.SetLastWriteTimeUtc(diagnostic,DateTime.UtcNow.AddDays(-8));
                var plan=DailyLogCleanup.Scan(hostRoot,policy,DateTime.UtcNow);
                check(plan.Units.Any(u=>u.Files.Any(f=>f.Path==diagnostic))&&!plan.Units.Any(u=>u.Files.Any(f=>f.Path==target)),
                    id+": host retention includes old logs and excludes preferences");
                check(DailyDiagnosticExport.Allowed($"tools/{id}/diagnostics/runtime.log")&&!DailyDiagnosticExport.Allowed($"tools/{id}/settings.json"),
                    id+": combined export includes diagnostics, excludes user preferences");
            }
        }
        finally {AppDomain.CurrentDomain.SetData("BD2.LocalIpc.SuiteDataRoot.v1",previous);}
    }
}
