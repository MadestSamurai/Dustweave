using System.Text.Json.Nodes;
using Dustweave;
namespace Dustweave.Soul;

// Recovery of a known failed growth route or an explicit fresh start. Abandons only
// the active dungeon, never a battle in progress or already cleared history.
internal static class SoulRecovery
{
    internal static async Task Restart(DailyPipeMailbox mailbox,DailyCommandDriver driver,string folder,string id,bool freshStart=false)
    {
        await SoulEvidence.Capture(mailbox,driver,folder,id+"-restart",continuation:async()=>{
            var state=JsonNode.Parse(File.ReadAllText(Path.Combine(folder,"live-state-latest.json")))!["state"]!;
            var play=state["play"]!;
            int dungeon=play["dungeonId"]!.GetValue<int>();
            if(dungeon<=0||play["stageId"]!.GetValue<int>()<=0||play["isStageEnter"]!.GetValue<bool>())throw new InvalidDataException("Only an idle active dungeon may restart");
            string journalPath=Path.Combine(folder,"campaign-recovery-history.json");
            var history=File.Exists(journalPath)?JsonNode.Parse(File.ReadAllText(journalPath))!.AsArray():new JsonArray();
            if(history.Any(n=>n?["status"]?.ToString()=="dispatching"))throw new InvalidDataException("An earlier recovery outcome is unknown; reconcile before proceeding");
            if(!freshStart&&history.Count(n=>n?["dungeon"]?.GetValue<int>()==dungeon&&n?["status"]?.ToString()=="confirmed"&&n?["kind"]?.ToString()!="fresh_start")>=2)throw new InvalidDataException("Automatic restart budget exhausted for this dungeon");
            var record=new JsonObject{["atUtc"]=DateTime.UtcNow.ToString("O"),["dungeon"]=dungeon,["kind"]=freshStart?"fresh_start":"strategy_retry",["status"]="dispatching",["run"]=play.DeepClone(),["evidencePrefix"]=id};
            void Save(){File.WriteAllText(journalPath+".pending",history.ToJsonString());File.Move(journalPath+".pending",journalPath,true);}
            async Task WaitFor(string surface){
                var until=DateTime.UtcNow.AddSeconds(20);
                while(DateTime.UtcNow<until){var frame=(await driver.ObserveAsync()).Frame;if(frame["Surfaces"]!.AsArray().Any(n=>n?["Type"]?.ToString()==surface))return;await Task.Delay(200);}
                throw new TimeoutException("Native recovery boundary did not appear: "+surface);
            }
            JsonObject Click(string ui,string field)=>new(){["ui"]=ui,["field"]=field,["timeout"]=20,["reason"]="soul-known-growth-route-recovery"};
            var first=(await driver.ObserveAsync()).Frame;
            bool inStage=first["Surfaces"]!.AsArray().Count(n=>n?["Type"]?.ToString()=="InventoryBattleStageUI")==1;
            if(!inStage&&first["Surfaces"]!.AsArray().Count(n=>n?["Type"]?.ToString()=="InventoryBattleLobbyUI")!=1)throw new InvalidDataException("Expected idle stage or dungeon list before recovery");
            // A failed read-only preflight has not dispatched anything and
            // must not permanently block recovery as an unknown transaction.
            history.Add(record);Save();
            if(inStage){await SoulSubmit.Send(driver,Click("InventoryBattleStageUI","_objBackButton"));await WaitFor("InventoryBattleLobbyUI");}
            await SoulSubmit.Send(driver,Click("InventoryBattleLobbyUI","_dungeonInfoFields._btnGiveUpStage"));await WaitFor("MessagePopupUI");
            var confirm=Click("MessagePopupUI","_buttonOK");confirm["expectProtocol"]="soul.IbDungeonGiveUp";confirm["expectedRun"]=play.DeepClone();
            await SoulEvidence.RunObservedUi(mailbox,driver,folder,id+"-confirm-restart",confirm);
            record["status"]="confirmed";Save();
            Console.WriteLine("Confirmed active-dungeon restart; next campaign decision chooses the first uncleared dungeon");
        });
    }
}
