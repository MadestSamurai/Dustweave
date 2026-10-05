using System.Text.Json.Nodes;
using BD2Daily;
using static BD2Daily.DailyData;

internal static class DailySquareCrossing
{
    static double Distance(JsonObject a,JsonObject b)=>Math.Sqrt(Math.Pow(a["X"]!.GetValue<double>()-b["X"]!.GetValue<double>(),2)+Math.Pow(a["Z"]!.GetValue<double>()-b["Z"]!.GetValue<double>(),2));
    public static async Task Run(DailyWorkflow w,string output,bool inspectOnly)
    {
        var frame=(await w.Observe()).Frame;
        DailyJson.Write(Path.Combine(output,"square-observation.json"),frame);
        if(inspectOnly)return;
        if(!S(frame["Scene"]).StartsWith("Map3009_"))throw new InvalidOperationException("穿越诊断要求已经进入广场；不切换卡带或进行战斗。");
        if(await w.Has("ShopPopupUI"))await w.Step("ShopPopupUI",back:true,absent:"ShopPopupUI",reason:"取消未确认的商品预览，准备广场路线测试");
        if(await w.Has("ShopUI"))await new DailyTradeExecution(w,new JsonObject()).Close();
        await DailyTravel.Ready(w,"GameFieldDefaultUI",10);
        frame=(await w.Observe()).Frame;
        var route=frame["SquareNavigation"]?.AsObject()??throw new InvalidOperationException("广场 A* 组件未就绪");
        var points=Rows(route["Landmarks"]).Where(x=>S(x["Name"]).StartsWith("NPCController:")).Where(x=>double.IsFinite(x["X"]!.GetValue<double>())&&double.IsFinite(x["Z"]!.GetValue<double>())).ToArray();
        var pairs=(from a in points from b in points where N(a["Id"])<N(b["Id"]) let distance=Distance(a,b) where distance>30&&distance<180 orderby distance descending select new{a,b,distance}).ToArray();
        if(pairs.Length==0)throw new InvalidOperationException("当前广场未读到相距 30 米以上的原生交互点；保留坐标待检查。");
        var pair=pairs[0];
        DailyJson.Write(Path.Combine(output,"endpoints.json"),new{a=pair.a,b=pair.b,distance=pair.distance});
        var result=new JsonArray();
        foreach(var destination in new[]{pair.a,pair.b,pair.a})
        {
            int leg=result.Count+1;var start=(await w.Observe()).Frame["SquareNavigation"]!.AsObject();double began=w.Time;
            var action=O(("ui","GameFieldDefaultUI"),("operation","square_route_probe"),("items",new JsonArray(new[]{"X","Y","Z"}.Select(k=>JsonValue.Create((long)Math.Round(destination[k]!.GetValue<double>()*1000))).ToArray())),("reason","实测广场两端普通方向移动，不使用 NavMesh"));
            await w.Step(action);
            using var log=new StreamWriter(Path.Combine(output,$"leg-{leg}.jsonl")){AutoFlush=true};
            while(w.Time-began<245)
            {
                frame=(await w.Observe()).Frame;DailySquareNavigation.Inspect(frame,"square_route_probe");route=frame["SquareNavigation"]!.AsObject();
                log.WriteLine(route.ToJsonString());
                if(S(route["Kind"])=="square_route_probe"&&S(route["State"])=="arrived")break;
                await w.Delay(400);
            }
            if(S(route["State"])!="arrived")throw new TimeoutException("广场穿越没有确认到达");
            result.Add(O(("leg",leg),("seconds",w.Time-began),("straightDistance",Distance(start,destination)),("destination",destination.DeepClone()),("route",route.DeepClone())));
            DailyJson.Write(Path.Combine(output,"result.json"),O(("state",result.Count==3?"passed":"running"),("legs",result.DeepClone()),("gameTouched",true),("navmesh",false)));
            Console.WriteLine($"广场第 {leg} 段完成：{w.Time-began:F1} 秒，{route["Travelled"]} 米");
        }
        DailyJson.Write(Path.Combine(output,"rewards.json"),await DailySquare.Run(w,"square"));
        await w.Step("GameFieldDefaultUI",operation:"square_shop_nav",reason:"穿越验证完成，按相同 A* 流程返回商人");
        await DailySquareNavigation.Wait(w,"square_shop_nav",async()=>S((await w.Observe()).Frame["SquareNavigation"]?["State"])=="arrived");
        DailyJson.Write(Path.Combine(output,"merchant-return.json"),(await w.Observe()).Frame["SquareNavigation"]!);
        await DailySquareNavigation.Stop(w);
    }
}