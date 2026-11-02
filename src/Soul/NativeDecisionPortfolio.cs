using System.Diagnostics;
using System.Text.Json.Nodes;
using P = Dustweave.Soul.NativeBattleKernel.Placement;
namespace Dustweave.Soul;

internal sealed partial class NativeFastPlanner
{
    internal JsonObject SearchPortfolio(double seconds,string? warm=null)
    {
        var timer=Stopwatch.StartNew();int workers=Math.Clamp(Environment.ProcessorCount/2,1,3);
        var progress=new (string Phase,int Count,double Time)[workers];object gate=new();
        var tasks=Enumerable.Range(0,workers).Select(i=>Task.Run(()=>{
            bool established=workers>=3&&i==1;
            using var branch=new NativeFastPlanner(state,established?710231:710231+i*104729,allowLiquidation:liquidate,preserveRouteGeometry:i==0);
            branch.PreferExpansion=i==workers-1;
            branch.Cancellation=Cancellation;
            branch.Progress=(phase,count,time)=>{lock(gate)progress[i]=(phase,count,time);};
            // Preserve the established randomized search as one independent
            // candidate source. Its speculative reserve append is disabled;
            // only a complete simulated layout competes in the common audit.
            return established?branch.Search(seconds*.45,warm,reserveGrowth:false):branch.SearchDecision(seconds*.8,warm);
        },Cancellation)).ToArray();
        // Marshal progress on the calling thread; the session/evidence writer
        // is not called concurrently by worker kernels.
        var completion=Task.WhenAll(tasks);
        while(!completion.IsCompleted){
            Thread.Sleep(200);
            lock(gate)Progress?.Invoke(progress.Any(p=>p.Phase=="combinations")?"combinations":progress.Any(p=>p.Phase=="growth")?"growth":"verify",progress.Sum(p=>p.Count),timer.Elapsed.TotalSeconds);
        }
        completion.GetAwaiter().GetResult();Cancellation.ThrowIfCancellationRequested();
        var plans=tasks.Select(t=>t.GetAwaiter().GetResult()).ToArray();
        var seeds=new Random(752491);var audit=Enumerable.Range(0,512).Select(_=>seeds.Next(1,int.MaxValue)).ToArray();
        var candidates=new List<(JsonObject Plan,Candidate C)>();Candidate? acquired=null;
        foreach(var plan in plans){
            Cancellation.ThrowIfCancellationRequested();Progress?.Invoke("verify",plans.Sum(p=>N(p,"evaluated")),timer.Elapsed.TotalSeconds);
            var board=plan["recommendation"]!["layout"]!.AsArray().Select(n=>new P(N(n,"inventoryIndex"),N(n,"itemId"),N(n,"position",-1),N(n,"rotation"),N(n,"level"),N(n,"targetUnitInventoryIndex",-1),N(n,"targetSlotIndex",-1))).ToList();
            var c=Evaluate(board,audit,false);if(c is not null)candidates.Add((plan,c));
            if(plan["ownedAlternative"] is null)continue;
            var owned=plan["ownedAlternative"]!["layout"]!.AsArray().Select(n=>new P(N(n,"inventoryIndex"),N(n,"itemId"),N(n,"position",-1),N(n,"rotation"),N(n,"level"),N(n,"targetUnitInventoryIndex",-1),N(n,"targetSlotIndex",-1))).ToList();
            if(owned.Any(p=>stock[p.Inventory].Slot>=0||p.Level!=stock[p.Inventory].Minimum))throw new InvalidDataException("Owned alternative contains an unacquired improvement");
            var current=Evaluate(owned,audit,false);if(current is not null&&(acquired is null||current.Rank.CompareTo(acquired.Rank)>0))acquired=current;
        }
        if(candidates.Count==0)throw new InvalidDataException("All decision branches failed validation");
        if(acquired is not null){
            // The acquired-only frontier competes as a real zero-cost plan,
            // not merely a diagnostic. A short later search must not forget
            // a stronger already-owned board and pay for a weaker one.
            var ownedPlan=candidates[0].Plan.DeepClone().AsObject();
            ownedPlan["recommendation"]=new JsonObject{["cost"]=0,["coinAfter"]=budget,["transactions"]=0,["path"]=new JsonArray(),
                ["layout"]=new JsonArray(acquired.Board.Select(p=>(JsonNode)PlacementJson(p)).ToArray()),
                ["deployedUnits"]=acquired.Board.Count(p=>stock[p.Inventory].Kind==1),["battle"]=Metric(acquired),["continuationProbe"]=new JsonArray()};
            candidates.Add((ownedPlan,acquired));
        }
        var best=candidates.MaxBy(c=>c.C.Rank);
        // Roster growth can only compete among equivalently reliable current
        // plans. Common 512-seed validation prevents branch screening bias.
        if(best.C.Rate>=.95){
            var near=candidates.Where(c=>c.C.Rate>=best.C.Rate-.015).ToArray();
            if(near.Length>1&&timer.Elapsed.TotalSeconds<seconds)
                best=near.OrderByDescending(c=>ContinuationRank(GrowthPotential(c.C),c.C.Rate,c.C.Cost)).ThenByDescending(c=>c.C.Rank).First();
        }
        var chosen=best.Plan.DeepClone().AsObject();
        chosen["decisionVersion"]=3;
        chosen["recommendation"]!["battle"]=Metric(best.C);
        if(acquired is not null)chosen["ownedAlternative"]=new JsonObject{["layout"]=new JsonArray(acquired.Board.Select(p=>(JsonNode)PlacementJson(p)).ToArray()),["battle"]=Metric(acquired)};
        int remaining=Load("IBStageTable").Count(n=>N(n,"groupId")==group&&N(n,"id")>=wave);
        var risk=NativeSearchRisk.Assess(best.C.Wins,best.C.Trials,N(state["play"],"life"),remaining);
        chosen["riskBudget"]=risk;chosen["status"]=risk["ready"]!.GetValue<bool>()?"ready":"search_required";
        chosen["auditSeeds"]=new JsonArray(audit.Select(s=>(JsonNode)JsonValue.Create(s)!).ToArray());
        chosen["decisionSeconds"]=timer.Elapsed.TotalSeconds;chosen["budgetSeconds"]=seconds;
        chosen["branches"]=workers;chosen["evaluated"]=plans.Sum(p=>N(p,"evaluated"));
        chosen["branchEvidence"]=new JsonArray(candidates.Select(p=>(JsonNode)new JsonObject{["cost"]=p.C.Cost,["battle"]=Metric(p.C)}).ToArray());
        return chosen;
    }
}
