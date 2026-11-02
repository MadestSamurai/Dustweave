using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;
using P = Dustweave.Soul.NativeBattleKernel.Placement;
namespace Dustweave.Soul;

internal sealed partial class NativeFastPlanner
{
    int capacityProposals;
    double capacitySeconds;
    JsonObject rosterAudit=new();
    Candidate CompleteRoster(Candidate initial,int[] screenSeeds,int[] auditSeeds)
    {
        var timer=Stopwatch.StartNew();var best=initial;int considered=0,audited=0;
        for(int pass=0;pass<3;pass++){
            Cancellation.ThrowIfCancellationRequested();
            var baseline=Evaluate(best.Board,screenSeeds,false)!;
            var options=new List<Candidate>();var used=best.Board.Select(p=>p.Inventory).ToHashSet();
            foreach(var board in FillRoom(best.Board).DistinctBy(Key)){
                Cancellation.ThrowIfCancellationRequested();
                if(considered>=96)break;
                // Do not create purchases, upgrades or replace a planned sale
                // with an unfunded unit. Compare acquired units at their real
                // level, on the same floor and at no additional net cost.
                if(board.Where(p=>!used.Contains(p.Inventory)).Any(p=>stock[p.Inventory].Slot>=0||p.Level!=stock[p.Inventory].Minimum)||NetCost(board)>best.Cost)continue;
                considered++;var value=Evaluate(board,screenSeeds,false);
                if(value is not null&&value.Rank.CompareTo(baseline.Rank)>0)options.Add(value);
            }
            var next=best;
            foreach(var candidate in options.OrderByDescending(c=>c.Rank).Take(4)){
                Cancellation.ThrowIfCancellationRequested();
                var value=Evaluate(candidate.Board,auditSeeds,false)!;audited++;
                // The final shared seed set is the same as the selected plan.
                // Empty-cell synergies, stamina and enemy on-hit effects can
                // make adding a unit harmful; never force a filled board.
                if(value.Rank.CompareTo(next.Rank)>0)next=value;
            }
            if(ReferenceEquals(next,best))break;
            best=next;if(considered>=96)break;
        }
        rosterAudit=new JsonObject{["considered"]=considered,["audited"]=audited,["beforeUnits"]=initial.Board.Count(p=>stock[p.Inventory].Kind==1),["afterUnits"]=best.Board.Count(p=>stock[p.Inventory].Kind==1),["before"]=Metric(initial),["after"]=Metric(best),["seconds"]=timer.Elapsed.TotalSeconds};
        return best;
    }
    IEnumerable<List<P>> FillRoom(List<P> basis)
    {
        ulong coverage=0,occupied=0;
        foreach(var p in basis.Where(p=>stock[p.Inventory].Kind==0))coverage|=ShapeOf(p).Mask;
        foreach(var p in basis.Where(p=>stock[p.Inventory].Kind==1))occupied|=ShapeOf(p).Mask;
        var members=basis.Where(p=>stock[p.Inventory].Kind==1).Select(p=>(P:p,S:ShapeOf(p))).ToArray();
        var additions=new List<(P P,Shape S,double Links)>();
        // A spatial expansion must explicitly try the paid-for bench. The
        // ordinary random roster generator often selects a new shop anchor.
        foreach(var s in stock.Values.Where(s=>s.Kind==1&&basis.All(p=>p.Inventory!=s.Index)).OrderBy(s=>s.Slot<0?0:1).ThenByDescending(s=>s.Minimum)){
            foreach(int level in new[]{s.Minimum,s.Maximum}.Distinct()){
                var unit=new P(s.Index,s.Id,0,0,level);
                if(NetCost([..basis,unit])>budget)continue;
                foreach(var shape in Shapes(s,level).Where(o=>(o.Mask&coverage)==o.Mask&&(o.Mask&occupied)==0)
                    .OrderByDescending(o=>members.Sum(m=>Links(o,m.S,m.P.Id)+Links(m.S,o,s.Id))).Take(3)){
                    var placed=unit with{Position=shape.Position,Rotation=shape.Rotation};
                    double links=members.Sum(m=>Links(shape,m.S,m.P.Id)+Links(m.S,shape,s.Id));
                    additions.Add((placed,shape,links));
                }
            }
        }
        // Combine compatible additions as well as testing each one. A floor
        // can be worthwhile only when two complementary bench roles enter.
        foreach(bool reverse in new[]{false,true}){
            var result=new List<P>(basis);ulong used=occupied;
            var ordered=additions.OrderBy(a=>stock[a.P.Inventory].Slot<0?0:1).ThenByDescending(a=>a.Links).ThenBy(a=>reverse?-a.P.Id:a.P.Id).ToArray();
            foreach(var a in ordered){
                if((a.S.Mask&used)!=0||result.Any(p=>p.Inventory==a.P.Inventory)||NetCost([..result,a.P])>budget)continue;
                result.Add(a.P);used|=a.S.Mask;yield return new(result);
            }
        }
        foreach(var a in additions)yield return [..basis,a.P];
    }
    IEnumerable<List<P>> FloorVariants(Candidate parent)
    {
        yield return parent.Board;
        ulong coverage=0;foreach(var p in parent.Board.Where(p=>stock[p.Inventory].Kind==0))coverage|=ShapeOf(p).Mask;
        foreach(var floor in stock.Values.Where(s=>s.Kind==0&&parent.Board.All(p=>p.Inventory!=s.Index)).OrderBy(s=>s.Slot<0?0:1).ThenBy(s=>s.Price)){
            foreach(var shape in Shapes(floor,0).Where(s=>(s.Mask&coverage)==0)
                .OrderByDescending(s=>BitOperations.PopCount(s.Halo&coverage)).ThenBy(s=>Math.Abs(s.Position%7-3)+Math.Abs(s.Position/7-3)).Take(3)){
                List<P> board=[..parent.Board,new(floor.Index,floor.Id,shape.Position,shape.Rotation,0)];
                if(NetCost(board)<=budget)yield return board;
            }
        }
    }
    IEnumerable<List<P>> CapacityBoards(List<P> board)
    {
        yield return board;
        // Repacking removes the incumbent geometry as a hidden constraint.
        // These trials add no new shop units: use the paid-for roster first.
        var parent=new Candidate(board,0,1,0,0,NetCost(board));
        for(int i=0;i<3;i++)yield return Generate(parent,preferOwned:true);
        foreach(var filled in FillRoom(board))yield return filled;
    }
    Candidate[] ExploreCapacity(Candidate[] parents,int[] seeds,bool fixedWork)
    {
        var timer=Stopwatch.StartNew();var found=new Dictionary<string,Candidate>();var seen=new HashSet<string>();
        bool Within(){Cancellation.ThrowIfCancellationRequested();return capacityProposals<384&&timer.Elapsed.TotalSeconds<(fixedWork?15:3);}
        for(int depth=0;depth<2&&Within();depth++){
            var variants=parents.SelectMany(FloorVariants).DistinctBy(Key).ToArray();
            var streams=variants.Select(board=>CapacityBoards(board).GetEnumerator()).ToList();
            try{
                // Round-robin prevents a large bench on one shape from using
                // the whole budget before other offered floors get a trial.
                int limit=(depth+1)*192;
                while(streams.Count>0&&Within()&&capacityProposals<limit){
                    for(int i=streams.Count-1;i>=0&&Within()&&capacityProposals<limit;i--){
                        if(!streams[i].MoveNext()){streams[i].Dispose();streams.RemoveAt(i);continue;}
                        var board=streams[i].Current;string key=Key(board);if(!seen.Add(key))continue;
                        capacityProposals++;var value=Evaluate(board,seeds,false);if(value is not null)found[key]=value;
                    }
                }
            }finally{foreach(var stream in streams)stream.Dispose();}
            parents=found.Values.OrderByDescending(c=>c.Rank).Take(4).ToArray();
        }
        capacitySeconds=timer.Elapsed.TotalSeconds;
        return found.Values.OrderByDescending(c=>c.Rank).Take(3)
            .Concat(found.Values.Where(c=>c.Rate>=.83).OrderBy(c=>c.Cost).ThenByDescending(c=>c.Rank).Take(3))
            .DistinctBy(c=>Key(c.Board)).ToArray();
    }
}
