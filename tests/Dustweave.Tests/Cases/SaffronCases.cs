using System.Text.Json;
using System.Text.Json.Nodes;
using BD2Territory;
using Dustweave;
using static Dustweave.DailyData;

static class SaffronCases
{
    public static void Run(List<string> cases)
    {
        void Check(bool value,string text) { if(!value)throw new Exception(text); cases.Add(text); }
        var journal = new SaffronJournal { Account="fixture", Pending="eat", State="buff", Reason="recover", BuffEnd=1234, Harvested=5, At=987, Currency=654, Stocks=[1,2,3] };
        var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(SaffronJournal));
        using var bytes = new MemoryStream();
        serializer.WriteObject(bytes, journal); bytes.Position=0;
        var restored = (SaffronJournal)serializer.ReadObject(bytes)!;
        Check(restored.Account==journal.Account && restored.Pending==journal.Pending && restored.State==journal.State && restored.Reason==journal.Reason
            && restored.BuffEnd==1234 && restored.Harvested==5 && restored.At==987 && restored.Currency==654 && restored.Stocks.SequenceEqual(journal.Stocks),
            "runtime DataContractJsonSerializer round-trips saffron recovery before any consumable action");
        var settings=new DailyPreferences();
        Check(!settings.Stages.Saffron&&!DailyStageCatalog.Enabled("territory_saffron",settings),"saffron is opt-in for new accounts");
        var old=JsonSerializer.SerializeToNode(settings)!.AsObject();old["Schema"]=17;old["Stages"]!.AsObject().Remove("Saffron");old["Stages"]!["Guild"]=false;
        var migrated=DailyPreferences.Parse(old.ToJsonString());
        Check(migrated.Schema==18&&!migrated.Stages.Saffron&&!migrated.Stages.Guild,"schema 17 keeps existing task choices and never enables crop purchases");
        settings.Stages.Saffron=true;
        Check(DailyPreferences.Parse(JsonSerializer.Serialize(settings)).Stages.Saffron&&DailyStageCatalog.Enabled("territory_saffron",settings),"saffron selection survives account preference round trip");
        Check(DailyNavigationDecision.FieldStages.Contains("territory_saffron"),"saffron can reuse the already-open territory cartridge");
        var command=DailySaffron.Command("test-owner",123,1000);
        Check(command["SaffronDaily"]!.GetValue<bool>()&&!command["Logging"]!.GetValue<bool>()&&!command["Mining"]!.GetValue<bool>(),"daily crop control never enables trees or mining");
        Check(command["UntilUtcTicks"]!.GetValue<long>()==1000+TimeSpan.TicksPerSecond*3,"crop control expires if daily supervision disappears");
        Check(!SaffronCycle.CanBuff(100,false,true,true,true,30,600),"unripe crop state cannot authorize food use regardless of elapsed desktop time");
        Check(SaffronCycle.CanBuff(100,true,true,true,true,30,600),"mature reachable fields fit the conservative harvest deadline");
        foreach(var blockers in new[]{(false,true,true),(true,false,true),(true,true,false)})
            Check(!SaffronCycle.CanBuff(100,true,blockers.Item1,blockers.Item2,blockers.Item3,30,600),"reachability, capacity and funds each block consuming yield food");
        Check(!SaffronCycle.CanBuff(0,true,true,true,true,0,600),"empty fields never consume food");
        Check(!SaffronCycle.CanBuff(100,true,true,true,true,30,280),"expired or insufficient food time cannot start a harvest");
        Check(!SaffronCycle.CanBuff(100,true,true,true,true,double.NaN,600),"invalid route time cannot authorize a harvest");
        string Action(int completed,bool filling,int empty,int mature,int growing,bool affordable=true,bool capacity=true)
            =>SaffronCycle.PotatoAction(completed,3,filling,empty,mature,growing,affordable,capacity);
        Check(Action(0,false,100,0,0)=="plant","the fixed plan starts its first potato round");
        Check(Action(0,true,50,1,50)=="plant","separate farm patches finish planting the same round before harvesting");
        Check(Action(0,true,0,0,100)=="wait","planted potatoes wait for growth without replanting saffron");
        Check(Action(0,false,0,100,100)=="harvest","mature potatoes are harvested");
        Check(Action(1,false,100,0,0)=="plant"&&Action(2,false,100,0,0)=="plant","the second and third rounds are independent of food expiry");
        Check(Action(3,false,100,0,0)=="replant","the completed target replants immediately without waiting for food expiry");
        Check(Action(3,false,0,0,100)=="wait"&&Action(3,false,0,100,100)=="harvest","a pending planted batch is finished even if the configured target is reduced");
        Check(Action(0,false,100,0,0,false)=="limited"&&Action(0,false,100,0,0,true,false)=="limited","resource shortage defers only this stage rather than waiting for buffs");
        Check(SaffronCycle.SellCount(0)==0&&SaffronCycle.SellCount(1)==0&&SaffronCycle.SellCount(100)==99,"finished food always keeps one for the next harvest");
        Check(SaffronCycle.ResumePhase(0,0,100,0,false,true,false)=="potatoes","retry with planted potatoes resumes maturation instead of requiring empty land");
        Check(SaffronCycle.ResumePhase(100,0,0,0,false,true,false)=="potatoes","a harvested cycle resumes remaining fixed rounds without eating again");
        Check(SaffronCycle.ResumePhase(0,0,100,0,false,false,false)=="potatoes","expired buffs still permit finishing already planted potatoes");
        Check(SaffronCycle.ResumePhase(100,0,0,0,false,false,false)=="replant","initial empty land plants saffron without food");
        Check(SaffronCycle.ResumePhase(25,75,0,0,false,true,true)=="replant","interrupted replant fills only empty land and continues cooking");
        Check(SaffronCycle.ResumePhase(25,75,0,0,false,false,false)=="unripe","unripe saffron without recent replant evidence never authorizes food");
        Check(SaffronCycle.ResumePhase(25,75,0,0,true,true,false)=="survey","partially harvested ripe saffron must recheck access before consuming or harvesting");
        Check(SaffronCycle.ResumePhase(25,0,25,50,false,true,false)=="potatoes","mixed mature crops do not block a resumed potato cycle");
        Check(SaffronCycle.ResumePhase(0,50,50,0,true,true,false)=="survey","mixed fields still prepare yield food before harvesting ripe saffron");
        Check(SaffronCycle.ResumePhase(0,0,0,100,false,false,false)=="replant","other mature crops can be harvested normally before replanting saffron");
        var previous=JsonSerializer.SerializeToNode(new DailyPreferences())!.AsObject();previous.Remove("SaffronPotatoRounds");
        Check(DailyPreferences.Parse(previous.ToJsonString()).SaffronPotatoRounds==3,"existing accounts default to three fixed potato rounds without enabling saffron");
        foreach(int target in new[]{1,2,3})
        {
            var p=new DailyPreferences{SaffronPotatoRounds=target};p.Validate();
            Check(DailyPreferences.Parse(JsonSerializer.Serialize(p)).SaffronPotatoRounds==target&&I(DailySaffron.Command("o",123,1000,target)["SaffronPotatoRounds"])==target,"chosen potato round count survives preferences and the runtime command: "+target);
            var cycle=new SaffronJournal{PotatoRoundLimit=target};
            for(int batch=0;batch<target;batch++)
            {
                Check(SaffronCycle.PotatoAction(cycle.PotatoRounds,target,false,100,0,0,true,true)=="plant","remaining fixed rounds permit a new planting: "+target+"/"+batch);
                SaffronCycle.ObservePotatoRound(cycle,100,0,100);
                Check(SaffronCycle.PotatoAction(cycle.PotatoRounds,target,false,99,1,1,true,true)=="harvest","partially harvested fields do not start an extra round: "+target+"/"+batch);
                using(var checkpoint=new MemoryStream()){serializer.WriteObject(checkpoint,cycle);checkpoint.Position=0;cycle=(SaffronJournal)serializer.ReadObject(checkpoint)!;}
                SaffronCycle.ObservePotatoRound(cycle,100,100,0);
                SaffronCycle.ObservePotatoRound(cycle,100,100,0);
            }
            Check(cycle.PotatoRounds==target&&SaffronCycle.PotatoAction(cycle.PotatoRounds,target,false,100,0,0,true,true)=="replant","native harvest evidence and reconnect finish exactly the selected number of rounds: "+target);
        }
        foreach(int invalid in new[]{0,4})
        {
            bool rejected=false;try{new DailyPreferences{SaffronPotatoRounds=invalid}.Validate();}catch(InvalidDataException){rejected=true;}
            Check(rejected,"out-of-range potato count is rejected before food use: "+invalid);
        }
        var round=new SaffronJournal{PotatoRoundLimit=3,Filling=true};
        Check(!SaffronCycle.ObservePotatoRound(round,100,100,0)&&round.PotatoRounds==0,"pre-plant empty snapshot cannot complete a round");
        Check(SaffronCycle.ObservePotatoRound(round,100,50,50)&&round.PotatoRoundPlanted&&round.PotatoRounds==0,"native planted fields begin a round without counting it as harvested");
        Check(!SaffronCycle.ObservePotatoRound(round,100,99,1)&&round.PotatoRounds==0,"one remaining potato keeps the round unfinished");
        using(var saved=new MemoryStream()) {
            serializer.WriteObject(saved,round);saved.Position=0;round=(SaffronJournal)serializer.ReadObject(saved)!;
        }
        Check(SaffronCycle.ObservePotatoRound(round,100,100,0)&&round.PotatoRounds==1&&!round.PotatoRoundPlanted&&!round.Filling,"recovery after the last harvest completes exactly one persisted potato round");
        Check(!SaffronCycle.ObservePotatoRound(round,100,100,0)&&round.PotatoRounds==1,"repeated empty snapshots never double-count a completed round");
        round.PotatoRoundPlanted=true;
        Check(!SaffronCycle.ObservePotatoRound(round,0,0,0)&&!SaffronCycle.ObservePotatoRound(round,100,90,0),"unloaded or partly unknown fields cannot complete a potato round");
    }
    public static async Task RuntimeLoop(string output,List<string> cases)
    {
        void Check(bool value,string text){if(!value)throw new Exception(text);cases.Add(text);}
        async Task<(JsonObject Result,double Time,int Starts,bool Stopped)> Sim(string name,Func<double,bool> ready,double completeAt,
            string phase="potatoes",bool frozenWait=false,bool changeOwner=false,bool staleError=false,bool waiting=false)
        {
            using var h=new WorkflowHarness(Path.Combine(output,"saffron-"+name),[]);
            JsonObject control=new();int starts=0;bool stopped=false;
            JsonObject Read(string file)
            {
                if(file=="control.json")return changeOwner&&h.Time>4?O(("OwnerId","another"),("Enabled",true),("UntilUtcTicks",h.Ticks+30_000_000)):control.DeepClone().AsObject();
                bool enabled=B(control["Enabled"]);
                return O(("ProcessId",h.Context["actor"]![0]),("CapturedUtcTicks",h.Ticks),("Ready",ready(h.Time)),("Enabled",enabled),
                    ("OwnerId",control["OwnerId"]),("Error",staleError&&starts==0?"old owner error":""),
                    ("Saffron",O(("State",h.Time>=completeAt?"completed":phase),("Harvested",100),("PotatoRounds",1),
                        ("ServerNow",frozenWait?1000:1000+(long)(h.Time*1000)),("WaitUntil",waiting?91000:0))),
                    ("GatherReplies",frozenWait||waiting?0:(int)(h.Time/30)));
            }
            void Write(string file,JsonNode value){control=value.DeepClone().AsObject();if(B(control["Enabled"]))starts++;else stopped=true;}
            try
            {
                var result=await DailySaffron.RunConnected(h.Workflow,Read,Write);
                return(result,h.Time,starts,stopped);
            }
            catch(StageHostException e) when(e.Kind=="identity") {return(O(("state","identity")),h.Time,starts,stopped);}
        }
        var good=await Sim("late-transition",t=>t<200||t>203,240);
        Check(S(good.Result["state"])=="completed"&&good.Stopped,"a brief HUD transition after food and harvest resumes with the same owner, then releases control");
        var missing=await Sim("late-missing",t=>t<200,500);
        Check(S(missing.Result["state"])=="partial"&&missing.Time is >207 and <212&&missing.Stopped&&!S(missing.Result["reason"]).Contains("未使用料理"),"persistent lost territory ends only this stage within eight seconds and does not deny food already consumed");
        var first=await Sim("initial-missing",_=>false,500);
        Check(S(first.Result["state"])=="partial"&&first.Starts==0&&first.Time<9,"unavailable startup never submits a crop or food command");
        var growing=await Sim("valid-growth-wait",_=>true,160,waiting:true);
        Check(S(growing.Result["state"])=="completed"&&growing.Stopped,"legitimate potato growth time is excluded from the inactivity window");
        var frozen=await Sim("frozen-growth-wait",_=>true,1000,frozenWait:true,waiting:true);
        Check(S(frozen.Result["state"])=="partial"&&frozen.Time<215&&frozen.Stopped,"a frozen crop clock cannot extend the wait indefinitely behind a popup");
        var changed=await Sim("owner-change",_=>true,50,changeOwner:true);
        Check(S(changed.Result["state"])=="identity"&&!changed.Stopped,"ownership changes stop daily without disabling another controller");
        var oldError=await Sim("old-error",_=>true,2,staleError:true);
        Check(S(oldError.Result["state"])=="completed"&&oldError.Starts>0,"a previous owner's stale runtime error cannot prevent a new retry from starting");
    }
}
