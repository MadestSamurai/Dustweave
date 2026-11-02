using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;
using P = Dustweave.Soul.NativeBattleKernel.Placement;
namespace Dustweave.Soul;

// Research-only bounded search. Produces a reviewable plan; never sends game
// commands. The execution boundary independently checks account/run/coins.
internal sealed partial class NativeFastPlanner : IDisposable
{
    static string Tables=>SoulAssets.Tables;
    static string Dll=>SoulAssets.Library;
    record Stock(int Index,int Id,int Minimum,int Maximum,int Kind,int Slot,int Price);
    record Shape(int Position,int Rotation,ulong Mask,ulong Halo,(ulong Cell,int Mode,int[] Tags)[] Links);
    record Candidate(List<P> Board,int Wins,int Trials,double Margin,double Seconds,int Cost)
    {
        internal double Rate=>(double)Wins/Trials;
        internal (double,double,double) Rank=>DecisionRank(Rate,Margin,Seconds);
    }
    readonly NativeBattleKernel kernel=new(Tables,Dll);
    readonly Dictionary<int,JsonNode> items;
    readonly Dictionary<(int,int),JsonNode> units,equipment;
    readonly Dictionary<int,int[]> tags;
    readonly Dictionary<int,Stock> stock=[];
    readonly Dictionary<(int,int),Shape[]> geometry=[];
    readonly Dictionary<string,Candidate> cache=[];
    readonly Dictionary<string,(int Wins,int Trials,double Margin,double Seconds)> combatCache=[];
    readonly Random rng;
    internal CancellationToken Cancellation { get; set; }
    readonly Random validationRng;
    readonly JsonNode state;
    readonly byte[] template;
    readonly int budget,searchBudget,group,wave;
    readonly bool liquidate;
    readonly bool projectGeometry;
    readonly bool retainEconomicFrontier;
    readonly bool searchFloorCapacity;
    readonly List<List<P>> floors=[];
    readonly List<Candidate> elite=[];
    readonly Dictionary<int,Candidate> economicBridges=[];
    int evaluated,cacheHits,combatCacheHits,invalid;
    static int N(JsonNode? n,string k,int fallback=0)=>n?[k] is {} v?int.Parse(v.ToString()):fallback;
    static int[] A(JsonNode n,string key)=>(n[key]?.AsArray()??new()).Select(x=>int.Parse(x!.ToString())).ToArray();
    static JsonNode[] Load(string name)=>JsonNode.Parse(File.ReadAllText(Path.Combine(Tables,name+".json")))!.AsArray().Select(n=>n!).ToArray();
    static (int,int) Rotate(int x,int y,int r)=>r switch{1=>(-y,x),2=>(-x,-y),3=>(y,-x),_=>(x,y)};
    internal NativeFastPlanner(JsonNode input,int seed,bool allowLiquidation=false,bool preserveRouteGeometry=false,bool economicFrontier=false,bool floorCapacity=true)
    {
        state=input["state"]??input;rng=new(seed);validationRng=new(seed^0x51C82F13);retainEconomicFrontier=economicFrontier;searchFloorCapacity=floorCapacity;budget=N(state["play"],"coin");wave=N(state["play"],"stageId");
        group=N(Load("IBDungeonTable").Single(n=>N(n,"id")==N(state["play"],"dungeonId")),"stageGroupId");
        items=Load("IBItemTable").ToDictionary(n=>N(n,"id"));tags=items.ToDictionary(x=>x.Key,x=>A(x.Value,"itemTag"));
        units=Load("IBUnitItemTable").ToDictionary(n=>(N(n,"groupId"),N(n,"id")));
        equipment=Load("IBEquipItemTable").ToDictionary(n=>(N(n,"groupId"),N(n,"id")));
        var caps=state["upgradeCapabilities"]!.AsArray().GroupBy(n=>N(n,"ItemId")).ToDictionary(g=>g.Key,g=>g.Max(n=>N(n,"MaximumLevel")));
        foreach(var n in state["inventory"]!.AsArray()){
            int id=N(n,"id"),index=N(n,"inventoryIndex"),level=N(n,"level");stock.Add(index,new(index,id,level,Math.Max(level,caps.GetValueOrDefault(id,level)),N(items[id],"itemType"),-1,0));
        }
        int synthetic=stock.Keys.Max()+100;
        foreach(var n in state["shop"]!.AsArray().Where(n=>n?["isSoldOut"]?.GetValue<bool>()!=true)){
            int id=N(n,"id"),slot=N(n,"slot"),index=synthetic+slot;stock.Add(index,new(index,id,0,caps.GetValueOrDefault(id),N(items[id],"itemType"),slot,N(n,"price")));
        }
        liquidate=allowLiquidation;projectGeometry=preserveRouteGeometry;
        searchBudget=budget+(liquidate?stock.Values.Where(s=>s.Slot<0&&s.Kind is 1 or 2).Select(s=>Refund(s)).OrderDescending().Take(5).Sum():0);
        template=kernel.Compile(new JsonObject{["stage"]=new JsonObject{["groupId"]=group,["id"]=wave},["blue"]=new JsonObject{["items"]=new JsonArray()}});
        var initial=Initial().Where(p=>stock[p.Inventory].Kind==0).ToList();if(initial.Count>0)floors.Add(initial);
        for(int i=0;i<16;i++)floors.Add(MakeFloors());
    }
    List<P> Initial()=>state["deck"]!.AsArray().Select(n=>{var s=stock[N(n,"inventoryIndex")];return new P(s.Index,s.Id,N(n,"position"),N(n,"rotation"),s.Minimum);}).ToList();
    Shape[] Shapes(P p)=>Shapes(stock[p.Inventory],p.Level);
    Shape[] Shapes(Stock s,int level)
    {
        if(geometry.TryGetValue((s.Id,level),out var cached))return cached;
        var root=items[s.Id];var xs=A(root,"slotPositionX");var ys=A(root,"slotPositionY");var data=units.GetValueOrDefault((s.Id,level+1));
        int[] lx=data is null?[]:A(data,"slotPositionX"),ly=data is null?[]:A(data,"slotPositionY"),lt=data is null?[]:A(data,"slotType");
        var result=new List<Shape>();var seen=new HashSet<ulong>();
        for(int r=0;r<4;r++)for(int pos=0;pos<49;pos++){
            ulong mask=0,halo=0;bool valid=true;
            for(int n=0;n<xs.Length;n++){
                var (dx,dy)=Rotate(xs[n],ys[n],r);int x=pos%7+dx,y=pos/7+dy;if(x<0||x>=7||y<0||y>=7){valid=false;break;}mask|=1UL<<(y*7+x);
                foreach(var (nx,ny) in new[]{(x-1,y),(x+1,y),(x,y-1),(x,y+1)})if(nx>=0&&nx<7&&ny>=0&&ny<7)halo|=1UL<<(ny*7+nx);
            }
            if(!valid||s.Kind==0&&!seen.Add(mask))continue;
            var links=new List<(ulong,int,int[])>();
            for(int n=0;n<lt.Length;n++){
                int t=lt[n];if(t<1||t>3)continue;var (dx,dy)=Rotate(lx[n],ly[n],r);int x=pos%7+dx,y=pos/7+dy;if(x<0||x>=7||y<0||y>=7)continue;
                ulong bit=1UL<<(y*7+x);if((mask&bit)==0)links.Add((bit,N(data,$"slotType{t}ActiveTagType"),A(data!,$"slotType{t}ActiveTag")));
            }
            result.Add(new(pos,r,mask,halo,links.ToArray()));
        }
        return geometry[(s.Id,level)]=result.ToArray();
    }
    Shape ShapeOf(P p)
    {
        var result=Shapes(p).FirstOrDefault(s=>s.Position==p.Position&&s.Rotation==p.Rotation);
        if(result is not null)return result;
        if(stock[p.Inventory].Kind!=0)throw new InvalidDataException("Unknown placement");
        var row=items[p.Id];ulong mask=0;var xs=A(row,"slotPositionX");var ys=A(row,"slotPositionY");
        for(int i=0;i<xs.Length;i++){var (dx,dy)=Rotate(xs[i],ys[i],p.Rotation);int x=p.Position%7+dx,y=p.Position/7+dy;if(x<0||x>=7||y<0||y>=7)throw new InvalidDataException("Invalid floor");mask|=1UL<<(y*7+x);}
        return Shapes(p).First(s=>s.Mask==mask);
    }
    int Cost(P p)
    {
        var s=stock[p.Inventory];int cost=s.Price;if(s.Kind==0)return cost;
        var rows=s.Kind==1?units:equipment;
        for(int level=s.Minimum;level<p.Level;level++)cost+=N(rows[(s.Id,level+2)],"upgradePrice");
        return cost;
    }
    // Native server receipts round positive half-coins up (5 -> 3), unlike
    // the client's RoundToInt display. Execution still trusts the receipt.
    int Refund(Stock s)=>(N(items[s.Id],"itemPrice")+1)/2;
    Stock[] Sales(IReadOnlyList<P> board)
    {
        int needed=board.Sum(Cost)-budget;if(needed<=0||!liquidate)return [];
        var used=board.Select(p=>p.Inventory).ToHashSet();var selected=new List<Stock>();
        foreach(var s in stock.Values.Where(s=>s.Slot<0&&s.Kind is 1 or 2&&!used.Contains(s.Index)).OrderByDescending(Refund).ThenBy(s=>s.Minimum).Take(5)){
            if(Refund(s)<=0)continue;selected.Add(s);needed-=Refund(s);if(needed<=0)return selected.ToArray();
        }
        return [];
    }
    int NetCost(List<P> board)=>board.Sum(Cost)-Sales(board).Sum(Refund);
    List<P> MakeFloors()
    {
        var result=new List<P>();ulong occupied=0;int cost=0;
        foreach(var s in stock.Values.Where(s=>s.Kind==0).OrderBy(_=>rng.Next())){
            if(s.Slot>=0&&rng.NextDouble()<.65)continue;
            if(cost+s.Price>searchBudget)continue;
            var choices=Shapes(s,0).Where(o=>(o.Mask&occupied)==0).ToArray();if(choices.Length==0)continue;
            var best=choices.MaxBy(o=>4*BitOperations.PopCount(o.Halo&occupied)-.15*(Math.Abs(o.Position%7-3)+Math.Abs(o.Position/7-3))+rng.NextDouble()*2)!;
            result.Add(new(s.Index,s.Id,best.Position,best.Rotation,0));occupied|=best.Mask;cost+=s.Price;
        }
        return result;
    }
    static bool Match(int[] query,int mode,int[] tags)=>query.Length>0&&(mode==1?query.All(tags.Contains):mode==2&&query.Any(tags.Contains));
    double Links(Shape a,Shape b,int id)=>a.Links.Count(l=>(l.Cell&b.Mask)!=0&&!l.Tags.Contains(999)&&Match(l.Tags,l.Mode,tags[id]));
    List<P> Generate(Candidate? parent,bool preferOwned=false)
    {
        int desired=rng.NextDouble()<.4?rng.Next(1,Math.Min(8,stock.Values.Count(s=>s.Kind==1))+1):int.MaxValue;
        var board=(parent is null?floors[rng.Next(floors.Count)]:parent.Board.Where(p=>stock[p.Inventory].Kind==0)).ToList();
        ulong coverage=0,occupied=0;foreach(var p in board)coverage|=ShapeOf(p).Mask;int expense=board.Sum(Cost);
        var placed=new List<(P P,Shape S)>();
        if(parent is not null){
            var old=parent.Board.Where(p=>stock[p.Inventory].Kind==1).OrderBy(_=>rng.Next()).Skip(rng.Next(1,4));
            foreach(var p in old.Take(desired)){var shape=ShapeOf(p);placed.Add((p,shape));occupied|=shape.Mask;expense+=Cost(p);}
        }
        var pending=stock.Values.Where(s=>s.Kind==1&&(!preferOwned||s.Slot<0)&&!placed.Any(p=>p.P.Inventory==s.Index)).OrderBy(_=>rng.Next()).ToArray();
        // A new damage/healing role must get an affordable high-level trial
        // before old low-value pieces consume its budget. Pure random order
        // heavily favored the larger incumbent roster.
        int focus=pending.Any(s=>s.Slot>=0)&&rng.NextDouble()<.55?pending.Where(s=>s.Slot>=0).OrderBy(_=>rng.Next()).First().Index:-1;
        if(focus>=0)pending=pending.OrderByDescending(s=>s.Index==focus).ToArray();
        bool sparse=rng.NextDouble()<.25;
        foreach(var s in pending){
            if(placed.Count>=desired)break;
            if(placed.Count>0&&rng.NextDouble()<(sparse?.5:.10))continue;
            int level=s.Index==focus?s.Maximum:rng.Next(4) switch{0=>s.Maximum,1=>rng.Next(s.Minimum,s.Maximum+1),_=>s.Minimum};
            P? selected=null;Shape? chosen=null;
            for(;level>=s.Minimum;level--){
                var probe=new P(s.Index,s.Id,0,0,level);if(expense+Cost(probe)>searchBudget)continue;
                var options=Shapes(s,level).Where(o=>(o.Mask&coverage)==o.Mask&&(o.Mask&occupied)==0).ToArray();if(options.Length==0)continue;
                if(rng.NextDouble()<.25)chosen=options[rng.Next(options.Length)];
                else chosen=options.MaxBy(o=>placed.Sum(q=>Links(o,q.S,q.P.Id)+Links(q.S,o,s.Id))+.5*o.Links.Count(l=>l.Tags.Contains(999)&&(l.Cell&coverage)!=0&&(l.Cell&occupied)==0)+rng.NextDouble()*(rng.NextDouble()<.5?1:4));
                selected=probe with{Position=chosen!.Position,Rotation=chosen.Rotation};break;
            }
            if(selected is null)continue;placed.Add((selected,chosen!));occupied|=chosen!.Mask;expense+=Cost(selected);
        }
        board.AddRange(placed.Select(x=>x.P));
        // Resolve sockets exactly as the native wire encoder does. Ambiguous
        // or off-board positions are not proposed for execution.
        var sockets=new List<(P Unit,int Slot,int Position)>();
        foreach(var p in placed.Select(x=>x.P)){
            var row=units[(p.Id,p.Level+1)];var xs=A(row,"socketSlotPositionX");var ys=A(row,"socketSlotPositionY");int slot=0;
            for(int n=0;n<xs.Length;n++){if(xs[n]==0&&ys[n]==0)continue;var (dx,dy)=Rotate(xs[n],ys[n],p.Rotation);int x=p.Position%7+dx,y=p.Position/7+dy;if(x>=0&&x<7&&y>=0&&y<7)sockets.Add((p,slot,y*7+x));slot++;}
        }
        var unique=sockets.GroupBy(s=>s.Position).Where(g=>g.Count()==1).Select(g=>g.Single()).ToList();
        foreach(var s in stock.Values.Where(s=>s.Kind==2).OrderBy(_=>rng.Next())){
            if(unique.Count==0||expense+s.Price>searchBudget)continue;
            var row=equipment[(s.Id,s.Minimum+1)];var chosen=unique.MaxBy(p=>(tags[p.Unit.Id].Contains(N(row,"targetTagId"))?1:0)+rng.NextDouble());unique.Remove(chosen);
            var p=new P(s.Index,s.Id,-1,0,s.Minimum,chosen.Unit.Inventory,chosen.Slot);board.Add(p);expense+=Cost(p);
        }
        return board;
    }
    List<P> Neighbor(Candidate parent)
    {
        var members=parent.Board.Where(p=>stock[p.Inventory].Kind==1).ToArray();if(members.Length==0)return Generate(null);
        var unit=members[rng.Next(members.Length)];int kind=rng.Next(3);var result=parent.Board.Where(p=>stock[p.Inventory].Kind!=2).ToList();
        if(kind==0&&members.Length>1){result.Remove(unit);return result;}
        if(kind==1){
            var s=stock[unit.Inventory];int level=rng.Next(3) switch{0=>s.Maximum,1=>s.Minimum,_=>Math.Clamp(unit.Level+(rng.Next(2)==0?-1:1),s.Minimum,s.Maximum)};
            result[result.IndexOf(unit)]=unit with{Level=level};return result;
        }
        ulong coverage=0,occupied=0;
        foreach(var p in result.Where(p=>stock[p.Inventory].Kind==0))coverage|=ShapeOf(p).Mask;
        foreach(var p in result.Where(p=>stock[p.Inventory].Kind==1&&p.Inventory!=unit.Inventory))occupied|=ShapeOf(p).Mask;
        var valid=Shapes(unit).Where(o=>(o.Mask&coverage)==o.Mask&&(o.Mask&occupied)==0).ToArray();
        if(valid.Length>0){var option=valid[rng.Next(valid.Length)];result[result.IndexOf(unit)]=unit with{Position=option.Position,Rotation=option.Rotation};}
        return result;
    }
    IEnumerable<List<P>> LocalBoards(Candidate parent)
    {
        var baseBoard=parent.Board.Where(p=>stock[p.Inventory].Kind!=2).ToList();
        ulong coverage=0;foreach(var p in baseBoard.Where(p=>stock[p.Inventory].Kind==0))coverage|=ShapeOf(p).Mask;
        foreach(var unit in baseBoard.Where(p=>stock[p.Inventory].Kind==1).OrderBy(_=>rng.Next())){
            // Compare every affordable level without moving the rest of the
            // established core. Random roster rebuilding can otherwise hide
            // a useful upgrade behind unrelated placement changes.
            var member=stock[unit.Inventory];
            for(int level=member.Minimum;level<=member.Maximum;level++){
                if(level==unit.Level)continue;
                var upgraded=new List<P>(baseBoard);upgraded[upgraded.IndexOf(unit)]=unit with{Level=level};
                if(NetCost(upgraded)<=budget)yield return upgraded;
            }
            ulong occupied=0;foreach(var p in baseBoard.Where(p=>stock[p.Inventory].Kind==1&&p.Inventory!=unit.Inventory))occupied|=ShapeOf(p).Mask;
            foreach(var option in Shapes(unit).Where(o=>(o.Mask&coverage)==o.Mask&&(o.Mask&occupied)==0).OrderBy(_=>rng.Next())){
                var board=new List<P>(baseBoard);board[board.IndexOf(unit)]=unit with{Position=option.Position,Rotation=option.Rotation};yield return board;
            }
        }
    }
    IEnumerable<List<P>> RoleSeeds()
    {
        var anchors=stock.Values.Where(s=>s.Kind==1).GroupBy(s=>s.Id)
            .Select(g=>g.OrderByDescending(s=>s.Minimum).First()).OrderBy(_=>rng.Next()).ToArray();
        var owned=stock.Values.Where(s=>s.Kind==1&&s.Slot<0).GroupBy(s=>s.Id).Select(g=>g.MaxBy(s=>s.Minimum)!).OrderBy(_=>rng.Next()).Take(12).ToArray();
        // Visit all owned and offered anchors before repeating one. A low
        // level bench purchase is as important as the latest shop offer.
        for(int attempt=0;attempt<14;attempt++)
        foreach(var anchor in anchors)
        foreach(int count in new[]{2,1,0,3}){
            var floor=floors[attempt%floors.Count].Where(p=>stock[p.Inventory].Slot<0).ToList();
            ulong coverage=0,occupied=0;foreach(var p in floor)coverage|=ShapeOf(p).Mask;
            var selected=owned.Where(s=>s.Index!=anchor.Index).OrderBy(_=>rng.Next()).Take(count).Prepend(anchor).ToArray();
            var result=new List<P>(floor);var placed=new List<(P P,Shape S)>();int expense=0;
            foreach(var s in selected){
                int level=s.Index==anchor.Index?s.Maximum:s.Minimum;
                var p=new P(s.Index,s.Id,0,0,level);
                if(expense+Cost(p)>searchBudget)break;
                var valid=Shapes(s,level).Where(o=>(o.Mask&coverage)==o.Mask&&(o.Mask&occupied)==0).ToArray();
                if(valid.Length==0)break;
                var option=valid.MaxBy(o=>placed.Sum(q=>Links(o,q.S,q.P.Id)+Links(q.S,o,s.Id))+rng.NextDouble()*1.5)!;
                p=p with{Position=option.Position,Rotation=option.Rotation};result.Add(p);placed.Add((p,option));occupied|=option.Mask;expense+=Cost(p);
            }
            if(result.Any(p=>p.Inventory==anchor.Index))yield return result;
        }
    }
    IEnumerable<List<P>> CoreSeeds()
    {
        string library=Path.Combine(SoulAssets.Root,"campaign-library-active.json");
        if(!File.Exists(library))yield break;
        var data=JsonNode.Parse(File.ReadAllText(library))!;
        if(data["kernelSha256"]?.ToString()!=kernel.KernelSha256||data["tableSha256"]?.ToString()!=kernel.TableSha256||N(data,"season")!=N(state["play"],"season"))yield break;
        var chapter=data["dungeons"]!.AsArray().SingleOrDefault(n=>N(n,"id")==N(state["play"],"dungeonId"));if(chapter is null)yield break;
        var routes=chapter["routes"]!.AsArray().OrderByDescending(r=>r!["coreItemIds"]!.AsArray().Count(n=>stock.Values.Any(s=>s.Kind==1&&s.Id==int.Parse(n!.ToString())&&s.Slot<0))).Take(12).ToArray();
        foreach(var route in routes){
        // Preserve the library's relative links before falling back to a
        // fresh arrangement. Repack actual floor shapes with a bounded
        // bitmask beam; a different floor inventory need not erase a core.
        if(projectGeometry)foreach(var projected in ProjectCore(route!))yield return projected;
        for(int attempt=0;attempt<8;attempt++){
            var wanted=new List<(Stock S,int Level)>();var used=new HashSet<int>();
            foreach(var p in route!["finalLayout"]!.AsArray().Where(p=>N(items[N(p,"itemId")],"itemType")==1)){
                var s=stock.Values.Where(s=>s.Id==N(p,"itemId")&&!used.Contains(s.Index)).OrderBy(s=>s.Slot>=0).ThenByDescending(s=>s.Minimum).FirstOrDefault();
                if(s is null)continue;used.Add(s.Index);wanted.Add((s,Math.Clamp(N(p,"level"),s.Minimum,s.Maximum)));
            }
            if(wanted.Count<2)continue;
            // A route remains one persistent core. Existing bridge pieces may
            // supplement missing roles; absent route units are never invented.
            foreach(var s in stock.Values.Where(s=>s.Kind==1&&s.Slot<0&&!used.Contains(s.Index)).OrderBy(_=>rng.Next()).Take(attempt%4))wanted.Add((s,s.Minimum));
            var board=floors[attempt%floors.Count].ToList();ulong coverage=0,occupied=0;foreach(var p in board)coverage|=ShapeOf(p).Mask;
            int expense=board.Sum(Cost);var placed=new List<(P P,Shape S)>();
            foreach(var (s,requested) in wanted.OrderBy(_=>rng.Next())){
                int level=requested;while(level>s.Minimum&&expense+Cost(new P(s.Index,s.Id,0,0,level))>searchBudget)level--;
                var p=new P(s.Index,s.Id,0,0,level);if(expense+Cost(p)>searchBudget)continue;
                var valid=Shapes(s,level).Where(o=>(o.Mask&coverage)==o.Mask&&(o.Mask&occupied)==0).ToArray();if(valid.Length==0)continue;
                var option=valid.MaxBy(o=>placed.Sum(q=>Links(o,q.S,q.P.Id)+Links(q.S,o,s.Id))+rng.NextDouble()*2)!;
                p=p with{Position=option.Position,Rotation=option.Rotation};board.Add(p);placed.Add((p,option));occupied|=option.Mask;expense+=Cost(p);
            }
            yield return board;
        }
        }
    }
    IEnumerable<List<P>> ProjectCore(JsonNode route)
    {
        var mapped=new List<P>();var used=new HashSet<int>();
        foreach(var p in route["finalLayout"]!.AsArray().Where(p=>N(items[N(p,"itemId")],"itemType")==1)){
            var s=stock.Values.Where(s=>s.Id==N(p,"itemId")&&!used.Contains(s.Index)).OrderBy(s=>s.Slot>=0).ThenByDescending(s=>s.Minimum).FirstOrDefault();
            if(s is null)continue;used.Add(s.Index);
            mapped.Add(new(s.Index,s.Id,N(p,"position"),N(p,"rotation"),Math.Clamp(N(p,"level"),s.Minimum,s.Maximum)));
        }
        if(mapped.Count<2||mapped.Sum(Cost)>searchBudget)yield break;
        foreach(var (dx,dy) in new[]{(0,0),(-1,0),(1,0),(0,-1),(0,1)}){
            var units=mapped.Select(p=>p with{Position=p.Position+dy*7+dx}).ToList();
            if(mapped.Any(p=>p.Position%7+dx<0||p.Position%7+dx>=7||p.Position/7+dy<0||p.Position/7+dy>=7))continue;
            ulong needed=0;bool valid=true;
            foreach(var p in units){var shape=Shapes(p).FirstOrDefault(s=>s.Position==p.Position&&s.Rotation==p.Rotation);if(shape is null||(needed&shape.Mask)!=0){valid=false;break;}needed|=shape.Mask;}
            if(!valid)continue;
            var floor=Cover(needed,searchBudget-units.Sum(Cost));
            if(floor is not null){floor.AddRange(units);yield return floor;}
        }
    }
    List<P>? Cover(ulong target,int allowance)
    {
        var beam=new List<(ulong Mask,int Cost,List<P> Board)>{(0,0,[])};
        foreach(var floor in stock.Values.Where(s=>s.Kind==0).OrderBy(s=>s.Slot>=0).ThenByDescending(s=>A(items[s.Id],"slotPositionX").Length)){
            var next=new List<(ulong Mask,int Cost,List<P> Board)>(beam);
            foreach(var state in beam){
                if(state.Cost+floor.Price>allowance)continue;
                ulong missing=target&~state.Mask;
                foreach(var shape in Shapes(floor,0).Where(o=>(o.Mask&state.Mask)==0&&(o.Mask&missing)!=0)
                    .OrderByDescending(o=>BitOperations.PopCount(o.Mask&missing)).ThenByDescending(o=>BitOperations.PopCount(o.Halo&state.Mask)).Take(8)){
                    var board=new List<P>(state.Board){new(floor.Index,floor.Id,shape.Position,shape.Rotation,0)};
                    ulong mask=state.Mask|shape.Mask;
                    if((mask&target)==target)return board;
                    next.Add((mask,state.Cost+floor.Price,board));
                }
            }
            beam=next.GroupBy(s=>s.Mask).Select(g=>g.MinBy(s=>s.Cost))
                .OrderBy(s=>BitOperations.PopCount(target&~s.Mask)).ThenBy(s=>s.Cost).Take(32).ToList();
        }
        return null;
    }
    static string Key(IEnumerable<P> board)=>string.Join(';',board.Select(p=>$"{p.Inventory}/{p.Position}/{p.Rotation}/{p.Level}/{p.Target}/{p.Socket}"));
    internal static double Remaining(int hp,int maximum)=>Math.Clamp((double)hp/Math.Max(1,maximum),0,1);
    internal static (double,double,double) DecisionRank(double rate,double margin,double seconds)
        =>(rate,rate>=.98?-seconds:margin,rate>=.98?margin:rate==0?seconds:-seconds);
    Candidate? Evaluate(List<P> board,int[] seeds,bool useCache)
    {
        if(!board.Any(p=>stock[p.Inventory].Kind==1)||NetCost(board)>budget)return null;
        string key=Key(board);if(useCache&&cache.TryGetValue(key,out var prior)){cacheHits++;return prior;}
        try{
            kernel.RebuildBlue(template,board);
            string? combatKey=useCache?kernel.CombatKey(template)+":"+string.Join(',',seeds):null;
            if(combatKey is not null&&combatCache.TryGetValue(combatKey,out var memo)){
                combatCacheHits++;
                var reused=new Candidate(board,memo.Wins,memo.Trials,memo.Margin,memo.Seconds,NetCost(board));cache[key]=reused;return reused;
            }
            var results=kernel.Run(template,seeds);evaluated++;
            var ev=new Candidate(board,results.Count(r=>r.Winner==1),results.Length,results.Average(r=>Remaining(r.Blue.Hp,r.Blue.MaxHp)-Remaining(r.Red.Hp,r.Red.MaxHp)),results.Average(r=>(double)r.Seconds),NetCost(board));
            if(useCache){
                if(cache.Count>=20000)cache.Clear();cache[key]=ev;
                if(combatCache.Count>=8192)combatCache.Clear();
                combatCache[combatKey!]=(ev.Wins,ev.Trials,ev.Margin,ev.Seconds);
            }return ev;
        }catch(InvalidDataException){invalid++;return null;}
    }
    void Offer(Candidate? e)
    {
        if(e is null)return;
        // Keep this frontier separate from mutation parents. Inserting cheap
        // boards into the main evolution pool reduced difficult-wave search
        // quality in the real-state ablation. They compete at final selection.
        if(retainEconomicFrontier&&e.Rate>=.80&&(!economicBridges.TryGetValue(e.Cost,out var bridge)||e.Rank.CompareTo(bridge.Rank)>0))economicBridges[e.Cost]=e;
        if(elite.Any(x=>Key(x.Board)==Key(e.Board)))return;elite.Add(e);
        var ranked=elite.OrderByDescending(e=>e.Rank).ToArray();
        // Keep economic and roster alternatives, not only nearly identical
        // placements of the current local optimum.
        var chosen=ranked.Take(6).ToList();
        foreach(var alternative in ranked.GroupBy(e=>(e.Cost/Math.Max(1,budget/4),string.Join(',',e.Board.Where(p=>stock[p.Inventory].Kind==1).Select(p=>p.Id).Order()))).Select(g=>g.First())){
            if(!chosen.Contains(alternative))chosen.Add(alternative);if(chosen.Count>=24)break;
        }
        elite.Clear();elite.AddRange(chosen);
    }
    static JsonObject PlacementJson(P p){
        var row=new JsonObject{["inventoryIndex"]=p.Inventory,["itemId"]=p.Id,["position"]=p.Position,["rotation"]=p.Rotation,["level"]=p.Level};
        if(p.Target>=0){row["targetUnitInventoryIndex"]=p.Target;row["targetSlotIndex"]=p.Socket;}return row;
    }
    static JsonObject Metric(Candidate e)=>new(){["wins"]=e.Wins,["trials"]=e.Trials,["winRate"]=e.Rate,["margin"]=e.Margin,["battleSeconds"]=e.Seconds};
    (double Value,JsonArray Evidence) Future(Candidate candidate,int[] seeds)
    {
        var stages=Load("IBStageTable").Where(n=>N(n,"groupId")==group&&N(n,"id")>wave).OrderBy(n=>N(n,"id")).ToArray();
        var evidence=new JsonArray();if(stages.Length==0)return (-.01*candidate.Cost,evidence);
        double total=0;int count=0;
        foreach(var row in stages.Where((_,i)=>i==0||i==2)){
            int futureWave=N(row,"id"),income=stages.Where(s=>N(s,"id")<=futureWave).Sum(s=>N(s,"entryGainCredit"));
            var upgraded=candidate.Board.Select(p=>stock[p.Inventory].Kind==1?p with{Level=stock[p.Inventory].Maximum}:p).ToList();
            // Only use the upgrade projection when native table income can
            // fund it. This counterfactual buys no unseen units or floors.
            int extra=upgraded.Sum(Cost)-candidate.Board.Sum(Cost);
            var projected=extra<=budget-candidate.Cost+income?upgraded:candidate.Board;
            var future=kernel.Compile(new JsonObject{["stage"]=new JsonObject{["groupId"]=group,["id"]=futureWave},["blue"]=new JsonObject{["items"]=new JsonArray()}});
            kernel.RebuildBlue(future,projected);var results=kernel.Run(future,seeds);
            double win=(double)results.Count(r=>r.Winner==1)/results.Length;
            double margin=results.Average(r=>Remaining(r.Blue.Hp,r.Blue.MaxHp)-Remaining(r.Red.Hp,r.Red.MaxHp));
            total+=win+.35*margin;count++;
            evidence.Add(new JsonObject{["wave"]=futureWave,["wins"]=results.Count(r=>r.Winner==1),["trials"]=results.Length,["upgradeCost"]=ReferenceEquals(projected,upgraded)?extra:0,["futureIncome"]=income,["assumption"]="Same acquired roster; only affordable upgrades. Future purchases and rearrangement are not assumed."});
        }
        return (total/count-.012*candidate.Cost,evidence);
    }
    internal JsonObject Search(double seconds,string? warm,int? workBudget=null,bool reserveGrowth=true)
    {
        var watch=Stopwatch.StartNew();int proposals=0;
        // An optional fixed-work mode permits paired algorithm comparisons
        // independent of machine load. It never removes the wall-clock cap.
        bool Within(double phase){Cancellation.ThrowIfCancellationRequested();return watch.Elapsed.TotalSeconds<(workBudget.HasValue?seconds:seconds*phase)&&(!workBudget.HasValue||proposals<workBudget.Value*phase);}
        // Preserve the validated live search stream. Paired fixed-work
        // experiments use an independent fixed validation stream.
        var evalRng=workBudget.HasValue?validationRng:rng;
        var screen=Enumerable.Range(0,12).Select(_=>evalRng.Next(1,int.MaxValue)).ToArray();
        var incumbent=Evaluate(Initial(),screen,true);Offer(incumbent);
        if(warm is not null){
            var rows=JsonNode.Parse(File.ReadAllText(warm))!["recommendation"]!["layout"]!.AsArray();
            var board=rows.Select(n=>new P(N(n,"inventoryIndex"),N(n,"itemId"),N(n,"position",-1),N(n,"rotation"),N(n,"level"),N(n,"targetUnitInventoryIndex",-1),N(n,"targetSlotIndex",-1))).ToList();
            if(board.All(p=>stock.TryGetValue(p.Inventory,out var s)&&s.Id==p.Id&&s.Minimum<=p.Level&&p.Level<=s.Maximum))Offer(Evaluate(board,screen,true));
        }
        foreach(var seed in CoreSeeds()){
            if(!Within(.15))break;proposals++;Offer(Evaluate(seed,screen,true));
        }
        foreach(var seed in RoleSeeds()){
            if(!Within(.25))break;proposals++;Offer(Evaluate(seed,screen,true));
        }
        while(Within(.7)){
            if(++proposals%100==0){floors.Add(MakeFloors());if(floors.Count>24)floors.RemoveAt(0);}
            Candidate? parent=elite.Count>0&&rng.NextDouble()<.75?elite[rng.Next(elite.Count)]:null;
            Offer(Evaluate(parent is not null&&rng.NextDouble()<.45?Neighbor(parent):Generate(parent),screen,true));
        }
        // Exhaust each affordable one-unit move around promising small cores.
        // Random mutation alone repeatedly misses sparse support link layouts.
        var polishSeeds=Enumerable.Range(0,32).Select(_=>evalRng.Next(1,int.MaxValue)).ToArray();
        var starting=elite.OrderByDescending(e=>e.Rank).Take(8).Select(e=>Evaluate(e.Board,polishSeeds,false)!).ToArray();
        elite.Clear();cache.Clear();foreach(var e in starting)Offer(e);
        int sweep=0;
        while(Within(1)&&elite.Count>0){
            var parent=elite.OrderByDescending(e=>e.Rank).ElementAt(sweep++%Math.Min(4,elite.Count));
            foreach(var board in LocalBoards(parent)){
                if(!Within(1))break;proposals++;Offer(Evaluate(board,polishSeeds,true));
            }
        }
        var select=Enumerable.Range(0,128).Select(_=>evalRng.Next(1,int.MaxValue)).ToArray();
        var shortlist=elite.OrderByDescending(e=>e.Rank).Take(6)
            .Concat(elite.Where(e=>e.Rate>=.83).OrderBy(e=>e.Cost).Take(6))
            .Concat(economicBridges.Values.OrderBy(e=>e.Cost).Take(6))
            .Concat(!retainEconomicFrontier||incumbent is null?[]:new[]{incumbent}).DistinctBy(e=>Key(e.Board)).ToArray();
        var finalists=shortlist.Select(e=>Evaluate(e.Board,select,false)!).OrderByDescending(e=>e.Rank).ToArray();
        if(finalists.Length==0)throw new InvalidDataException("No legal native candidate");
        var auditSeeds=Enumerable.Range(0,512).Select(_=>evalRng.Next(1,int.MaxValue)).ToArray();
        // Prefer a reliable inexpensive bridge, retaining money for the
        // whole-dungeon core rather than maximizing this wave's investment.
        Candidate? accepted=null;
        int remaining=Load("IBStageTable").Count(n=>N(n,"groupId")==group&&N(n,"id")>=wave),lives=N(state["play"],"life");
        bool Ready(Candidate c)=>NativeSearchRisk.Assess(c.Wins,c.Trials,lives,remaining)["ready"]!.GetValue<bool>();
        var eligible=finalists.Where(Ready).Select(e=>(Candidate:e,Horizon:Future(e,select.Take(32).ToArray()))).OrderByDescending(x=>x.Horizon.Value).ThenBy(x=>x.Candidate.Cost).ToArray();
        JsonArray futureEvidence=new();
        foreach(var choice in eligible){
            var audited=Evaluate(choice.Candidate.Board,auditSeeds,false)!;if(Ready(audited)){accepted=audited;futureEvidence=choice.Horizon.Evidence;break;}
        }
        // A weak screening winner must not exclude the other finalists from
        // the independent audit, especially when every screen has zero wins.
        var best=accepted??finalists.Take(3).Select(c=>Evaluate(c.Board,auditSeeds,false)!).MaxBy(c=>c.Rank)!;
        // Run this after the original search and its independent audit. It
        // cannot perturb the original proposals, seeds or surviving fallback.
        var capacity=searchFloorCapacity?ExploreCapacity(finalists.Prepend(best).DistinctBy(e=>Key(e.Board)).Take(4).ToArray(),polishSeeds,workBudget.HasValue):[];
        foreach(var extension in capacity.OrderByDescending(e=>e.Rank)){
            var audited=Evaluate(extension.Board,auditSeeds,false)!;
            if(Ready(audited)){
                var horizon=Future(audited,select.Take(32).ToArray());
                if(!Ready(best)||audited.Rate>=best.Rate&&horizon.Value>=Future(best,select.Take(32).ToArray()).Value){best=audited;futureEvidence=horizon.Evidence;}
            }else if(!Ready(best)&&audited.Rank.CompareTo(best.Rank)>0)best=audited;
        }
        // The spatial search above may select a new board after its parent's
        // fill trials. Recheck the actual final board against free additions.
        var beforeRoster=Key(best.Board);
        best=CompleteRoster(best,select,auditSeeds);
        if(beforeRoster!=Key(best.Board))futureEvidence=Future(best,select.Take(32).ToArray()).Evidence;
        var path=new JsonArray();
        foreach(var sale in Sales(best.Board))path.Add(new JsonObject{["kind"]="sell",["identity"]=$"sell:{sale.Index}",["inventoryIndex"]=sale.Index,["itemId"]=sale.Id,["level"]=sale.Minimum,["price"]=-Refund(sale),["cost"]=-Refund(sale)});
        foreach(var p in best.Board){
            var s=stock[p.Inventory];
            if(s.Slot>=0)path.Add(new JsonObject{["kind"]="buy",["identity"]=$"shop:{s.Slot}",["slot"]=s.Slot,["itemId"]=s.Id,["price"]=s.Price,["cost"]=s.Price,["syntheticInventoryIndex"]=s.Index});
            for(int level=s.Minimum;level<p.Level;level++){
                int price=N((s.Kind==1?units:equipment)[(s.Id,level+2)],"upgradePrice");
                path.Add(new JsonObject{["kind"]="upgrade",["identity"]=$"upgrade:{s.Index}:{level+1}",["inventoryIndex"]=s.Index,["itemId"]=s.Id,["fromLevel"]=level,["toLevel"]=level+1,["price"]=price,["cost"]=price});
            }
        }
        var initial=new JsonObject{["season"]=state["play"]!["season"]!.DeepClone(),["dungeonId"]=state["play"]!["dungeonId"]!.DeepClone(),["stageId"]=wave,["coin"]=budget,
            ["inventory"]=new JsonArray(state["inventory"]!.AsArray().Select(n=>(JsonNode)new JsonObject{["index"]=N(n,"inventoryIndex"),["id"]=N(n,"id"),["level"]=N(n,"level")}).ToArray()),
            ["shop"]=new JsonArray(state["shop"]!.AsArray().Select(n=>(JsonNode)new JsonObject{["slot"]=N(n,"slot"),["id"]=N(n,"id"),["price"]=N(n,"price"),["soldOut"]=n?["isSoldOut"]?.GetValue<bool>()??false}).ToArray())};
        var result=new JsonObject{["status"]=Ready(best)?"ready":"search_required",["riskBudget"]=NativeSearchRisk.Assess(best.Wins,best.Trials,lives,remaining),["solver"]="dustweave-soul",["decisionSeconds"]=watch.Elapsed.TotalSeconds,["proposals"]=proposals,["evaluated"]=evaluated,["cacheHits"]=cacheHits,["invalid"]=invalid,["kernelSha256"]=kernel.KernelSha256,["auditSeeds"]=new JsonArray(auditSeeds.Select(s=>(JsonNode)JsonValue.Create(s)!).ToArray()),["initialState"]=initial,
            ["recommendation"]=new JsonObject{["cost"]=best.Cost,["coinAfter"]=budget-best.Cost,["transactions"]=path.Count,["path"]=path,["layout"]=new JsonArray(best.Board.Select(p=>(JsonNode)PlacementJson(p)).ToArray()),["deployedUnits"]=best.Board.Count(p=>stock[p.Inventory].Kind==1),["battle"]=Metric(best),["continuationProbe"]=futureEvidence},
            ["limits"]=new JsonArray("Research candidate, not a proof of optimality.","No unseen shop assumptions or future wave guarantee.","Current account upgrade caps and current coin budget enforced.")};
        result["tableSha256"]=kernel.TableSha256;
        result["combatCacheHits"]=combatCacheHits;
        result["economicFrontierEnabled"]=retainEconomicFrontier;
        result["proposalBudget"]=workBudget;
        result["floorCapacitySearch"]=new JsonObject{["enabled"]=searchFloorCapacity,["proposals"]=capacityProposals,["seconds"]=capacitySeconds,["finalists"]=capacity.Length};
        result["finalRosterAudit"]=rosterAudit.DeepClone();
        if(reserveGrowth)NativeGrowthPolicy.Append(result,state,items,units,kernel.KernelSha256,kernel.TableSha256);
        return result;
    }
    public void Dispose()=>kernel.Dispose();
}
