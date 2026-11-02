using System.Text.Json.Nodes;
namespace Dustweave.Soul;
public static class SoulChecks
{
    public static void ValidateKernel(string root,string client,string fixtures,string output)
    {
        SoulAssets.Prepare(root,client);using var kernel=new NativeBattleKernel(SoulAssets.Tables,SoulAssets.Library);
        int count=0,battles=0;
        foreach(string line in File.ReadLines(fixtures)){
            if(string.IsNullOrWhiteSpace(line))continue;var fixture=JsonNode.Parse(line)!;
            var compiled=kernel.Compile(fixture["input"]!);var seeds=fixture["seeds"]!.AsArray().Select(s=>s!.GetValue<int>()).ToArray();
            var result=kernel.Run(compiled,seeds);
            string Hash(ReadOnlySpan<byte> data)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
            if(Hash(compiled)!=fixture["compiledSha256"]!.ToString()||Hash(System.Runtime.InteropServices.MemoryMarshal.AsBytes(result.AsSpan()))!=fixture["resultsSha256"]!.ToString())
                throw new InvalidDataException("Packaged Soul kernel differs from calibrated case: "+fixture["name"]);
            count++;battles+=seeds.Length;
        }
        File.WriteAllText(output,System.Text.Json.JsonSerializer.Serialize(new{status="passed",cases=count,battles,kernel=kernel.KernelSha256,table=kernel.TableSha256,realGameTouched=false}));
    }
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        Storage(Path.Combine(output,"tools","soul","runs","fixture"));
        SaleSynchronization();
        Resume();
        ManualProgress();
        NativeLayoutEncoder.Test();NativePlanExecutor.Test();SoulSubmit.Test();NativeSearchRisk.Test();NativeGrowthPolicy.Test();
        SoulViewAudit.Test();SoulPacking.Test();SoulReservePolicy.Test();SoulShopPolicy.Test();SoulEntryReceipt.Test();
        var half=NativeFastPlanner.RetryValue([.5,.5],[1,1],2);
        if(Math.Abs(half.Clear-.5)>1e-12||Math.Abs(half.Seconds-2.5)>1e-12)throw new Exception("Finite-life continuation recurrence differs from enumerated outcomes");
        var win=NativeFastPlanner.RetryValue([1,1],[2,3],5);
        var loss=NativeFastPlanner.RetryValue([0,1],[2,3],5);
        if(win.Clear!=1||win.Seconds!=5||loss.Clear!=0||loss.Seconds!=10)throw new Exception("Continuation terminal or retry cost is incorrect");
        if(NativeFastPlanner.RetryValue([.5,.5],[1,1],3).Clear<=half.Clear)throw new Exception("Extra lives reduced continuation value");
        if(NativeFastPlanner.ContinuationRank(0,1,100).CompareTo(NativeFastPlanner.ContinuationRank(0,.99,0))<=0)throw new Exception("Terminal cash displaced a stronger final-wave board");
        var state=JsonNode.Parse("""{"progress":{"SeasonDungeons":[11,12,13,14,15],"WaveCounts":[5,10,10,15,15],"ClearedDungeons":[11,12,13,14,15]}}""")!.AsObject();
        var run=new SoulCampaign();run.Initialize(state,"account-a","player-a");
        if(run.Target!=11)throw new Exception("Old clears must not finish a fresh challenge.");
        for(int i=0;i<5;i++)for(int w=1;w<=run.Waves[i];w++){
            if(run.Target!=run.Dungeons[i])throw new Exception("Skipped a dungeon.");
            run.BeginBattle(run.Target,w);
            if(i==2&&w==4){run.Settle(false);if(run.Target!=13)throw new Exception("Loss skipped the dungeon.");run.BeginBattle(13,4);}
            run.Settle(true);
        }
        if(run.Target!=0||run.Wins!=55||run.Losses!=1||run.Completed.Count!=5)throw new Exception("Full campaign counts differ.");
        try{run.Initialize(state,"account-b","player-a");throw new Exception("Identity guard missing.");}catch(InvalidDataException){}
        var pending=new SoulCampaign();pending.Initialize(state,"a","b");pending.BeginBattle(11,1);
        try{pending.BeginBattle(11,1);throw new Exception("Double dispatch allowed.");}catch(InvalidDataException){}
        var retry=new SoulCampaign();retry.Initialize(state,"retry-account","retry-player");retry.Completed.AddRange([11,12,13]);
        for(int attempt=0;attempt<50;attempt++){
            for(int life=0;life<5;life++){retry.BeginBattle(14,1);retry.Settle(false);}
            retry.Status="defeated";retry.RetryDefeatedDungeon();
            if(retry.Target!=14||!retry.Completed.SequenceEqual(new[]{11,12,13})||retry.FailedAttempts[14]!=attempt+1)throw new Exception("Retry lost cleared dungeons or skipped its target");
            // A persisted old defeated record may be read again after a
            // process interruption. Counting that same settlement is idempotent.
            retry.Status="defeated";
            retry=System.Text.Json.JsonSerializer.Deserialize<SoulCampaign>(System.Text.Json.JsonSerializer.Serialize(retry))!;
            retry.RetryDefeatedDungeon();
            if(retry.FailedAttempts[14]!=attempt+1)throw new Exception("Resuming counted a defeat twice");
        }
        if(retry.Battles!=250||retry.Target!=14)throw new Exception("Repeated losses exhausted a tool-only attempt cap");
        retry.BeginBattle(14,1);retry.Status="defeated";
        try{retry.RetryDefeatedDungeon();throw new Exception("Retry replayed an unsettled battle");}catch(InvalidDataException){}
        retry.Settle(true);
        for(int wave=2;wave<=15;wave++){retry.BeginBattle(14,wave);retry.Settle(true);}
        if(retry.Target!=15||retry.Completed.Count!=4)throw new Exception("A retried dungeon did not advance after clearing");
        run.Save(output);
        File.WriteAllText(Path.Combine(output,"result.json"),"""{"status":"passed","realGameTouched":false,"campaignWaves":55,"losses":1,"checks":"layout, transactions, unknown receipts, growth identity, full replay, battle identity"}""");
    }
    static void Resume()
    {
        var season=JsonNode.Parse("""{"progress":{"SeasonDungeons":[11,12,13,14,15],"WaveCounts":[5,10,10,15,15],"ClearedDungeons":[11,12,13,14,15]}}""")!.AsObject();
        JsonObject Reply(string role,JsonObject body,long ticks=long.MaxValue)=>new(){["Role"]=role,["Kind"]="response",["Accepted"]=true,["AtUtcTicks"]=ticks,["Values"]=new JsonArray(new JsonObject{["Json"]=body.ToJsonString()})};
        JsonObject Progress(int dungeon,int wave,int life,bool active=false)=>Reply("soul.IbMainInfo",new JsonObject{
            ["regularSeason"]=3,["clearedDungeonId"]=new JsonArray(11,12,13,14,15),
            ["playInfo"]=new JsonObject{["season"]=3,["dungeonId"]=dungeon,["stageId"]=wave,["life"]=life,["isStageEnter"]=active}});
        SoulCampaign Pending(int wave=2){
            var run=new SoulCampaign();run.Initialize(season,"account","player");run.Completed.AddRange([11,12]);
            run.BeginBattle(13,wave,new JsonObject{["play"]=new JsonObject{["season"]=3,["life"]=5}});
            // Exercise the actual persisted schema, including a restart while
            // the last battle callback has not arrived.
            return System.Text.Json.JsonSerializer.Deserialize<SoulCampaign>(System.Text.Json.JsonSerializer.Serialize(run))!;
        }
        var pending=Pending();
        if(pending.Reconcile(Progress(13,2,5,true))!="active"||pending.PendingDungeon!=13)throw new Exception("An active battle was replayed or lost");
        if(pending.Reconcile(Progress(13,3,5))!="won"||pending.Wins!=1)throw new Exception("Reconnect did not accept actual advanced progress");
        if(pending.Reconcile(Progress(13,3,5))!="none"||pending.Wins!=1)throw new Exception("Repeated Continue counted the same battle twice");
        pending.BeginBattle(13,3);pending.Settle(true);
        if(pending.Wins!=2||!pending.Completed.SequenceEqual(new[]{11,12}))throw new Exception("Recovered campaign could not actually continue");
        var lost=Pending();if(lost.Reconcile(Progress(13,2,4))!="lost"||lost.Losses!=1)throw new Exception("Life loss did not recover settlement");
        lost.BeginBattle(13,2);lost.Settle(true);
        foreach(bool oldFormat in new[]{false,true}){
            var unknown=Pending();if(oldFormat){unknown.PendingSince=0;unknown.PendingLife=-1;unknown.PendingSeason=0;}
            if(unknown.Reconcile(Progress(13,2,5))!="current_progress"||unknown.Wins!=0||unknown.Losses!=0||unknown.UnconfirmedBattles!=1)throw new Exception("Idle server progress remained locked or fabricated an outcome");
            unknown.BeginBattle(13,2);unknown.Settle(true);
        }
        var final=Pending(10);
        var receipt=Reply("soul.IbStageEnd",new JsonObject{["isDungeonCleared"]=1,["nextStageId"]=0,["life"]=5});
        if(final.Reconcile(Progress(0,0,0),receipt)!="won"||final.Target!=14)throw new Exception("Confirmed final victory did not continue to next dungeon");
        var oldReceipt=Pending(10);receipt["AtUtcTicks"]=oldReceipt.PendingSince-1;
        if(oldReceipt.Reconcile(Progress(0,0,0),receipt)!="current_progress"||oldReceipt.Target!=13||oldReceipt.Wins!=0)throw new Exception("Historical clears or old receipt invented a new clear");
        oldReceipt.BeginBattle(13,1);oldReceipt.Settle(true);
        var empty=Pending();
        if(empty.Reconcile(Reply("soul.IbMainInfo",new JsonObject{["regularSeason"]=3}))!="current_progress"||empty.PendingDungeon!=0)
            throw new Exception("Native no-active-dungeon response was treated as missing evidence");
        foreach(var response in new[]{Reply("soul.IbMainInfo",new JsonObject{["regularSeason"]=4,["playInfo"]=new JsonObject()})}){
            var run=Pending();bool rejected=false;
            try{run.Reconcile(response);}catch(InvalidDataException){rejected=true;}
            if(!rejected||run.PendingDungeon!=13)throw new Exception("Different dungeon/season lost the pending record");
            if(run.Reconcile(Progress(13,3,5))!="won")throw new Exception("Restored context remained permanently blocked");
        }
        SoulResume.CheckIdentity(true,"account","player",null,null);
        SoulResume.CheckIdentity(true,"account","player","","");
        SoulResume.CheckIdentity(true,"account","player","account","player");
        foreach(var changed in new[]{(false,"account","player"),(true,"other","player"),(true,"account","other")}){
            bool rejected=false;try{SoulResume.CheckIdentity(changed.Item1,"account","player",changed.Item2,changed.Item3);}catch(InvalidOperationException){rejected=true;}
            if(!rejected)throw new Exception("Changed process or account was accepted");
        }
        if(!SoulResume.Transient(new StageHostException("adapter","gap"){ObservationTimedOut=true})
            ||SoulResume.Transient(new DailyStepException("pending","uncertain purchase",true))
            ||SoulResume.Transient(new StageHostException("identity","different account")))throw new Exception("Recovery widened into an uncertain consuming action or identity change");
        Console.WriteLine("Soul reconnect checks passed: persisted continuation, repeated recovery, active/idle battle, old receipts and identity boundaries");
    }
    static void ManualProgress()
    {
        var state=JsonNode.Parse("""{"progress":{"RegularSeason":3,"SeasonDungeons":[11,12,13,14,15],"WaveCounts":[5,10,10,15,15],"ClearedDungeons":[11,12]},"play":{"season":3,"dungeonId":13,"stageId":4,"life":3,"coin":17},"inventory":[{"inventoryIndex":321}],"deck":[{"inventoryIndex":321,"position":7}],"shop":[{"slot":0,"id":10000}]}""")!.AsObject();
        JsonObject Reply(int active,int wave,int[] clears,bool inBattle=false,int season=3){
            var body=new JsonObject{["regularSeason"]=season,["clearedDungeonId"]=new JsonArray(clears.Select(i=>(JsonNode)JsonValue.Create(i)!).ToArray()),["playInfo"]=new JsonObject{["season"]=season,["dungeonId"]=active,["stageId"]=wave,["life"]=3,["coin"]=17,["isStageEnter"]=inBattle}};
            return new JsonObject{["Role"]="soul.IbMainInfo",["Kind"]="response",["Accepted"]=true,["AtUtcTicks"]=DateTime.UtcNow.Ticks,["Values"]=new JsonArray(new JsonObject{["Json"]=body.ToJsonString()})};
        }
        var original=state.ToJsonString();
        var fresh=new SoulCampaign();var run=fresh.ResumeFromGame(state,Reply(13,4,[11,12]),"account","player");
        if(run.Target!=13||!run.Completed.SequenceEqual(new[]{11,12})||run.Wins!=0||fresh.Dungeons.Length!=0||state.ToJsonString()!=original)
            throw new Exception("Manual resume replayed cleared chapters, fabricated wins, or changed resources/layout");
        run.BeginBattle(13,4,state);run.Settle(true);
        run=System.Text.Json.JsonSerializer.Deserialize<SoulCampaign>(System.Text.Json.JsonSerializer.Serialize(run))!;
        run=run.ResumeFromGame(state,Reply(13,5,[11,12]),"account","player");
        if(run.Target!=13||run.Wins!=1||run.PendingDungeon!=0)throw new Exception("Repeated/manual resume lost saved progress");
        run.BeginBattle(13,5,state);run.Settle(true);
        var idle=fresh.ResumeFromGame(state,Reply(0,0,[11,12]),"account","player");
        if(idle.Target!=13)throw new Exception("Between-dungeon manual progress restarted chapter one");
        var complete=fresh.ResumeFromGame(state,Reply(0,0,[11,12,13,14,15]),"account","player");
        if(complete.Target!=0||complete.Status!="complete")throw new Exception("An already completed season was replayed by Resume");
        var redoing=fresh.ResumeFromGame(state,Reply(13,4,[11,12,13,14,15]),"account","player");
        if(redoing.Target!=13||redoing.Completed.Contains(13)||redoing.Completed.Count!=4)throw new Exception("Current manual rechallenge was skipped");
        var unordered=fresh.ResumeFromGame(state,Reply(14,6,[13,15]),"account","player");
        if(unordered.Target!=14||!unordered.Completed.SequenceEqual(new[]{13,15}))throw new Exception("Current dungeon lost priority to an earlier incomplete chapter");
        unordered.BeginBattle(14,15,state);unordered.Settle(true);
        if(unordered.Target!=11)throw new Exception("Other unfinished dungeons were lost after the active dungeon");
        var active=fresh.ResumeFromGame(state,Reply(13,4,[11,12],true),"account","player");
        if(active.PendingDungeon!=13||active.PendingWave!=4||active.Battles!=0)throw new Exception("Ongoing manual battle was treated as a new dispatch");
        active=active.ResumeFromGame(state,Reply(13,5,[11,12]),"account","player");
        if(active.PendingDungeon!=0||active.Wins!=1)throw new Exception("Manual battle could not settle after observing the result");
        active.BeginBattle(13,5,state);
        active=active.ResumeFromGame(state,Reply(14,6,[11,12,13]),"account","player");
        if(active.Target!=14||active.PendingDungeon!=0||active.UnconfirmedBattles!=1)throw new Exception("Stale local attempt locked newer manual game progress");
        var nextSeason=state.DeepClone().AsObject();nextSeason["progress"]!["RegularSeason"]=4;
        active=active.ResumeFromGame(nextSeason,Reply(0,0,[],season:4),"account","player");
        if(active.Target!=11||active.Completed.Count!=0||active.Wins!=0||active.Season!=4)throw new Exception("Old season history blocked or skipped the new season");
        bool wrongAccount=false;try{run.ResumeFromGame(state,Reply(13,5,[11,12]),"other","player");}catch(InvalidDataException){wrongAccount=true;}
        if(!wrongAccount)throw new Exception("Manual resume crossed an account boundary");
        Console.WriteLine("Manual Soul progress checks passed: no tool history, saved/manual progress, active battle, nonsequential clears, current rechallenge, all-clear and new season");
    }
    static void SaleSynchronization()
    {
        var action=JsonNode.Parse("""{"operation":"soul_sell","ui":"InventoryBattleStageUI","items":[3,11,3,20,21065461,10057,0,4],"value":0}""")!.AsObject();
        var receipt=JsonNode.Parse("""{"Accepted":true,"Role":"soul.IbItemSell","Values":[{"Json":"{\"invenIndex\":21065461,\"sellPrice\":4,\"currentCoin\":24}"}]}""")!;
        var sync=SoulEvidence.SaleSynchronization(action,receipt);
        if(sync["operation"]!.ToString()!="soul_sale_sync"||sync["ui"]!.ToString()!="InventoryBattleStageUI"
            ||!JsonNode.DeepEquals(sync["items"],JsonNode.Parse("[3,11,3,20,21065461,24]"))
            ||action["operation"]!.ToString()!="soul_sell"||action["items"]!.AsArray().Count!=8)
            throw new Exception("Sale receipt must produce a local item/run-bound wallet synchronization without changing the sale");
        // Preserve 64-bit inventory identity and trust actual positive refunds,
        // including the server's half-up rounding rather than planner estimates.
        var wide=action.DeepClone().AsObject();wide["items"]![4]=9007199254740993L;
        var rounded=receipt.DeepClone();rounded["Values"]![0]!["Json"]="{\"invenIndex\":9007199254740993,\"sellPrice\":5,\"currentCoin\":25}";
        var large=SoulEvidence.SaleSynchronization(wide,rounded);
        if(large["items"]![4]!.GetValue<long>()!=9007199254740993L||large["items"]![5]!.GetValue<long>()!=25)throw new Exception("Sale synchronization lost native item precision or actual refund");
        foreach(var mutate in new Action<JsonNode>[] {
            r=>r["Accepted"]=false,
            r=>r["Role"]="soul.IbShopBuy",
            r=>r["Values"]![0]!["Json"]="{\"invenIndex\":999,\"sellPrice\":4,\"currentCoin\":24}",
            r=>r["Values"]![0]!["Json"]="{\"invenIndex\":21065461,\"sellPrice\":4,\"currentCoin\":25}",
            r=>r["Values"]![0]!["Json"]="{\"invenIndex\":21065461,\"sellPrice\":-1,\"currentCoin\":19}",
            r=>r["Values"]![0]!["Json"]="{\"invenIndex\":21065461,\"sellPrice\":2147483628,\"currentCoin\":2147483648}" })
        {
            var changed=receipt.DeepClone();mutate(changed);bool rejected=false;
            try{SoulEvidence.SaleSynchronization(action,changed);}catch(InvalidDataException){rejected=true;}
            if(!rejected)throw new Exception("Unaccepted, wrong-item or inconsistent sale cannot update the cached wallet");
        }
    }
    static void Storage(string folder)
    {
        Directory.CreateDirectory(folder);
        JsonObject Row(string name,string json)=>new() { ["Id"]="soul."+name,["Error"]="",["Values"]=new JsonArray(new JsonObject { ["Json"]=json }) };
        var observed=new JsonObject { ["AtUtcTicks"]=1L,["Frame"]=new JsonObject { ["Scene"]="test",["Surfaces"]=new JsonArray(new string('x',16000)) },["Readings"]=new JsonArray(
            Row("IbItemDBInfo","""[{"invenIndex":"9007199254740993","itemId":1}]"""),
            Row("IbDeckDBInfo","""[{"invenIndex":"9007199254740993","rotationCount":2,"position":3}]"""),
            Row("IbPlayDBInfo","""{"season":1,"dungeonId":2,"stageId":3,"coin":24,"life":5}"""),
            Row("IbShopItemDBInfo","[]"),Row("Capabilities","{}"),Row("Progress","{}")) };
        byte[] config=System.Text.Encoding.UTF8.GetBytes("{\"Mvid\":\"fixture\",\"Reads\":[]}");
        for(int i=0;i<20;i++){
            SoulEvidence.StoreConfiguration(folder,config).GetAwaiter().GetResult();
            observed["AtUtcTicks"]=i+1;
            SoulEvidence.StoreObservation(observed,folder,"poll-"+i,false).GetAwaiter().GetResult();
        }
        if(Directory.GetFiles(folder).Length!=3)throw new Exception("Routine observations accumulated per-poll files or duplicate configuration");
        var current=JsonNode.Parse(File.ReadAllText(Path.Combine(folder,"live-state-latest.json")))!;
        if(current["state"]!["revision"]!.GetValue<long>()!=20||current["state"]!["inventory"]![0]!["inventoryIndex"]!.GetValue<long>()!=9007199254740993L
            ||current["state"]!["play"]!["coin"]!.GetValue<int>()!=24||current["evidence"]!.ToString()!="soul-latest.json")throw new Exception("Rolling snapshot changed planner state or evidence binding");
        long bytes=Directory.GetFiles(folder).Sum(f=>new FileInfo(f).Length);
        long legacyBytes=20*(System.Text.Encoding.UTF8.GetByteCount(observed.ToJsonString())+System.Text.Encoding.UTF8.GetByteCount(current.ToJsonString())+config.Length)
            +System.Text.Encoding.UTF8.GetByteCount(observed.ToJsonString())+System.Text.Encoding.UTF8.GetByteCount(current.ToJsonString());
        if(bytes>=legacyBytes/5)throw new Exception("Routine snapshot storage did not remove repeated observations");
        if(File.ReadAllText(Path.Combine(folder,"soul-latest.json")).Contains('\n'))throw new Exception("Machine observations unexpectedly expanded into indented JSON");
        SoulEvidence.StoreObservation(observed,folder,"action-before",true).GetAwaiter().GetResult();
        var archive=File.ReadAllText(Path.Combine(folder,"action-before-state.json"));
        observed["AtUtcTicks"]=21;SoulEvidence.StoreObservation(observed,folder,"next-poll",false).GetAwaiter().GetResult();
        if(File.ReadAllText(Path.Combine(folder,"action-before-state.json"))!=archive
            ||JsonNode.Parse(archive)!["evidence"]!.ToString()!="action-before-soul.json"
            ||!File.Exists(Path.Combine(folder,"action-before-soul.json")))throw new Exception("Action checkpoint lost its immutable original observation");
        SoulEvidence.StoreConfiguration(folder,System.Text.Encoding.UTF8.GetBytes("{\"Mvid\":\"changed\"}")).GetAwaiter().GetResult();
        if(Directory.GetFiles(folder,"observer-*.json").Length!=2)throw new Exception("Changed observer configuration overwrote the previous configuration");
        if(!SoulEvidence.StoreFailure(folder,"failed",observed)||!File.Exists(Path.Combine(folder,"failed-failed-soul.json")))throw new Exception("Failed routine read lost its complete raw observation");
        Directory.CreateDirectory(Path.Combine(folder,"blocked-failed-soul.json"));
        if(SoulEvidence.StoreFailure(folder,"blocked",observed))throw new Exception("Unwritable diagnostic destination was reported as saved");
        File.WriteAllText(Path.Combine(folder,"storage-comparison.json"),System.Text.Json.JsonSerializer.Serialize(new { polls=20,legacyBytes,bytes,ordinaryFiles=3,archivedSourcesPreserved=true }));
    }
}
