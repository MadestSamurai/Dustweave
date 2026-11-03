using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace Dustweave.Soul;

internal static class SoulSolverChecks
{
    internal static void Settings(string root)
    {
        var initial=SoulSolverSettings.Load(root);
        if(initial.WorkerLimit!=3||!initial.ChapterCandidates.SequenceEqual(new[]{3000,3000,3000,10000,3000}))
            throw new Exception("Missing Soul settings lost defaults");
        if(initial.Workers<Math.Clamp(Environment.ProcessorCount/2,1,3)||SoulSearchWork.NativeBatchLimit(3)!=0)
            throw new Exception("High performance restricted the original search or native batch concurrency");
        var value=new SoulSolverSettings{WorkerLimit=1,ChapterCandidates=[1000,3000,10000,30000,1000]};value.Save(root);
        var session=new SoulSession(root);
        if(!session.Settings.ChapterCandidates.SequenceEqual(value.ChapterCandidates)||session.Settings.WorkerLimit!=1)
            throw new Exception("Soul settings did not reload");
        var mutable=session.Settings;mutable.ChapterCandidates[0]=30000;
        if(session.Settings.ForChapter(0)!=1000)throw new Exception("A caller mutated the active solver snapshot");
        session.Configure(value with{WorkerLimit=2});
        if(session.Running||SoulSolverSettings.Load(root).WorkerLimit!=2)throw new Exception("Saving settings started gameplay or failed to persist");
        var damaged=new SoulSolverSettings{WorkerLimit=99,ChapterCandidates=[-1,30000,7]}.Normalize();
        if(damaged.WorkerLimit!=3||!damaged.ChapterCandidates.SequenceEqual(new[]{3000,30000,3000,10000,3000}))
            throw new Exception("Invalid Soul settings discarded valid chapter choices or did not repair missing fields");
        Directory.CreateDirectory(Path.Combine(root,"tools","soul"));File.WriteAllText(Path.Combine(root,"tools","soul","settings.json"),"{broken");
        if(SoulSolverSettings.Load(root).ForChapter(3)!=10000)throw new Exception("Damaged settings blocked chapter-specific fallback");
        foreach(int workers in new[]{1,2,3}){
            int active=0,peak=0,executed=0;
            var result=SoulSearchWork.Run(3,workers,(i,token)=>{
                int current=Interlocked.Increment(ref active),prior;
                do{prior=Volatile.Read(ref peak);}while(prior<current&&Interlocked.CompareExchange(ref peak,current,prior)!=prior);
                Thread.Sleep(20);Interlocked.Increment(ref executed);Interlocked.Decrement(ref active);
                return SoulSearchWork.Share(1001,i,3);
            },CancellationToken.None,()=>{});
            if(peak>workers||active!=0||executed!=3||result.Sum()!=1001||!result.SequenceEqual(new[]{334,334,333}))
                throw new Exception("Performance limit altered work, branch order or parallelism");
        }
        using var stop=new CancellationTokenSource();int started=0;
        try{
            SoulSearchWork.Run(3,1,(i,token)=>{Interlocked.Increment(ref started);stop.Cancel();token.ThrowIfCancellationRequested();return i;},stop.Token,()=>{});
            throw new Exception("Cancelled queued solver branches completed");
        }catch(OperationCanceledException){}
        if(started!=1)throw new Exception("Cancelled search started a waiting branch");
        int running=0;
        try{
            SoulSearchWork.Run(3,1,(i,token)=>{
                Interlocked.Increment(ref running);
                try{while(!token.IsCancellationRequested)Thread.Sleep(5);token.ThrowIfCancellationRequested();}
                finally{Interlocked.Decrement(ref running);}
                return i;
            },CancellationToken.None,()=>throw new IOException("synthetic progress failure"));
            throw new Exception("Progress failure was swallowed");
        }catch(IOException error)when(error.Message=="synthetic progress failure"){}
        if(running!=0)throw new Exception("Failed progress left background computation running");
    }

