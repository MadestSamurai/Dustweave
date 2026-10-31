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
        Check(prefs.General.FastTravel && prefs.Trade.CookingRecipes.Length == 0,"fast travel defaults on while custom recipes remain opt-in");
        prefs.General.FastTravel=true; prefs.Trade.FavoritesOnly=true;prefs.Trade.CookingRecipes=[20,10];
        var restored=DailyPreferences.Parse(JsonSerializer.Serialize(prefs));
        Check(restored.General.FastTravel&&restored.Trade.CookingRecipes.SequenceEqual(new long[]{20,10})&&restored.Trade.RequiresCatalog,"ordered recipe choices and fast travel survive preference round trip");
        bool rejected=false;try{restored.Trade.CookingRecipes=[10,10];restored.Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"duplicate recipe choices are rejected");
        using var h=new WorkflowHarness(Path.Combine(root,"dash"),[]);
        h.Frame["Scene"]="Map0001_001";h.Page("GameFieldDefaultUI");
        var e=h.Evidence();
        e["Readings"]=new JsonArray(Read("mainline.talent_rows","$self",new JsonArray(O(("Group",1305),("Reason",""),("Cooldown",0)))));
        double longWalk = DailyFieldRoute.MinimumDashDistance;
        Check(!DailyFieldRoute.ShouldDash(new(){FastTravel=false},e,longWalk)&&DailyFieldRoute.ShouldDash(new(){FastTravel=true},e,longWalk),"walking speed talent respects opt out, sufficient distance and controllable field");
        foreach (double? distance in new double?[] { null, -1, 0, 2, longWalk - .1, double.NaN, double.PositiveInfinity })
            Check(!DailyFieldRoute.ShouldDash(new(){FastTravel=true},e,distance),"short or unknown walking distance never spends dash potions: "+distance);
        Check(DailyFieldRoute.TargetDistance(O(("Near",true),("Distance",100)))==0
            && DailyFieldRoute.TargetDistance(O(("Reachable",false),("Distance",100)))==null
            && DailyFieldRoute.TargetDistance(new())==null,"in-range, unreachable and missing merchant/NPC distances do not trigger dash");
        Check(DailyFieldRoute.TargetDistance(O(("Distance",400)),squared:true)==20
            && !DailyFieldRoute.WorthDashing(DailyFieldRoute.TargetDistance(O(("Distance",400)),squared:true)),
            "NPC squared distance is converted before deciding whether a walk is long");
        var routeEvidence=e.DeepClone().AsObject();
        routeEvidence["Readings"]!.AsArray().Add(Read("mainline.map",DailyFieldRoute.MapPath,O(("id",11),("packId",1))));
        var approaches=new JsonArray(O(("Instance",10),("Reachable",true),("Distance",80)),
            O(("Instance",20),("Reachable",true),("Distance",3)),
            O(("Instance",30),("Reachable",false),("Distance",100)));
        routeEvidence["Readings"]!.AsArray().Add(Read("mainline.travel","$self",O(("Pack",1),("Map",11),("Approaches",approaches))));
        Check(DailyFieldRoute.WalkingDistance(routeEvidence,10)==80 && DailyFieldRoute.WalkingDistance(routeEvidence,20)==3
            && DailyFieldRoute.WalkingDistance(routeEvidence,30)==null && DailyFieldRoute.WalkingDistance(routeEvidence,999)==null,
            "distance uses the selected reachable route, not a different far-away gate");
        routeEvidence["Readings"]=new JsonArray(
            O(("Id","mainline.map"),("Error",""),("Values",new JsonArray(
                O(("Path","ὪὨὯὢὫὮὨὩὮὡὬ.transform.position.x"),("Error",""),("Json","0")),
                O(("Path","ὪὨὯὢὫὮὨὩὮὡὬ.transform.position.z"),("Error",""),("Json","0"))))),
            O(("Id","mainline.reward"),("Error",""),("InstanceId",40),("Values",new JsonArray(
                O(("Path","transform.position.x"),("Error",""),("Json","3")),
                O(("Path","transform.position.y"),("Error",""),("Json","100")),
                O(("Path","transform.position.z"),("Error",""),("Json","4"))))));
        Check(DailyFieldRoute.WalkingDistance(routeEvidence,40)==5,"a high reward next to the player does not count as a long horizontal walk");
        var active=Read("mainline.durations","$self",new JsonArray(O(("Key","MoveSpeedUp"),("Value.ὫὢὢὬὫὫὡὩὪὩὮ",true),("Value.ὠὨὠὭὬὤὪὢὫὨὮ",1))));
        e["Readings"]!.AsArray().Add(active);
        Check(!DailyFieldRoute.ShouldDash(new(){FastTravel=true},e,longWalk),"even one second of active dash never waits for recast");
        e["Readings"]=new JsonArray(Read("mainline.talent_rows","$self",new JsonArray(O(("Group",1305),("Reason",""),("Cooldown",9)))));
        Check(!DailyFieldRoute.ShouldDash(new(){FastTravel=true},e,longWalk),"dash cooldown preserves normal walking");
        e["Readings"]=new JsonArray(Read("mainline.talent_rows","$self",new JsonArray(O(("Group",205),("Reason",""),("Cooldown",9)))),
            Read("mainline.durations","$self",new JsonArray(O(("Key","FastDash"),("Value.ὫὢὢὬὫὫὡὩὪὩὮ",true),("Value.ὠὨὠὭὬὤὪὢὫὨὮ",20)))));
        Check(DailyFieldRoute.MovementSpeedTalent==13 && DailyFieldRoute.Kinds[13]=="MoveSpeedUp"
            && DailyFieldRoute.ShouldDash(new(){FastTravel=true},e,longWalk),"unlimited sprint activity and cooldown are not movement-speed evidence");
        JsonObject Talent(int instance,int kind,int group,double speed,double duration,int cost) => O(("instance",instance),
            (DailyFieldRoute.SkillPath,O(("classType",kind),("groupId",group),("id",5),("valueList",new JsonArray(speed,duration,0)),("catalystValue",cost))));
        var speedTalents=new[]{Talent(1,2,205,.95,20,10),Talent(2,13,1315,.25,30,5),Talent(3,13,1305,.75,10,10),Talent(4,13,1325,.75,40,30)};
        var statuses=speedTalents.ToDictionary(r=>N(r["instance"]),r=>O(("Reason",""),("Cooldown",0)));
        JsonObject? Choose(long? potions=null) => DailyFieldRoute.SelectTalent(13,speedTalents,statuses,new Dictionary<long,long>(),potions);
        Check(N(Choose()?["instance"])==4,"movement speed takes priority, then duration among equally fast talents");
        statuses[4]["Reason"]="character_pack_restricted";
        Check(N(Choose()?["instance"])==3,"unavailable fastest character falls back to another speed talent, never unlimited sprint");
        statuses[3]["Cooldown"]=9;
        Check(N(Choose()?["instance"])==2,"fast talent cooldown falls back to the slower usable speed talent");
        statuses[2]["Reason"]="character_dead";
        Check(Choose()==null,"unlimited sprint is never used when all walking-speed talents are unavailable");
        foreach(var state in statuses.Values){state["Reason"]="";state["Cooldown"]=0;}
        Check(N(Choose(10)?["instance"])==3 && N(Choose(5)?["instance"])==2 && Choose(4)==null,
            "speed selection honors the spendable potion balance after reserve before choosing a slower affordable talent");
        speedTalents[3]["_objDisableImage.activeSelf"]=true;
        Check(N(Choose()?["instance"])==3,"disabled speed rows are excluded");
        speedTalents[2][DailyFieldRoute.SkillPath]!["valueList"]=new JsonArray(0,10,0);
        Check(N(Choose()?["instance"])==2,"missing positive movement bonus cannot outrank a usable speed talent");
        var speedIntent=O(("role","dispatch.start"),("action",O(("operation","mainline_talent"))),("scope",O(("kind",13))));
        Check(DailyFieldTalentProof.Owns(speedIntent),"movement-speed activations use the ordinary talent receipt and recovery path");
        var legacySpeed=speedIntent.DeepClone().AsObject();
        legacySpeed["before"]=O(("Frame",h.Frame)); legacySpeed["before"]!["Frame"]!["BridgeVersion"]=131;
        legacySpeed["command_id"]=new string('a',32); legacySpeed["action"]!["value"]=42;
        var rejectedSpeed=O(("State","unknown"),("Error","System.InvalidOperationException: Only mainline gathering talents allowed\r\n at Bridge.TickCore()"),
            ("Before",legacySpeed["before"]!["Frame"]),("Command",O(("Id",new string('a',32)),("Kind","mainline_talent"),("Value",42))));
        Check(DailyFieldTalentProof.LegacySpeedRejected(legacySpeed,rejectedSpeed),"old speed whitelist rejection is proven before native click");
        foreach(var mutation in new Action<JsonObject>[] {
            x=>x["Error"]="System.TimeoutException: no reply", x=>x["Command"]!["Value"]=43,
            x=>x["Command"]!["Id"]=new string('b',32), x=>x["Before"]!["AccountKey"]="other" }){
            var other=rejectedSpeed.DeepClone().AsObject();mutation(other);
            Check(!DailyFieldTalentProof.LegacySpeedRejected(legacySpeed,other),"unrelated or mismatched talent failure cannot authorize replay");
        }
        legacySpeed["before"]!["Frame"]!["BridgeVersion"]=132;
        Check(!DailyFieldTalentProof.LegacySpeedRejected(legacySpeed,rejectedSpeed),"legacy speed recovery is restricted to affected component versions");
        h.Page("QuickMenuUI");e["Frame"]=h.Frame.DeepClone();
        Check(!DailyFieldRoute.ShouldDash(new(){FastTravel=true},e,longWalk),"fast travel never opens another menu during an interaction");
        Check(!await new DailyFieldRoute(h.Workflow).PrepareWalking(longWalk)&&h.Box.Commands.Count==0,"default walking sends no speed talent command or observation");
        h.Workflow.Settings.General.FastTravel=true;
        Check(!await new DailyFieldRoute(h.Workflow).PrepareWalking(3)&&!await new DailyFieldRoute(h.Workflow).PrepareWalking(null)
            && h.Box.Commands.Count==0&&h.Time==0,"enabled fast travel leaves short and unmeasured walks alone without opening a menu or waiting");
        h.Page("GameFieldDefaultUI");
        h.Readings=()=>[Read("mainline.map",DailyFieldRoute.MapPath,O(("id",11),("packId",1))),
            Read("mainline.talent_rows","$self",new JsonArray()),Read("mainline.talent_counts","Values",new JsonArray())];
        h.OnCommand=command=>{if(S(command["Kind"])=="mainline_menu")h.Page("QuickMenuUI","_objBackButton");else if(S(command["Kind"])=="back")h.Page("GameFieldDefaultUI");};
        Check(await new DailyFieldRoute(h.Workflow).PrepareWalking(longWalk),"long walk attempts the optional movement-speed menu");
        Check(h.Box.Commands.Any(c=>S(c["Kind"])=="mainline_menu"&&N(c["Value"])==13)
            && !h.Box.Commands.Any(c=>S(c["Kind"])=="mainline_talent"||S(c["Kind"])=="mainline_menu"&&N(c["Value"])==2)
            && DailyNavigationDecision.Types(h.Frame).Contains("GameFieldDefaultUI"),
            "production walking workflow opens only MoveSpeedUp and returns to ordinary walking if no talent is usable");
        var catalog=JsonNode.Parse("""{"items":[{"id":3,"stack":9999},{"id":4,"stack":9999}],"recipes":[{"id":10,"output":3,"count":2,"potions":2,"materials":{"1":3,"2":2}},{"id":20,"output":4,"count":1,"potions":1,"materials":{"1":2}}]}""")!.AsObject();
        var labels = JsonNode.Parse("""{"cooking_items":[{"id":1,"name":"原料","name_tw":"材料","name_en":"Ingredient"},{"id":3,"name":"简中料理","name_tw":"繁中料理","name_en":"Dish"},{"id":4,"name":"旧目录料理"}],"cooking_recipes":[{"id":20,"output":4,"materials":{"1":2}},{"id":10,"output":3,"materials":{"1":3,"999":2}}]}""")!.AsObject();
        string labelsBefore = labels.ToJsonString();
        var choices = DailyCustomCooking.Choices(labels, "en-US");
        Check(choices.Select(c=>c.Id).SequenceEqual(new long[]{20,10}) && choices[1].Name=="Dish" && choices[1].Materials=="Ingredient ×3 · #999 ×2",
            "custom cooking display resolves output ID without confusing recipe ID or missing ingredient");
        Check(choices[0].Name=="旧目录料理" && labels.ToJsonString()==labelsBefore,
            "legacy labels fall back without changing saved recipe IDs or planning catalog");
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
        wallet["storage"]=O(("slots",4),("used",3),("stacks",new JsonArray(O(("id",4),("count",9999)))));
        Check(N(DailyCustomCooking.Next(catalog,20,wallet,0,[20])?["count"])==2,"custom cooking creates a second output stack when a shared slot is free");
        wallet["storage"]!["slots"]=3;
        Check(DailyCustomCooking.Next(catalog,20,wallet,0,[20])==null,"custom cooking cannot borrow slots from ingredients not consumed yet");
        wallet.Remove("storage");
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
