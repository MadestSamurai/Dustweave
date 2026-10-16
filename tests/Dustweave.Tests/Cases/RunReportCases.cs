using Dustweave;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;

static class RunReportCases
{
    public static void Run(List<string> checks)
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        var names = new[] { O(("id",1),("textCn","角色"),("textTw","角色繁"),("textEn","Character")),
            O(("id",2),("textCn","服装"),("textTw","服裝"),("textEn","Costume")), O(("id",3),("textCn","专武"),("textTw","專武"),("textEn","Weapon")) };
        var chars = new[] { O(("id",1),("uniqueCharId",90),("grade",5),("charNameTextId",1)), O(("id",2),("uniqueCharId",91),("grade",3),("charNameTextId",1)) };
        var catalog = new DailyDrawCatalog([O(("id",101),("useUniqueCharId",90),("costumeNameTextId",2))],
            [O(("id",102),("privateUniqueCharId",90),("grade",4),("itemNameTextId",3)), O(("id",103),("privateUniqueCharId",91),("grade",4),("itemNameTextId",3)), O(("id",104),("privateUniqueCharId",90),("grade",3),("itemNameTextId",3))], chars, names);
        JsonObject Proof(params JsonObject[] bundles) => O(("draws",bundles.Length),("results",bundles.Select(b => O(("rewardInfoBundle",b))).ToArray()));
        var duplicate = O(("viewItemInfo",new[]{O(("type",11),("id",101),("count",1)),O(("type",20),("count",300))}),
            ("itemInfo",new[]{O(("type",20),("count",300))}), ("itemAutoExchangeInfo",new[]{O(("originalItemType",11),("originalItemId",101),("originalItemCount",1))}));
        var draw = catalog.Describe(Proof(duplicate),"Costume");
        Check(draw.FiveStarCostumes == 1 && draw.Items.Count == 1 && draw.Items[0].Converted, "Converted five-star costume counts once without counting compensation currency");
        Check(draw.Items[0].Name.En == "Character · Costume" && draw.Unknown == 0, "Reward names retain all locales independently of execution language");
        var noView = duplicate.DeepClone().AsObject(); noView.Remove("viewItemInfo");
        Check(catalog.Describe(Proof(noView),"Costume").FiveStarCostumes == 1, "Original converted reward remains reportable without display items");
        var gear = catalog.Describe(Proof([..new[]{102,103,104}.Select(id=>O(("viewItemInfo",new[]{O(("type",10),("id",id),("count",1))}), ("equipInfo",new[]{O(("baseInfo",O(("id",id))))}))) ]),"Equipment");
        Check(gear.FiveStarUrEquipment == 1 && gear.Items.Count == 3, "UR three-star weapons and SR five-star weapons are not five-star UR rewards; inventory/display copies are deduplicated");
        var missing = catalog.Describe(Proof(O(("viewItemInfo",new[]{O(("type",11),("id",999),("count",1))}))),"Costume");
        Check(missing.Unknown == 1 && missing.FiveStarCostumes == 0, "Unknown metadata stays explicitly unclassified");
        var item = JsonSerializer.SerializeToElement(O(("result",O(("draw_summary",draw)))));
        Check(DailyDrawSummary.Read(item)?.Items[0].Converted == true, "Draw report round-trips through durable queue JSON");
        var summary = new QueueView("partial","","fixture",[
            new("guild","completed",""), new("weekly_book","skipped","本周末日之书任务已完成"),
            new QueueStage("free_draws","completed",""){Draws=draw},
            new QueueStage("event_battle","completed","event_auto_failed_swept"){HasNotice=true},
            new("weekly_mainline","partial","absorption_exhausted"), new("mirror","skipped","mirror_not_in_battle_period"),
            new("mail","completed","",Carried:true)]);
        Check(DailyRunReport.Relevant(summary).Select(s=>s.Task).SequenceEqual(new[]{"free_draws","event_battle","weekly_mainline","mirror"}), "Summary omits routine success but retains draws, fallback, deferrals and actionable skips");
        Check(DailyRunReport.HasNotice(JsonSerializer.SerializeToElement(O(("result",O(("reason","event_auto_failed_swept")))))), "A successful sweep cannot hide failed event auto-battle");
        Check(!DailyRunReport.HasNotice(JsonSerializer.SerializeToElement(O(("result",O(("before",O(("reason","event_auto_failed_swept")))))))), "Historical evidence does not become a fresh completion warning");
        var labels = DailyLocationLabels.Build([O(("id",6),("packNameTextId",1))], [O(("id",608),("mapNameTextId",2))],names);
        Check(DailyLocationLabels.Format("卡带 6 · 地图 608 · 正在收集",labels,"zh-CN") == "角色 · 服装 · 正在收集", "Map display uses cartridge and map names rather than identifiers");
        Check(DailyLocationLabels.Format("Cartridge 6 · map 608",labels,"en-US") == "Character · Costume", "English map labels follow the current UI language");
        Check(DailyLocationLabels.Format("Cartridges 6 · 地图 608",labels,"en-US") == "Character · Costume"
            && DailyLocationLabels.Format("第6章 · 地图608",labels,"zh-TW") == "角色繁 · 服裝", "Legacy chapter labels and deferred cartridge summaries also resolve names");
        Check(DailyLocationLabels.Read(JsonSerializer.SerializeToElement(O(("progress",O(("location_labels",labels)))))).Count == 2, "Map names survive progress serialization and parallel transport");
    }
}
