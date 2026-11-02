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
}
