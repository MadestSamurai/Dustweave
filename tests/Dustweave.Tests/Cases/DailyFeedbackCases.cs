using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;
static class DailyFeedbackCases
{
 public static async Task Run(string root,List<string> checks)
 {
  void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
  var steps=new Dictionary<long,JsonObject>{[1]=O(("id",1),("conditionType",19),("nextQuestId",2)),[2]=O(("id",2),("conditionType",1),("nextQuestId",0))};
  var data=new DailyNpcData(O(("Wild",new JsonArray(O(("Step",2),("Map",11),("Available",true))))),steps,[O(("id",1))]);
  Check(data.Protects(11)&&!data.Protects(12),"future quest kill targets are protected only on their map");
  var activeKill=data with { Active=[O(("id",2))] };
  Check(!activeKill.Protects(11),"active kill objective allows suppression to credit the quest");
  var selected=DailyWeeklyRoute.Select(["weekly_npc"],new(){Npc=true,Mainline=false,Steal=false});
  Check(selected.SequenceEqual(new[]{"weekly_npc"}),"NPC-only run does not silently enable weekly collection");
  Check(DailyWeeklyRoute.StageOrder.Where(s=>new[]{"weekly_npc","weekly_mainline"}.Contains(s)).First()=="weekly_mainline","enabled collection clears a shared map before NPC interactions");
  using(var h=new WorkflowHarness(Path.Combine(root,"clear-map"),[])){
   h.Page("GameFieldDefaultUI");
   h.Readings=()=>[WorkflowCases.Reading("mainline.map",(DailyFieldRoute.MapPath,O(("id",11))),("HasCanOverwhelmMonster()",false))];
   var workflow=new DailyWorkflow(h.Root,Path.Combine(TestPaths.SourceRoot,"assets"),h.Context,h.Driver,h.Business,h.Workflow.Navigation,[],()=>h.Stopped,h.Workflow.Relay);
   var route=new DailyFieldRoute(workflow);await route.PrepareNpcMap(11);
   Check(h.Box.Commands.Count==0,"already cleared NPC map does not consume a suppression or stealth skill");
   bool rejected=false;try{await route.PrepareNpcMap(12);}catch(InvalidDataException){rejected=true;}
   Check(rejected&&h.Box.Commands.Count==0,"changed map is rejected before any safety skill");
  }
  foreach(bool protect in new[]{false,true})
  {
   using var h=new WorkflowHarness(Path.Combine(root,protect?"protected-map":"disabled-suppression"),[]);
   int skill=0;var menus=new List<int>();h.Page("GameFieldDefaultUI");
   h.OnCommand=c=>{if(S(c["Kind"])=="mainline_menu"){skill=I(c["Value"]);menus.Add(skill);h.Page("QuickMenuUI");}else if(S(c["Kind"])=="back")h.Page("GameFieldDefaultUI");else throw new Exception("Safety check unexpectedly consumed a skill");};
   h.Readings=()=>{
    var candidate=WorkflowCases.Reading("dispatch.talent",("ὬὩὬὡὯὮὨὣὩὦὡ",DailyFieldRoute.Kinds.GetValueOrDefault(skill,"")),("_objDisableImage.activeSelf",true));candidate["InstanceId"]=71;
    return [WorkflowCases.Reading("mainline.map",(DailyFieldRoute.MapPath,O(("id",11))),("HasCanOverwhelmMonster()",true)),
     WorkflowCases.Reading("mainline.reset",("GetWeeklyResetTime().Ticks",500)),WorkflowCases.Reading("mainline.talent_rows",("$self",new JsonArray(O(("Instance",71),("Kind",skill),("Group",skill*100+5),("PackRestricted",false),("Reason","catalyst_insufficient"),("Gate","talent_reject:catalyst_insufficient"),("Cooldown",0))))),WorkflowCases.Reading("mainline.talent_counts",("Values",new JsonArray())),candidate];
   };
   var workflow=new DailyWorkflow(h.Root,Path.Combine(TestPaths.SourceRoot,"assets"),h.Context,h.Driver,h.Business,h.Workflow.Navigation,[],()=>h.Stopped,h.Workflow.Relay);
   var route=new DailyFieldRoute(workflow){CanSuppress=_=>!protect};bool stopped=false;
   try{await route.PrepareNpcMap(11);}catch(InvalidDataException e){stopped=e.Message.Contains("无法压制或藏身");if(!stopped)throw new Exception("Unexpected safety result: "+e.Message+"; menus="+string.Join(",",menus),e);}
   Check(stopped&&menus.SequenceEqual(protect?new[]{17}:new[]{3,17}),protect?"protected future quest skips suppression and tries stealth":"unavailable suppression falls back to stealth without moving into monsters");
  }
  var mail=new DailyEventMailDependency(true);int visits=0;
  Task<JsonObject> Collect(){visits++;return Task.FromResult(O(("state","completed")));}
  await mail.Collect(Collect);await mail.Collect(Collect);
  Check(visits==1&&!mail.Required,"token activities check old pending mail once, not once per page");
  mail.RewardClaimed();await mail.Collect(Collect);
  Check(visits==2&&mail.Results.Count==2,"new mission reward enables another bounded mail dependency");
  mail.RewardClaimed();bool failed=false;
  try{await mail.Collect(()=>Task.FromResult(O(("state","partial"))));}catch(InvalidDataException){failed=true;}
  Check(failed&&mail.Required,"incomplete mailbox does not falsely release token consumers");
  var disabled=new DailyEventMailDependency(false);disabled.RewardClaimed();await disabled.Collect(Collect);
  Check(visits==2,"no selected token activity adds no mailbox detour");
  var entry=O(("state","completed"),("kind","MiniGamePuzzleUI"),("cycle","today"),("currency_key","1:123"),("cost",1));
  var balances=new Dictionary<string,long>();var missions=new Dictionary<(long,long),JsonObject>();
  Check(!DailyEventRewards.Needed(entry,"today",missions,balances),"empty puzzle is cached before receiving pens");
  balances["1:123"]=5;
  Check(DailyEventRewards.Needed(entry,"today",missions,balances),"mailbox pen delivery invalidates an earlier empty puzzle visit");
 }
}
