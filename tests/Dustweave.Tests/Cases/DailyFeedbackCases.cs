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
  await FieldEncounterCases.Run(Path.Combine(root,"field-encounters"),checks);
 }
}

static class FieldEncounterCases
{
 const string Pause="$pointer/HUD/ButtonLayout/Button - Pause/Button - Pause";
 static void Page(WorkflowHarness h,int step)
 {
  if(step==3){h.Page("GameFieldDefaultUI");return;}
  JsonObject Target(int id,string field,string route="ui")=>O(("Id",id),("Field",field),("Enabled",true),("Route",route));
  JsonObject Surface(int id,string type,bool popup,int order,params JsonObject[] targets)=>O(("Id",id),("Type",type),("Popup",popup),("Order",order),("InputReady",true),("Targets",Array(targets)));
  var surfaces=new JsonArray(Surface(101,"BattleUI_FieldBattle",false,0,Target(1001,Pause,"pointer"),Target(1002,"_objBattleStartBtn")));
  if(step>=1)surfaces.Add(Surface(102,"BattlePauseUI",true,10,Target(2001,"_objectRun")));
  if(step>=2)surfaces.Add(Surface(103,"BattlePopupUI",true,20,Target(3001,"_objGiveUpButton")));
  h.Frame["Surfaces"]=surfaces;Token(h);
 }
 static void Token(WorkflowHarness h)=>h.Frame["UiToken"]=DailyIdentity.Hash(h.Frame["Surfaces"]!.ToJsonString());
 static int Step(JsonObject command)=>I(command["SurfaceId"])-101;
 public static async Task Run(string root,List<string> checks)
 {
  void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
  WorkflowHarness Harness(string name,int step=0)
  {
   var h=new WorkflowHarness(Path.Combine(root,name),[]);h.Frame["Scene"]="Map1005_002";Page(h,step);
   h.OnCommand=c=>Page(h,Step(c)+1);return h;
  }
  foreach(int initial in new[]{0,1,2})
  {
   using var h=Harness("normal-"+initial,initial);var route=new DailyFieldRoute(h.Workflow);
   await route.ExitEncounter(h.Frame);
   Check(h.Box.Commands.Select(Step).SequenceEqual(Enumerable.Range(initial,3-initial)),"field retreat resumes directly from observed step "+initial);
   Check(h.Box.Commands.All(c=>I(c["TargetId"])!=1002)&&h.Time<1,"field retreat never starts a fight or waits after a visible transition "+initial);
   if(initial==0)Check(S(h.Box.Commands[0]["Kind"])=="pointer"&&I(h.Box.Commands[0]["TargetId"])==1001,"field retreat uses the real pause button instead of assuming Back opens it");
  }
  foreach(int stalled in new[]{0,1,2})
  {
   using var h=Harness("retry-"+stalled);int count=0;
   h.OnCommand=c=>{int step=Step(c);if(step==stalled&&++count<3)return;Page(h,step+1);};
   await new DailyFieldRoute(h.Workflow).ExitEncounter(h.Frame);
   Check(count==3&&h.Box.Commands.Count==5,"same field retreat step retries only its unchanged control "+stalled);
  }
  using(var h=Harness("delayed-pause"))
  {
   double next=double.PositiveInfinity;
   h.OnCommand=c=>{if(Step(c)==0)next=h.Time+1;else Page(h,Step(c)+1);};
   h.OnDelay=()=>{if(h.Time>=next){next=double.PositiveInfinity;Page(h,1);}};
   await new DailyFieldRoute(h.Workflow).ExitEncounter(h.Frame);
   Check(h.Box.Commands.Count==3,"delayed pause arrival advances without repeating the delivered click");
  }
  using(var h=Harness("loading-after-confirm"))
  {
   double next=double.PositiveInfinity;
   h.OnCommand=c=>{if(Step(c)!=2){Page(h,Step(c)+1);return;}h.Page("LoadingUI");h.Frame["Scene"]="Empty";next=h.Time+2;};
   h.OnDelay=()=>{if(h.Time>=next){next=double.PositiveInfinity;h.Frame["Scene"]="Map1005_002";Page(h,3);}};
   await new DailyFieldRoute(h.Workflow).ExitEncounter(h.Frame);
   Check(h.Box.Commands.Count==3,"confirmed retreat waits through loading and verifies return to the original map");
  }
  using(var h=Harness("button-disabled"))
  {
   h.Frame["Surfaces"]![0]!["Targets"]![0]!["Enabled"]=false;Token(h);
   h.OnDelay=()=>{if(h.Time>=.6){h.Frame["Surfaces"]![0]!["Targets"]![0]!["Enabled"]=true;Token(h);}};
   await new DailyFieldRoute(h.Workflow).ExitEncounter(h.Frame);
   Check(h.Time>=.6&&h.Box.Commands.Count==3&&S(h.Box.Commands[0]["Kind"])=="pointer","disabled pause button waits for readiness and is not bypassed with Back");
  }
  using(var h=Harness("return-coroutine"))
  {
   double next=double.PositiveInfinity;
   h.OnCommand=c=>{if(Step(c)!=2){Page(h,Step(c)+1);return;}Page(h,0);next=h.Time+1;};
   h.OnDelay=()=>{if(h.Time>=next){next=double.PositiveInfinity;Page(h,3);}};
   await new DailyFieldRoute(h.Workflow).ExitEncounter(h.Frame);
   Check(h.Box.Commands.Count==3,"give-up dialogs can close before the field-return animation finishes without reopening pause");
  }
  using(var h=Harness("legacy-back"))
  {
   h.Frame["Surfaces"]![0]!["Targets"]!.AsArray().RemoveAt(0);Token(h);
   await new DailyFieldRoute(h.Workflow).ExitEncounter(h.Frame);
   Check(S(h.Box.Commands[0]["Kind"])=="back","missing pause pointer retains the guarded compatibility fallback");
  }
  foreach(int stalled in new[]{0,1,2})
  {
   using var h=Harness("bounded-"+stalled);h.OnCommand=c=>{if(Step(c)!=stalled)Page(h,Step(c)+1);};
   bool failed=false;try{await new DailyFieldRoute(h.Workflow).ExitEncounter(h.Frame);}catch(StageHostException e){failed=e.Kind=="adapter"&&e.Message.Contains("已尝试3次");}
   Check(failed&&h.Box.Commands.Count(c=>Step(c)==stalled)==3,"unchanged retreat step stops after three attempts with an actionable reason "+stalled);
  }
  foreach(string change in new[]{"battle","map","account","cycle","popup","target","regression","stop","lease"})
  {
   using var h=Harness("changed-"+change,change=="regression"?1:0);
   h.OnCommand=c=>{
    switch(change){
     case "battle":h.Frame["Surfaces"]![0]!["Id"]=999;break;
     case "map":h.Frame["Scene"]="Map1005_003";break;
     case "account":h.Frame["AccountKey"]="other";h.Context["actor"]![3]="other";break;
     case "cycle":h.Context["cycle"]="tomorrow";break;
     case "popup":h.Frame["Surfaces"]!.AsArray().Add(O(("Id",999),("Type","MessagePopupUI"),("Popup",true),("Order",100),("Targets",new JsonArray())));break;
     case "target":h.Frame["Surfaces"]![0]!["Targets"]![0]!["Id"]=999;break;
     case "regression":Page(h,0);break;
     case "stop":h.Stopped=true;break;
     case "lease":h.Box.Revoked=true;break;
    }
    Token(h);
   };
   bool failed=false;try{await new DailyFieldRoute(h.Workflow).ExitEncounter(h.Frame);}catch(Exception e)when(e is InvalidDataException or StageHostException or DailyStepException or BD2.LocalIpc.LeaseRevokedException){failed=true;}
   Check(failed&&h.Box.Commands.Count==1,"field retreat never retries after a changed or stopped flow: "+change);
  }
  using(var h=Harness("preflight-transition"))
  {
   int reads=0;h.OnRead=()=>{if(++reads==2)Page(h,1);};
   await new DailyFieldRoute(h.Workflow).ExitEncounter(h.Frame);
   Check(h.Box.Commands.Select(Step).SequenceEqual(new[]{1,2}),"pause opening during preflight is reobserved instead of clicking stale UI");
  }
  using(var h=Harness("unknown-receipt"))
  {
   h.Box.State="unknown";h.OnCommand=_=>{};
   bool failed=false;try{await new DailyFieldRoute(h.Workflow).ExitEncounter(h.Frame);}catch(DailyStepException e){failed=e.Kind=="pending";}
   Check(failed&&h.Box.Commands.Count==1,"unknown command receipt is not converted into permission to replay");
  }
  foreach(int lateStep in new[]{0,1,2})
  {
   using var h=Harness("late-progress-"+lateStep,lateStep);h.Box.State="unknown";
   double next=double.PositiveInfinity;
   h.OnCommand=c=>{if(Step(c)==lateStep){next=h.Time+1;h.Box.State="observed_after_dispatch";}else Page(h,Step(c)+1);};
   h.OnDelay=()=>{if(h.Time>=next){next=double.PositiveInfinity;Page(h,lateStep+1);}};
   await new DailyFieldRoute(h.Workflow).ExitEncounter(h.Frame);
   Check(h.Box.Commands.Select(Step).SequenceEqual(Enumerable.Range(lateStep,3-lateStep)),"fresh UI progress resolves an uncertain retreat receipt without replaying step "+lateStep);
  }
  using(var h=Harness("returned-before-input"))
  {
   var original=h.Frame.DeepClone().AsObject();Page(h,3);
   await new DailyFieldRoute(h.Workflow).ExitEncounter(original);
   Check(h.Box.Commands.Count==0,"already returned field completes retreat without additional input");
  }
  using(var h=Harness("encounter-integration"))
  {
   h.Readings=()=>[WorkflowCases.Reading("mainline.map",(DailyFieldRoute.MapPath,O(("id",10052))),("HasCanOverwhelmMonster()",false)),WorkflowCases.Reading("mainline.durations",("entries",new JsonArray(O(("Key",17),("Value.ὫὢὢὬὫὫὡὩὪὩὮ",true),("Value.ὠὨὠὭὬὤὪὢὫὨὮ",20)))))];
   var route=new DailyFieldRoute(h.Workflow){RecoverEncounters=true};
   Check(await route.Encounter(h.Frame)&&h.Box.Commands.Count==3,"full encounter recovery returns to field and reuses active stealth without consuming another skill");
  }
  using(var h=Harness("npc-battle"))
  {
   var route=new DailyFieldRoute(h.Workflow){RecoverEncounters=true,NpcActing=true};
   Check(!await route.Encounter(h.Frame)&&h.Box.Commands.Count==0,"NPC objective battles remain excluded from automatic retreat");
  }
 }
}
