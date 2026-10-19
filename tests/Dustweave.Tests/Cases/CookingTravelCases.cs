extern alias sichuan;
extern alias vision;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;

static class CookingTravelCases
{
    static JsonObject Read(string id, string path, JsonNode value) => O(("Id",id),("Error",""),("Values",new JsonArray(O(("Path",path),("Error",""),("Json",value.ToJsonString())))));
    public static async Task Run(string root,List<string> cases)
    {
        void Check(bool ok,string text){if(!ok)throw new Exception(text);cases.Add(text);}
        var prefs = new DailyPreferences();
        Check(!prefs.General.FastTravel && prefs.Trade.CookingRecipes.Length == 0,"fast travel and custom recipes remain opt-in");
        prefs.General.FastTravel=true; prefs.Trade.FavoritesOnly=true;prefs.Trade.CookingRecipes=[20,10];
        var restored=DailyPreferences.Parse(JsonSerializer.Serialize(prefs));
        Check(restored.General.FastTravel&&restored.Trade.CookingRecipes.SequenceEqual(new long[]{20,10})&&restored.Trade.RequiresCatalog,"ordered recipe choices and fast travel survive preference round trip");
        bool rejected=false;try{restored.Trade.CookingRecipes=[10,10];restored.Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"duplicate recipe choices are rejected");
        using var h=new WorkflowHarness(Path.Combine(root,"dash"),[]);
        h.Frame["Scene"]="Map0001_001";h.Page("GameFieldDefaultUI");
        var e=h.Evidence();
        e["Readings"]=new JsonArray(Read("mainline.talent_rows","$self",new JsonArray(O(("Group",201),("Reason",""),("Cooldown",0)))));
        Check(!DailyFieldRoute.ShouldDash(new(),e)&&DailyFieldRoute.ShouldDash(new(){FastTravel=true},e),"walking speed talent requires explicit preference and controllable field");
        var active=Read("mainline.durations","$self",new JsonArray(O(("Key","FastDash"),("Value.ὫὢὢὬὫὫὡὩὪὩὮ",true),("Value.ὠὨὠὭὬὤὪὢὫὨὮ",1))));
        e["Readings"]!.AsArray().Add(active);
        Check(!DailyFieldRoute.ShouldDash(new(){FastTravel=true},e),"even one second of active dash never waits for recast");
        e["Readings"]=new JsonArray(Read("mainline.talent_rows","$self",new JsonArray(O(("Group",201),("Reason",""),("Cooldown",9)))));
        Check(!DailyFieldRoute.ShouldDash(new(){FastTravel=true},e),"dash cooldown preserves normal walking");
        h.Page("QuickMenuUI");e["Frame"]=h.Frame.DeepClone();
        Check(!DailyFieldRoute.ShouldDash(new(){FastTravel=true},e),"fast travel never opens another menu during an interaction");
        Check(!await new DailyFieldRoute(h.Workflow).PrepareWalking()&&h.Box.Commands.Count==0,"default walking sends no speed talent command or observation");
        var catalog=JsonNode.Parse("""{"items":[{"id":3,"stack":9999},{"id":4,"stack":9999}],"recipes":[{"id":10,"output":3,"count":2,"potions":2,"materials":{"1":3,"2":2}},{"id":20,"output":4,"count":1,"potions":1,"materials":{"1":2}}]}""")!.AsObject();
        var wallet=O(("potions",100),("items",O(("1",20),("2",10))),("protected",new JsonObject()));
        var first=DailyCustomCooking.Next(catalog,10,wallet,0,[10,20])!;
        Check(N(first["count"])==5&&N(first["potions"])==10&&N(first["materials"]!["1"])==15,"custom cooking respects multi-ingredient amounts and output yields");
        wallet["items"]!["1"]=5;wallet["items"]!["2"]=0;
        Check(N(DailyCustomCooking.Next(catalog,20,wallet,0,[10,20])!["count"])==2,"later recipe uses the remaining inventory rather than initial stock");
        wallet["protected"]=O(("stack",O(("id",1),("count",4))));
        Check(DailyCustomCooking.Next(catalog,20,wallet,0,[20])==null,"locked ingredients are not consumed");
        wallet["protected"]=new JsonObject(); wallet["potions"]=10;
        Check(DailyCustomCooking.Next(catalog,20,wallet,10,[20])==null,"custom cooking never spends reserved talent pills");
        Check(DailyCustomCooking.Next(catalog,20,wallet,0,[10])==null&&DailyCustomCooking.Next(catalog,999,wallet,0,[999])==null,"unlearned and removed recipes are skipped");
        wallet["items"]!["4"]=9999;Check(DailyCustomCooking.Next(catalog,20,wallet,0,[20])==null,"full output inventory is skipped without consuming materials");
        Check(DailySuite.PreparesInWindow("sichuan")&&DailySuite.PreparesInWindow("secret-vision")&&DailyToolCatalog.Find("sichuan").Repository=="Dustweave","both owned tools use deferred shared preparation");
        Check(DailyDiagnosticExport.Allowed("tools/secret-vision/diagnostics/progress.json")&&!DailyDiagnosticExport.Allowed("tools/secret-vision/settings.json")&&!DailyDiagnosticExport.Allowed("tools/secret-vision/private-key.json"),"SECRET VISION evidence is exportable without exposing settings or secrets");
        catalog["cooking_recipes"] = new JsonArray(O(("id",99),("output",5),("count",1),("potions",1),("materials",O(("1",1)))));
        catalog["cooking_items"] = new JsonArray(O(("id",5),("stack",999)));
        Check(N(DailyCustomCooking.Next(catalog,99,wallet,0,[99])?["count"])==5 && Rows(catalog["recipes"]).Length==2,"non-premium custom recipes are usable without changing profit optimizer inputs");
        var prior=AppDomain.CurrentDomain.GetData("BD2.LocalIpc.SuiteDataRoot.v1");
        try {
            AppDomain.CurrentDomain.SetData("BD2.LocalIpc.SuiteDataRoot.v1",Path.Combine(root,"owned"));
            string legacy=Path.Combine(root,"legacy");Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy,"settings.json"),"{\"IntervalMilliseconds\":250}");
            sichuan::BD2Sichuan.SichuanStorage.ImportSettings(legacy);
            var target=Path.Combine(sichuan::BD2Sichuan.SichuanStorage.Data,"settings.json");
            Check(sichuan::BD2Sichuan.SichuanSettings.Read(target).IntervalMilliseconds==250,"Sichuan imports valid settings under the owning suite root");
            File.WriteAllText(Path.Combine(legacy,"settings.json"),"{\"IntervalMilliseconds\":500}");
            sichuan::BD2Sichuan.SichuanStorage.ImportSettings(legacy);
            Check(sichuan::BD2Sichuan.SichuanSettings.Read(target).IntervalMilliseconds==250,"new owned preferences are never overwritten by legacy settings");
            new vision::BD2SecretVision.UserPreferences().Save(legacy);
            vision::BD2SecretVision.SecretVisionStorage.ImportSettings(legacy);
            Check(File.Exists(Path.Combine(vision::BD2SecretVision.SecretVisionStorage.Data,"settings.json")),"SECRET VISION imports validated settings to its owned location");
            File.WriteAllText(Path.Combine(legacy,"runtime.log"),"synthetic error");File.WriteAllText(Path.Combine(legacy,"control.json"),"{}");
            vision::BD2SecretVision.SecretVisionStorage.ImportLegacyLogs(legacy);
            Check(Directory.GetFiles(vision::BD2SecretVision.SecretVisionStorage.Diagnostics).Length==1&&File.Exists(Path.Combine(legacy,"control.json"))&&!File.Exists(Path.Combine(legacy,"runtime.log")),"known logs move to host retention without copying control state");
        } finally { AppDomain.CurrentDomain.SetData("BD2.LocalIpc.SuiteDataRoot.v1",prior); }
    }
}
