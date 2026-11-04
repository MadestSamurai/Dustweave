using System.Diagnostics;
using System.Text.Json.Nodes;
using P = Dustweave.Soul.NativeBattleKernel.Placement;
namespace Dustweave.Soul;

internal sealed partial class NativeFastPlanner
{
    internal JsonObject SearchPortfolio(double seconds,string? warm=null,int? candidateBudget=null,int? workerLimit=null)
    {
        var timer=Stopwatch.StartNew();
        int workers=Math.Clamp(workerLimit??Environment.ProcessorCount/2,1,Math.Min(3,Math.Max(1,Environment.ProcessorCount)));
        int branches=candidateBudget.HasValue?3:workers;
        int batchLimit=SoulSearchWork.NativeBatchLimit(workerLimit??3);
        kernel.BatchLimit=batchLimit;
        if(candidateBudget is <3)throw new ArgumentOutOfRangeException(nameof(candidateBudget));
        var progress=new (string Phase,int Count,double Time,int Proposals)[branches];object gate=new();
        var plans=SoulSearchWork.Run(branches,workers,(i,token)=>{
            bool established=branches>=3&&i==1;
            using var branch=new NativeFastPlanner(state,established?710231:710231+i*104729,allowLiquidation:liquidate,preserveRouteGeometry:i==0);
            branch.PreferExpansion=i==branches-1;
            branch.Cancellation=token;branch.kernel.BatchLimit=batchLimit;
            branch.Progress=(phase,count,time)=>{lock(gate)progress[i]=(phase,count,time,branch.CandidateProposals);};
            // Preserve the established randomized search as one independent
            // candidate source. Its speculative reserve append is disabled;
            // only a complete simulated layout competes in the common audit.
            int? share=candidateBudget.HasValue?SoulSearchWork.Share(candidateBudget.Value,i,branches):null;
            var plan=established?branch.Search(seconds*.45,warm,share,reserveGrowth:false,fixedCandidates:share.HasValue):branch.SearchDecision(seconds*.8,warm,share);
            lock(gate)progress[i]=("verify",N(plan,"evaluated"),timer.Elapsed.TotalSeconds,branch.CandidateProposals);
            return plan;
        },Cancellation,()=>{
            // Only this calling thread writes session status and evidence.
            (string Phase,int Count,double Time,int Proposals)[] snapshot;
            lock(gate)snapshot=progress.ToArray();
            CandidateProposals=snapshot.Sum(p=>p.Proposals);
            // The callback writes diagnostics. Do not hold the workers' progress
            // lock across filesystem I/O or UI notification.
            Progress?.Invoke(snapshot.Any(p=>p.Phase is null or "combinations")?"combinations":snapshot.Any(p=>p.Phase=="growth")?"growth":"verify",snapshot.Sum(p=>p.Count),timer.Elapsed.TotalSeconds);
        });
        Cancellation.ThrowIfCancellationRequested();CandidateProposals=plans.Sum(p=>N(p,"proposals"));
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
            if(near.Length>1&&(candidateBudget.HasValue||timer.Elapsed.TotalSeconds<seconds))
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
        chosen["decisionSeconds"]=timer.Elapsed.TotalSeconds;chosen["budgetSeconds"]=candidateBudget.HasValue?null:JsonValue.Create(seconds);
        chosen["candidateBudget"]=candidateBudget;chosen["proposals"]=CandidateProposals;chosen["workers"]=workers;
        chosen["branches"]=branches;chosen["evaluated"]=plans.Sum(p=>N(p,"evaluated"));
        chosen["branchEvidence"]=new JsonArray(candidates.Select(p=>(JsonNode)new JsonObject{["cost"]=p.C.Cost,["battle"]=Metric(p.C)}).ToArray());
        return chosen;
    }
}
