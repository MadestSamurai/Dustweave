using System.Text.Json;
using System.Text.Json.Nodes;
using Dustweave;

internal static class TradePresetCases
{
    public static void Run(string root, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        void Reject(string text) { try { DailyTradePreset.Parse(text); } catch (InvalidDataException) { cases.Add("Invalid trade preset rejected before applying"); return; } throw new Exception("Accepted invalid trade preset"); }
        string area = Path.Combine(root, "trade-presets"), first = new('a',64), second = new('b',64);
        var store = new DailyPreferenceStore(area);
        var source = new DailyPreferences();
        source.Trade = new() { Enabled = true, FavoritesOnly = true, CookingRecipes = [44,22], Sales = [new(10,true,800),new(11,false,9)],
            AutoCookAndSell = false, OpenBoxesBeforeTrade = true, RefillCookingPotions = true };
        source.General.TalentPotionTarget = 12345;
        store.Save(first, source);
        var target = new DailyPreferences(); target.Mirror.Enabled = false; target.General.TalentPotionTarget = 54321;
        store.Save(second, target);
        var budget = new JsonObject { ["reserve_gold"] = 1200000L, ["max_spend"] = 0L,
            ["reserve_items"] = new JsonObject { ["321"] = 123 }, ["include_break_even_resales"] = true };
        DailyJson.Write(DailyTradePlan.SettingsPath(area, first), budget);
        string journal = Path.Combine(area,"trade","pending-example.json"); DailyJson.Write(journal,new{account=second,order=123});
        string oldJournal = File.ReadAllText(journal), oldSource = File.ReadAllText(store.PathFor(first));
        var preset = DailyTradePreset.Capture(area,first);
        string path = Path.Combine(area,"export.json"); preset.Export(path);
        string text = File.ReadAllText(path);
        Check(!text.Contains(first) && !text.Contains("12345") && !text.Contains("pending"),"Export contains only trading policy, not account, daily pills or actions");
        DailyTradePreset.Read(path).Apply(area,second);
        var after=store.Read(second);
        Check(JsonSerializer.Serialize(after.Trade)==JsonSerializer.Serialize(source.Trade),"Recipe order, sale minimums and all trading switches transfer across accounts");
        Check(after.General.TalentPotionTarget==54321 && !after.Mirror.Enabled,"Import retains unrelated target daily preferences");
        Check(DailyData.N(DailyTradePlan.ReadSettings(area,second)["max_spend"])==0 && DailyTradePlan.ReadSettings(area,second)["max_spend"]!=null,"Zero budget remains zero, not unlimited");
        Check(DailyData.N(DailyTradePlan.ReadSettings(area,second)["reserve_items"]?["321"])==123,"Ingredient reserves transfer with trade budget");
        Check(File.ReadAllText(store.PathFor(first))==oldSource && File.ReadAllText(journal)==oldJournal,"Source account and pending orders are unchanged");
        preset.Budget["max_spend"]=null; preset.Apply(area,second);
        Check(DailyTradePlan.ReadSettings(area,second)["max_spend"]==null,"Unlimited budget round-trips explicitly");
        string previous=File.ReadAllText(store.PathFor(second));
        void Mutate(Action<JsonObject> change) { var n=JsonNode.Parse(text)!.AsObject();change(n);Reject(n.ToJsonString()); }
        Mutate(n=>n["Schema"]=2); Mutate(n=>n["Format"]="inventory"); Mutate(n=>n["account"]="secret");
        Mutate(n=>n["Trade"]!["CookingRecipes"]=new JsonArray(1,1)); Mutate(n=>n["Trade"]!["Sales"]![0]!["Reserve"]=-1);
        Mutate(n=>n["Trade"]!.AsObject().Remove("AutoCookAndSell"));
        Mutate(n=>n["Trade"]!["Sales"]=null); Mutate(n=>n["Budget"]!["max_spend"]=-1);
        Mutate(n=>n["Budget"]!["password"]="secret"); Reject("{}"); Reject(new string('x',DailyTradePreset.MaxBytes+1));
        Mutate(n=>n["Budget"]!["max_spend"]=1.5); Mutate(n=>n["Budget"]!["reserve_items"]=null);
        Mutate(n=>n["Budget"]!.AsObject().Remove("max_spend"));
        Check(File.ReadAllText(store.PathFor(second))==previous,"Invalid imports leave account settings unchanged");
        string broken=new('c',64), blocked=store.PathFor(broken); Directory.CreateDirectory(blocked);
        DailyJson.Write(DailyTradePlan.SettingsPath(area,broken),new {reserve_gold=777});
        bool failed=false; try{preset.Apply(area,broken);}catch(IOException){failed=true;}catch(UnauthorizedAccessException){failed=true;}
        Check(failed && DailyData.N(DailyTradePlan.ReadSettings(area,broken)["reserve_gold"])==777,"Failed account save restores the previous trading budget");
    }
}
