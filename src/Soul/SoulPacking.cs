using System.Numerics;
using System.Text.Json.Nodes;
namespace Dustweave.Soul;

// A concrete spatial witness for optional purchases. Cell count alone cannot
// distinguish usable room from fragmented holes. This never authorizes combat.
internal sealed class SoulPacking(Dictionary<int,JsonNode> items)
{
    internal record Place(int Index,int Id,int Position,int Rotation,ulong Mask);
    readonly Dictionary<int,Place[]> shapes=[];
    static int N(JsonNode? n,string key)=>n?[key] is {} v?int.Parse(v.ToString()):0;
    internal Place[] Shapes(int id)
    {
        if(shapes.TryGetValue(id,out var known))return known;
        var row=items[id];var xs=row["slotPositionX"]!.AsArray();var ys=row["slotPositionY"]!.AsArray();
        var result=new List<Place>();var seen=new HashSet<ulong>();
        for(int r=0;r<4;r++)for(int pos=0;pos<49;pos++){
            ulong mask=0;bool valid=true;
            for(int i=0;i<xs.Count;i++){
                int x=xs[i]!.GetValue<int>(),y=ys[i]!.GetValue<int>();
                (x,y)=r switch{1=>(-y,x),2=>(-x,-y),3=>(y,-x),_=>(x,y)};
                x+=pos%7;y+=pos/7;if(x<0||x>=7||y<0||y>=7){valid=false;break;}mask|=1UL<<(y*7+x);
            }
            if(valid&&mask!=0&&seen.Add(mask))result.Add(new(0,id,pos,r,mask));
        }
        return shapes[id]=result.ToArray();
    }
    internal ulong Mask(JsonNode placement)
    {
        int id=N(placement,"itemId"),pos=N(placement,"position"),rot=N(placement,"rotation");
        var row=items[id];var xs=row["slotPositionX"]!.AsArray();var ys=row["slotPositionY"]!.AsArray();ulong mask=0;
        for(int i=0;i<xs.Count;i++){
            int x=xs[i]!.GetValue<int>(),y=ys[i]!.GetValue<int>();
            (x,y)=rot switch{0=>(x,y),1=>(-y,x),2=>(-x,-y),3=>(y,-x),_=>throw new InvalidDataException("Invalid rotation")};
            x+=pos%7;y+=pos/7;if(pos<0||pos>=49||x<0||x>=7||y<0||y>=7)throw new InvalidDataException("Placement outside board");mask|=1UL<<(y*7+x);
        }
        return mask;
    }
    internal JsonArray? Fit(JsonArray floorLayout,IEnumerable<(int Index,int Id)> roster,int nodeLimit=12000)
    {
        ulong coverage=0;
        foreach(var p in floorLayout){var mask=Mask(p!);if((coverage&mask)!=0)return null;coverage|=mask;}
        var members=roster.ToArray();if(members.Select(p=>p.Index).Distinct().Count()!=members.Length)return null;
        if(members.Sum(p=>items[p.Id]["slotPositionX"]!.AsArray().Count)>BitOperations.PopCount(coverage))return null;
        var options=members.Select(p=>Shapes(p.Id).Where(s=>(s.Mask&coverage)==s.Mask).Select(s=>s with{Index=p.Index}).ToArray()).ToArray();
        if(options.Any(p=>p.Length==0))return null;
        var placed=new Place?[members.Length];int nodes=0;
        bool Search(ulong occupied,int count){
            if(count==members.Length)return true;if(++nodes>nodeLimit)return false;
            int next=-1;Place[]? choices=null;
            for(int i=0;i<members.Length;i++)if(placed[i] is null){
                var valid=options[i].Where(p=>(p.Mask&occupied)==0).ToArray();if(valid.Length==0)return false;
                if(choices is null||valid.Length<choices.Length){next=i;choices=valid;}
            }
            foreach(var p in choices!){placed[next]=p;if(Search(occupied|p.Mask,count+1))return true;placed[next]=null;if(nodes>nodeLimit)break;}
            return false;
        }
        if(!Search(0,0))return null;
        return new JsonArray(placed.Select(p=>(JsonNode)new JsonObject{["inventoryIndex"]=p!.Index,["itemId"]=p.Id,["position"]=p.Position,["rotation"]=p.Rotation}).ToArray());
    }
    // All retained units, not just the latest purchase, must have room. Ordinary
    // combat plans can still replace units; optional stockpiling cannot hide them.
    internal JsonArray? PurchaseRoom(JsonNode state,JsonArray layout,JsonArray path)
    {
        var sold=path.Where(p=>p?["kind"]?.ToString()=="sell").Select(p=>N(p,"inventoryIndex")).ToHashSet();
        var retained=state["inventory"]!.AsArray().Where(p=>!sold.Contains(N(p,"inventoryIndex"))).Select(p=>(Index:N(p,"inventoryIndex"),Id:N(p,"id"))).ToList();
        retained.AddRange(path.Where(p=>p?["kind"]?.ToString()=="buy").Select(p=>(N(p,"syntheticInventoryIndex"),N(p,"itemId"))));
        var owned=retained.ToDictionary(p=>p.Index,p=>p.Id);
        var roster=retained.Where(p=>N(items[p.Id],"itemType")==1).ToArray();
        var floors=new JsonArray(layout.Where(p=>N(items[N(p,"itemId")],"itemType")==0&&owned.GetValueOrDefault(N(p,"inventoryIndex"))==N(p,"itemId")).Select(p=>p!.DeepClone()).ToArray());
        var witness=Fit(floors,roster);
        return witness is null?null:new JsonArray(floors.Concat(witness).Select(p=>p!.DeepClone()).ToArray());
    }
    internal static void Test()
    {
        var items=JsonNode.Parse("""[{"id":1,"slotPositionX":[0],"slotPositionY":[0]},{"id":2,"slotPositionX":[0,1],"slotPositionY":[0,0],"itemType":1},{"id":3,"slotPositionX":[0,1,2],"slotPositionY":[0,0,0]}]""")!.AsArray().ToDictionary(p=>N(p,"id"),p=>p!);
        var pack=new SoulPacking(items);
        var holes=JsonNode.Parse("""[{"inventoryIndex":1,"itemId":1,"position":0},{"inventoryIndex":2,"itemId":1,"position":2}]""")!.AsArray();
        if(pack.Fit(holes,[(10,2)])!=null)throw new Exception("Fragmented holes were treated as usable space");
        var floor=JsonNode.Parse("""[{"inventoryIndex":1,"itemId":3,"position":0}]""")!.AsArray();
        if(pack.Fit(floor,[(10,2)])?.Count!=1||pack.Fit(floor,[(10,2),(11,2)])!=null)throw new Exception("Reserve roster exceeds physical floor coverage");
        var vertical=JsonNode.Parse("""[{"inventoryIndex":1,"itemId":3,"position":0,"rotation":1}]""")!.AsArray();
        if(pack.Fit(vertical,[(10,2)])?.Single()?["rotation"]?.GetValue<int>()!=1)throw new Exception("Rotation packing missed a valid witness");
        Console.WriteLine("Packing checks passed: rotations, fragmented space, cumulative roster");
    }
}
