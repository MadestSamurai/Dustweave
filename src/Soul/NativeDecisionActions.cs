using System.Numerics;
using P = Dustweave.Soul.NativeBattleKernel.Placement;
namespace Dustweave.Soul;

internal sealed partial class NativeFastPlanner
{
    internal bool PreferExpansion { get; set; }
    List<(int Unit,int Slot,int Position)> Sockets(IReadOnlyList<P> board)
    {
        var slots=new List<(int Unit,int Slot,int Position)>();
        foreach(var p in board.Where(p=>stock[p.Inventory].Kind==1)){
            var row=units[(p.Id,p.Level+1)];var xs=A(row,"socketSlotPositionX");var ys=A(row,"socketSlotPositionY");int slot=0;
            for(int i=0;i<xs.Length;i++){
                if(xs[i]==0&&ys[i]==0)continue;
                var (x,y)=Rotate(xs[i],ys[i],p.Rotation);x+=p.Position%7;y+=p.Position/7;
                if(x>=0&&x<7&&y>=0&&y<7)slots.Add((p.Inventory,slot,y*7+x));slot++;
            }
        }
        return slots.GroupBy(s=>s.Position).Where(g=>g.Count()==1).Select(g=>g.Single()).ToList();
    }
    List<P> BindEquipment(List<P> board)
    {
        var sockets=Sockets(board);var result=new List<P>();var used=new HashSet<(int,int)>();
        foreach(var p in board){
            if(stock[p.Inventory].Kind!=2){result.Add(p);continue;}
            var found=sockets.Where(s=>p.Target>=0?s.Unit==p.Target&&s.Slot==p.Socket:s.Position==p.Position).ToArray();
            if(found.Length==1&&used.Add((found[0].Unit,found[0].Slot)))result.Add(p with{Position=-1,Rotation=0,Target=found[0].Unit,Socket=found[0].Slot});
        }
        return result;
    }
    IEnumerable<List<P>> EquipmentActions(List<P> basis)
    {
        var slots=Sockets(basis);var byInventory=basis.ToDictionary(p=>p.Inventory);
        foreach(var s in stock.Values.Where(s=>s.Kind==2).OrderBy(s=>s.Slot>=0).ThenBy(s=>s.Id)){
            var row=equipment[(s.Id,s.Minimum+1)];
            foreach(var socket in slots.OrderByDescending(k=>tags[byInventory[k.Unit].Id].Contains(N(row,"targetTagId"))).ThenBy(k=>k.Unit).ThenBy(k=>k.Slot)){
                var next=basis.Where(p=>p.Inventory!=s.Index&&!(p.Target==socket.Unit&&p.Socket==socket.Slot)).ToList();
                next.Add(new(s.Index,s.Id,-1,0,s.Minimum,socket.Unit,socket.Slot));
                if(NetCost(next)<=budget)yield return next;
            }
        }
    }
    IEnumerable<List<P>> UpgradeActions(List<P> basis)
    {
        foreach(var p in basis.Where(p=>stock[p.Inventory].Kind==1))
            for(int level=stock[p.Inventory].Minimum;level<=stock[p.Inventory].Maximum;level++){
                if(level==p.Level)continue;var next=new List<P>(basis);next[next.IndexOf(p)]=p with{Level=level};
                if(NetCost(next)<=budget)yield return BindEquipment(next);
            }
    }
    IEnumerable<List<P>> SpatialActions(List<P> basis)
    {
        ulong coverage=0;foreach(var p in basis.Where(p=>stock[p.Inventory].Kind==0))coverage|=ShapeOf(p).Mask;
        var members=basis.Where(p=>stock[p.Inventory].Kind==1).ToArray();
        foreach(var unit in members){
            ulong occupied=0;foreach(var other in members.Where(p=>p.Inventory!=unit.Inventory))occupied|=ShapeOf(other).Mask;
            var others=members.Where(p=>p.Inventory!=unit.Inventory).Select(p=>(P:p,S:ShapeOf(p))).ToArray();
            foreach(var shape in Shapes(unit).Where(o=>(o.Mask&coverage)==o.Mask&&(o.Mask&occupied)==0)
                .OrderByDescending(o=>others.Sum(m=>Links(o,m.S,m.P.Id)+Links(m.S,o,unit.Id))+.5*o.Links.Count(l=>l.Tags.Contains(999)&&(l.Cell&coverage)!=0&&(l.Cell&occupied)==0))){
                if(shape.Position==unit.Position&&shape.Rotation==unit.Rotation)continue;
                var next=new List<P>(basis);next[next.IndexOf(unit)]=unit with{Position=shape.Position,Rotation=shape.Rotation};yield return BindEquipment(next);
            }
        }
    }
    IEnumerable<List<P>> RosterActions(List<P> basis)
    {
        var members=basis.Where(p=>stock[p.Inventory].Kind==1).ToArray();
        // Whole substitutions cross the low-value remove-only intermediate.
        foreach(var removed in members.Prepend(null!)){
            var keep=removed is null?basis:basis.Where(p=>p.Inventory!=removed.Inventory&&p.Target!=removed.Inventory).ToList();
            if(removed is not null&&members.Length>1)yield return keep;
            foreach(var added in FillRoom(keep))yield return BindEquipment(added);
        }
        if(!PreferExpansion)foreach(var floor in FloorVariants(new(basis,0,1,0,0,NetCost(basis))).Skip(1))
            foreach(var filled in FillRoom(floor))yield return BindEquipment(filled);
    }
    IEnumerable<List<P>> ExpansionActions(List<P> basis)
    {
        // Expansion must not wait behind every possible roster substitution.
        // Traverse floor geometries round-robin, including two-floor bundles:
        // neither one alone may make room for the acquired bench.
        var parent=new Candidate(basis,0,1,0,0,NetCost(basis));
        var singles=FloorVariants(parent).Skip(1).ToArray();
        var variants=singles.Concat(singles.SelectMany(b=>FloorVariants(new(b,0,1,0,0,NetCost(b))).Skip(1))).DistinctBy(Key).Take(48).ToArray();
        var streams=variants.Select(b=>FillRoom(b).GetEnumerator()).ToList();
        try{
            while(streams.Count>0)for(int i=streams.Count-1;i>=0;i--){
                Cancellation.ThrowIfCancellationRequested();
                if(!streams[i].MoveNext()){streams[i].Dispose();streams.RemoveAt(i);continue;}
                yield return BindEquipment(streams[i].Current);
            }
        }finally{foreach(var stream in streams)stream.Dispose();}
    }
    IEnumerable<List<P>> DecisionActions(Candidate parent)
    {
        var basis=BindEquipment(parent.Board);
        var actions=new List<IEnumerable<List<P>>>{UpgradeActions(basis),EquipmentActions(basis),RosterActions(basis),SpatialActions(basis)};
        if(PreferExpansion)actions.Add(ExpansionActions(basis));
        var streams=actions.Select(s=>s.GetEnumerator()).ToList();
        try{
            while(streams.Count>0)for(int i=streams.Count-1;i>=0;i--){
                if(!streams[i].MoveNext()){streams[i].Dispose();streams.RemoveAt(i);continue;}yield return streams[i].Current;
            }
        }finally{foreach(var s in streams)s.Dispose();}
    }
    // Diagnostic work estimate, not a permission to send economic commands.
    int Transactions(Candidate c)=>Sales(c.Board).Length+c.Board.Sum(p=>(stock[p.Inventory].Slot>=0?1:0)+(p.Level>stock[p.Inventory].Minimum?1:0));
}
