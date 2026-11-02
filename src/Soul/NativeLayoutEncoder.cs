using System.Text.Json.Nodes;
namespace Dustweave.Soul;

internal static class NativeLayoutEncoder
{
    static long Index(JsonNode value)=>long.Parse(value.ToString(),System.Globalization.CultureInfo.InvariantCulture);
    internal static JsonArray Encode(JsonArray layout,JsonArray unitRows)
    {
        var sockets=new Dictionary<(long unit,int slot),int>();var positions=new Dictionary<int,int>();
        var entries=layout.ToDictionary(p=>Index(p!["inventoryIndex"]!),p=>p!.AsObject());
        foreach(var p in entries.Values)
        {
            int id=p["itemId"]!.GetValue<int>(),level=p["level"]?.GetValue<int>()??0;
            var row=unitRows.SingleOrDefault(r=>r!["groupId"]!.GetValue<int>()==id&&r["id"]!.GetValue<int>()==level+1);
            if(row is null)continue;
            int pos=p["position"]!.GetValue<int>(),rot=p["rotation"]?.GetValue<int>()??0,n=0;
            var xs=row["socketSlotPositionX"]!.AsArray();var ys=row["socketSlotPositionY"]!.AsArray();
            if(xs.Count!=ys.Count)throw new InvalidDataException("Socket coordinate arrays disagree");
            for(int i=0;i<xs.Count;i++)
            {
                int x=xs[i]!.GetValue<int>(),y=ys[i]!.GetValue<int>();if(x==0&&y==0)continue;
                (x,y)=(rot%4) switch {1=>(-y,x),2=>(-x,-y),3=>(y,-x),_=>(x,y)};
                x+=pos%7;y+=pos/7;
                // Out-of-board coordinates alias under the game's scalar
                // encoding; never claim that ambiguous transport is safe.
                int encoded=x<0||x>=7||y<0||y>=7?-1:y*7+x;
                sockets[(Index(p["inventoryIndex"]!),n++)]=encoded;
                if(encoded>=0)positions[encoded]=positions.GetValueOrDefault(encoded)+1;
            }
        }
        var result=new JsonArray();var used=new HashSet<(long,int)>();
        foreach(var p in entries.Values)
        {
            int position=p["position"]?.GetValue<int>()??-1,rotation=p["rotation"]?.GetValue<int>()??0;
            if(p["targetUnitInventoryIndex"] is {} target&&Index(target)>=0)
            {
                var key=(Index(target),p["targetSlotIndex"]!.GetValue<int>());
                if(!used.Add(key)||!sockets.TryGetValue(key,out position)||position<0||positions[position]!=1)
                    throw new InvalidDataException("Equipment socket cannot be uniquely encoded for the current client");
                rotation=0;
            }
            if(position<0||position>=49||rotation<0||rotation>3)throw new InvalidDataException("Invalid native deck position");
            result.Add(new JsonObject{["inventoryIndex"]=p["inventoryIndex"]!.DeepClone(),["position"]=position,["rotation"]=rotation});
        }
        return result;
    }
    internal static void Test()
    {
        var rows=JsonNode.Parse("""[{"groupId":10001,"id":1,"socketSlotPositionX":[0,1],"socketSlotPositionY":[0,0]}]""")!.AsArray();
        var plan=JsonNode.Parse("""[{"inventoryIndex":10,"itemId":10001,"position":24,"rotation":1,"level":0},{"inventoryIndex":20,"itemId":30001,"position":-1,"rotation":0,"level":0,"targetUnitInventoryIndex":10,"targetSlotIndex":0}]""")!.AsArray();
        if(Encode(plan,rows)[1]!["position"]!.GetValue<int>()!=31)throw new Exception("Rotated native socket encoding");
        plan[0]!["inventoryIndex"]=10;plan[1]!["inventoryIndex"]=20L;plan[1]!["targetUnitInventoryIndex"]=10;
        if(Encode(plan,rows)[1]!["position"]!.GetValue<int>()!=31)throw new Exception("In-memory and parsed numeric identities differ");
        plan[1]!.AsObject().Remove("position");
        if(Encode(plan,rows)[1]!["position"]!.GetValue<int>()!=31)throw new Exception("Logical socket must not require a board coordinate");
        plan.Add(JsonNode.Parse("""{"inventoryIndex":11,"itemId":10001,"position":30,"rotation":0,"level":0}"""));
        try{Encode(plan,rows);throw new Exception("Ambiguous socket accepted");}catch(InvalidDataException){}
        Console.WriteLine("Native layout encoding checks passed: 3");
    }
}
