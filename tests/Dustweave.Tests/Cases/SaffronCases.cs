using System.Text.Json;
using System.Text.Json.Nodes;
using BD2Territory;
using Dustweave;

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
        foreach(string phase in new[]{"idle","survey","prepare","buff","recover","replant","cook","sell","completed","skipped"})
            Check(!SaffronCycle.AllowHarvest(phase,15,true),"only the harvest phase can gather saffron: "+phase);
        Check(SaffronCycle.AllowHarvest("harvest",15,true)&&!SaffronCycle.AllowHarvest("harvest",2,true)&&!SaffronCycle.AllowHarvest("harvest",15,false),"harvest is limited to captured saffron fields");
        Check(SaffronCycle.AllowHarvest("potatoes",2,true)&&!SaffronCycle.AllowHarvest("potatoes",15,true),"potato rounds never harvest newly planted saffron");
        const long end=600_000;
        string Action(long now,bool filling,int empty,int mature,int growing,bool affordable=true,bool capacity=true)
            =>SaffronCycle.PotatoAction(now,end,60,60,filling,empty,mature,growing,affordable,capacity);
        Check(Action(120_000,false,100,0,0)=="plant","the remaining yield buff starts a potato round");
        Check(Action(140_000,true,50,1,50)=="plant","separate farm patches finish planting the same round before harvesting");
        Check(Action(170_000,true,0,0,100)=="wait","planted potatoes wait for growth without replanting saffron");
        Check(Action(230_000,false,0,100,100)=="harvest","mature potatoes are harvested");
        Check(Action(300_000,false,100,0,0)=="plant","a second potato round uses the remaining food duration");
        Check(Action(490_000,false,100,0,0)=="drain","a round that cannot grow and finish harvesting in time is not started");
        Check(Action(end-1,false,100,0,0)=="drain"&&Action(end,false,100,0,0)=="replant","saffron replanting waits until yield buffs expire");
        Check(Action(end+1,false,0,0,100)=="wait"&&Action(end+60_000,false,0,100,100)=="harvest","late potatoes are retained until mature instead of overwritten");
        Check(Action(100_000,false,100,0,0,false)=="drain"&&Action(100_000,false,100,0,0,true,false)=="drain","potato rounds protect replant funds and inventory capacity");
        Check(SaffronCycle.SellCount(0)==0&&SaffronCycle.SellCount(1)==0&&SaffronCycle.SellCount(100)==99,"finished food always keeps one for the next harvest");
    }
}
