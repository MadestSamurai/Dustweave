using System.Diagnostics;
using System.Text.Json.Nodes;
using P = Dustweave.Soul.NativeBattleKernel.Placement;
namespace Dustweave.Soul;

internal sealed partial class NativeFastPlanner
{
    internal Action<string,int,double>? Progress { get; set; }
    internal JsonObject SearchDecision(double seconds,string? warm=null)
    {
        var watch=Stopwatch.StartNew();double notified=-1;int proposals=0,duplicates=0;
        var fixedRng=new Random(684517);int[] Seeds(int n)=>Enumerable.Range(0,n).Select(_=>fixedRng.Next(1,int.MaxValue)).ToArray();
        var screen=Seeds(16);var selection=Seeds(64);var audit=Seeds(512);var futureSeeds=Seeds(16);
        bool Within(double fraction=1){Cancellation.ThrowIfCancellationRequested();return watch.Elapsed.TotalSeconds<seconds*fraction;}
        void Notify(string phase){if(watch.Elapsed.TotalSeconds-notified<.4)return;notified=watch.Elapsed.TotalSeconds;Progress?.Invoke(phase,evaluated,watch.Elapsed.TotalSeconds);}
        var seen=new HashSet<string>();Candidate? acquired=null;
        Candidate? Consider(List<P> board){
            proposals++;board=BindEquipment(board);string key=Key(board);
            if(!seen.Add(key)){duplicates++;return null;}var c=Evaluate(board,screen,false);
            if(c is not null&&board.All(p=>stock[p.Inventory].Slot<0&&p.Level==stock[p.Inventory].Minimum)&&(acquired is null||c.Rank.CompareTo(acquired.Rank)>0))acquired=c;
            OfferDecision(c);return c;
        }
        Notify("combinations");var initial=Consider(Initial());
        if(warm is not null&&File.Exists(warm)){
            var data=JsonNode.Parse(File.ReadAllText(warm))!;
            var rows=data["recommendation"]!["layout"]!.AsArray();
            // Inventory indices for shop offers are synthetic. Reuse only
            // exact current offer identities, never an item from an old shop.
            var board=rows.Select(n=>new P(N(n,"inventoryIndex"),N(n,"itemId"),N(n,"position",-1),N(n,"rotation"),N(n,"level"),N(n,"targetUnitInventoryIndex",-1),N(n,"targetSlotIndex",-1))).ToList();
            if(board.All(p=>stock.TryGetValue(p.Inventory,out var s)&&s.Id==p.Id&&p.Level>=s.Minimum&&p.Level<=s.Maximum))Consider(board);
        }
        foreach(var board in CoreSeeds()){if(!Within(.08))break;Consider(board);}
        foreach(var board in RoleSeeds()){if(!Within(.14))break;Consider(board);}
        var expanded=new HashSet<string>();var streams=new Queue<IEnumerator<List<P>>>();int step=0;
        try{
            while(Within(.56)){
                Notify("combinations");
                if(step++%32==0){
                    foreach(var candidate in elite.OrderByDescending(GrowthRank).Take(4).Concat(elite.OrderByDescending(c=>c.Rank).Take(2)).DistinctBy(c=>Key(c.Board)))
                        if(expanded.Add(Key(candidate.Board)))streams.Enqueue(DecisionActions(candidate).GetEnumerator());
                    while(streams.Count>24)streams.Dequeue().Dispose();
                }
                if(streams.Count>0&&step%3!=0){
                    var stream=streams.Dequeue();if(stream.MoveNext()){Consider(stream.Current);streams.Enqueue(stream);}else stream.Dispose();
                }else{
                    if(step%30==0){floors.Add(MakeFloors());if(floors.Count>24)floors.RemoveAt(0);}
                    var parent=elite.Count>0&&rng.NextDouble()<.55?elite[rng.Next(elite.Count)]:null;
                    Consider(Generate(parent));
                }
            }
        }finally{foreach(var stream in streams)stream.Dispose();}
        Notify("verify");
        var selected=new List<Candidate>();
        foreach(var c in elite.OrderByDescending(c=>c.Rank).Take(4).Concat(elite.OrderByDescending(GrowthRank).Take(6)).Concat(initial is null?[]:new[]{initial}).DistinctBy(c=>Key(c.Board))){
            if(selected.Count>0&&!Within(.68))break;var checkedCandidate=Evaluate(c.Board,selection,false);if(checkedCandidate is not null)selected.Add(checkedCandidate);
        }
        if(selected.Count==0)throw new InvalidDataException("No legal native candidate");
        int remaining=Load("IBStageTable").Count(n=>N(n,"groupId")==group&&N(n,"id")>=wave),lives=N(state["play"],"life");
        bool Ready(Candidate c)=>NativeSearchRisk.Assess(c.Wins,c.Trials,lives,remaining)["ready"]!.GetValue<bool>();
        // Audit the actual finalists before ranking their continuation. Keep
        // one checked incumbent even if a slower machine exhausts its budget.
        var audited=new List<Candidate>();
        foreach(var c in selected.OrderByDescending(c=>c.Rank).Take(4)){
            if(audited.Count>0&&!Within(.80))break;
            var count=Within(.67)?256:128;var check=Evaluate(c.Board,audit.Take(count).ToArray(),false);if(check is not null)audited.Add(check);
        }
        var best=audited.MaxBy(c=>c.Rank)!;JsonArray continuation=new();
        var forecast=new List<(Candidate C,double Clear,double Margin,double Time,JsonArray Evidence)>();
        Notify("growth");
        foreach(var c in audited.Where(c=>Ready(c)&&c.Rate>=best.Rate-.02).OrderBy(c=>c.Cost)){
            if(!Within(.88))break;
            var f=Continuation(c,futureSeeds,()=>Within(.92));
            if(f.Evidence.Count==Math.Min(3,remaining-1))forecast.Add((c,f.Clear,f.Margin,f.Seconds,f.Evidence));
        }
        if(forecast.Count>0){
            var winner=forecast.OrderByDescending(f=>f.Clear).ThenByDescending(f=>f.Margin).ThenBy(f=>f.C.Cost).ThenBy(f=>f.Time).First();
            best=winner.C;continuation=winner.Evidence;
        }
        Notify("verify");
        if(Within(.94))best=Evaluate(best.Board,audit,false)!;
        if(acquired is not null)acquired=Evaluate(acquired.Board,audit,false);
        // No unverified reserve purchase is appended after selection. Every
        // purchased item in this path has a place in the evaluated layout.
        var path=new JsonArray();
        foreach(var sale in Sales(best.Board))path.Add(new JsonObject{["kind"]="sell",["identity"]=$"sell:{sale.Index}",["inventoryIndex"]=sale.Index,["itemId"]=sale.Id,["level"]=sale.Minimum,["price"]=-Refund(sale),["cost"]=-Refund(sale)});
        foreach(var p in best.Board){
            var s=stock[p.Inventory];if(s.Slot>=0)path.Add(new JsonObject{["kind"]="buy",["identity"]=$"shop:{s.Slot}",["slot"]=s.Slot,["itemId"]=s.Id,["price"]=s.Price,["cost"]=s.Price,["syntheticInventoryIndex"]=s.Index});
            for(int level=s.Minimum;level<p.Level;level++){
                int price=N((s.Kind==1?units:equipment)[(s.Id,level+2)],"upgradePrice");
                path.Add(new JsonObject{["kind"]="upgrade",["identity"]=$"upgrade:{s.Index}:{level+1}",["inventoryIndex"]=s.Index,["itemId"]=s.Id,["fromLevel"]=level,["toLevel"]=level+1,["price"]=price,["cost"]=price});
            }
        }
        var initialState=new JsonObject{["season"]=state["play"]!["season"]!.DeepClone(),["dungeonId"]=state["play"]!["dungeonId"]!.DeepClone(),["stageId"]=wave,["coin"]=budget,
            ["inventory"]=new JsonArray(state["inventory"]!.AsArray().Select(n=>(JsonNode)new JsonObject{["index"]=N(n,"inventoryIndex"),["id"]=N(n,"id"),["level"]=N(n,"level")}).ToArray()),
            ["shop"]=new JsonArray(state["shop"]!.AsArray().Select(n=>(JsonNode)new JsonObject{["slot"]=N(n,"slot"),["id"]=N(n,"id"),["price"]=N(n,"price"),["soldOut"]=n?["isSoldOut"]?.GetValue<bool>()??false}).ToArray())};
        return new JsonObject{["status"]=Ready(best)?"ready":"search_required",["solver"]="dustweave-soul",["decisionVersion"]=3,["decisionSeconds"]=watch.Elapsed.TotalSeconds,["budgetSeconds"]=seconds,
            ["proposals"]=proposals,["evaluated"]=evaluated,["cacheHits"]=duplicates,["invalid"]=invalid,["expandedStates"]=expanded.Count,["kernelSha256"]=kernel.KernelSha256,["tableSha256"]=kernel.TableSha256,
            ["auditSeeds"]=new JsonArray(audit.Take(best.Trials).Select(s=>(JsonNode)JsonValue.Create(s)!).ToArray()),["initialState"]=initialState,["riskBudget"]=NativeSearchRisk.Assess(best.Wins,best.Trials,lives,remaining),
            ["ownedAlternative"]=acquired is null?null:new JsonObject{["layout"]=new JsonArray(acquired.Board.Select(p=>(JsonNode)PlacementJson(p)).ToArray()),["battle"]=Metric(acquired)},
            ["recommendation"]=new JsonObject{["cost"]=best.Cost,["coinAfter"]=budget-best.Cost,["transactions"]=path.Count,["path"]=path,["layout"]=new JsonArray(best.Board.Select(p=>(JsonNode)PlacementJson(p)).ToArray()),["deployedUnits"]=best.Board.Count(p=>stock[p.Inventory].Kind==1),["battle"]=Metric(best),["continuationProbe"]=continuation},
            ["limits"]=new JsonArray("Bounded joint action search; not a proof of global optimality.","Continuation is conditional on acquired items and affordable upgrades; unknown shop offers are not assumed.")};
    }
}