    internal static void Native(string output)
    {
        Directory.CreateDirectory(output);SoulAssets.PrepareModel(Path.Combine(output,"isolated"));
        JsonArray Table(string name)=>JsonNode.Parse(File.ReadAllText(Path.Combine(SoulAssets.Tables,name+".json")))!.AsArray();
        int N(JsonNode? n,string key)=>n![key]!.GetValue<int>();
        var defaults=Table("IBDefaultTable")[0]!;
        var deck=Table("IBDeckTable").Single(n=>N(n,"id")==N(defaults,"startDeckId"))!;
        var dungeon=Table("IBDungeonTable").First(n=>N(n,"id")>=11)!;
        int group=N(dungeon,"stageGroupId");var stage=Table("IBStageTable").Where(n=>N(n,"groupId")==group).OrderBy(n=>N(n,"id")).First()!;
        var inventory=new JsonArray();var placements=new JsonArray();
        for(int i=0;i<deck["useItemId"]!.AsArray().Count;i++){
            inventory.Add(new JsonObject{["inventoryIndex"]=i+1,["id"]=deck["useItemId"]![i]!.DeepClone(),["level"]=0});
            placements.Add(new JsonObject{["inventoryIndex"]=i+1,["position"]=deck["invenPosition"]![i]!.DeepClone(),["rotation"]=deck["itemRotate"]![i]!.DeepClone()});
        }
        var offers=Table("IBItemTable").Where(n=>N(n,"itemType")==1).Take(3).Select(n=>N(n,"id")).ToArray();
        var shop=new JsonArray(offers.Select((id,i)=>(JsonNode)new JsonObject{["slot"]=i,["id"]=id,["price"]=3,["isSoldOut"]=false}).ToArray());
        var ids=inventory.Select(n=>N(n,"id")).Concat(offers).Distinct().ToArray();
        var state=new JsonObject{
            ["play"]=new JsonObject{["season"]=0,["dungeonId"]=N(dungeon,"id"),["stageId"]=N(stage,"id"),["coin"]=20,["life"]=5},
            ["inventory"]=inventory,["deck"]=placements,["shop"]=shop,
            ["upgradeCapabilities"]=new JsonArray(ids.Select(id=>(JsonNode)new JsonObject{["ItemId"]=id,["MaximumLevel"]=0}).ToArray())};
        using var kernel=new NativeBattleKernel(SoulAssets.Tables,SoulAssets.Library);
        var battle=kernel.Compile(new JsonObject{["stage"]=new JsonObject{["groupId"]=group,["id"]=N(stage,"id")}});
        var seeds=Enumerable.Range(1,97).ToArray();var original=kernel.Run(battle,seeds);
        using var cancellation=new CancellationTokenSource();kernel.Cancellation=cancellation.Token;
        foreach(int performance in new[]{1,2,3}){
            kernel.BatchLimit=SoulSearchWork.NativeBatchLimit(performance);
            var chunked=kernel.Run(battle,seeds);
            if(!MemoryMarshal.AsBytes(original.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(chunked.AsSpan())))
                throw new Exception("Performance limit changed native battle results");
        }
        var plans=new List<JsonObject>();var times=new List<double>();
        const int candidateBudget=SoulSolverSettings.DefaultCandidates;
        foreach(int workers in new[]{1,2,3}){
            var clock=Stopwatch.StartNew();using var planner=new NativeFastPlanner(state,710231,allowLiquidation:true){Cancellation=cancellation.Token};
            var plan=planner.SearchPortfolio(0,null,candidateBudget,workers);plans.Add(plan);times.Add(clock.Elapsed.TotalSeconds);
            if(N(plan,"branches")!=3||N(plan,"proposals")!=candidateBudget||plan["auditSeeds"]!.AsArray().Count!=512)
                throw new Exception("Configured work or independent validation was lost");
            File.WriteAllText(Path.Combine(output,$"workers-{workers}.json"),plan.ToJsonString());
        }
        foreach(var plan in plans.Skip(1))foreach(string key in new[]{"recommendation","auditSeeds","proposals","evaluated","ownedAlternative"})
            if(!JsonNode.DeepEquals(plans[0][key],plan[key]))throw new Exception("Performance limit changed search results: "+key);
        using var stopSearch=new CancellationTokenSource();var stopClock=Stopwatch.StartNew();
        using(var planner=new NativeFastPlanner(state,710231,allowLiquidation:true){Cancellation=stopSearch.Token}){
            planner.Progress=(_,_,_)=>stopSearch.Cancel();
            try{planner.SearchPortfolio(0,null,30000,3);throw new Exception("Active search ignored pause");}
            catch(OperationCanceledException){}
        }
        cancellation.Cancel();
        try{kernel.Run(battle,seeds);throw new Exception("Cancelled native batch ran");}catch(OperationCanceledException){}
        DailyJson.Write(Path.Combine(output,"result.json"),new{status="passed",branches=3,candidateBudget,proposals=N(plans[0],"proposals"),auditSeeds=512,workerSeconds=times,workerResultsIdentical=true,nativeChunkParity=true,activeSearchCancellation=true,cancellationSeconds=stopClock.Elapsed.TotalSeconds,realGameTouched=false});
    }
}
