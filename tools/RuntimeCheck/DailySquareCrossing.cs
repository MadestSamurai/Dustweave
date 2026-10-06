using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;

internal static class DailySquareCrossing
{
    static double Distance(JsonObject a,JsonObject b)=>Math.Sqrt(Math.Pow(a["X"]!.GetValue<double>()-b["X"]!.GetValue<double>(),2)+Math.Pow(a["Z"]!.GetValue<double>()-b["Z"]!.GetValue<double>(),2));
    public static async Task Merchant(DailyWorkflow w,string output)
    {
        var frame=(await w.Observe()).Frame;
        if(!S(frame["Scene"]).StartsWith("Map3009_"))throw new InvalidOperationException("商人导航验证要求已在广场，不切换卡带。");
        var trade=new DailyTradeExecution(w,new JsonObject());
        if(await w.Has("ShopPopupUI"))throw new InvalidOperationException("有待确认的商品弹窗，保留现场。");
        await trade.Close();
        var runs=new JsonArray();
        for(int i=0;i<2;i++)
        {
            frame=(await w.Observe()).Frame;
            if(i==1)
            {
                var start=frame["SquareNavigation"]!.AsObject();
                var destination=Rows(start["Landmarks"]).Where(p=>S(p["Name"]).StartsWith("NPCController:")&&Distance(start,p)>30&&Distance(start,p)<180).OrderByDescending(p=>Distance(start,p)).FirstOrDefault()
                    ??throw new InvalidOperationException("没有可用的广场远端测试点。");
                DailyJson.Write(Path.Combine(output,"far-endpoint.json"),destination);
                await w.Step(O(("ui","GameFieldDefaultUI"),("operation","square_route_probe"),("items",new JsonArray(new[]{"X","Y","Z"}.Select(k=>JsonValue.Create((long)Math.Round(destination[k]!.GetValue<double>()*1000))).ToArray())),("reason","从商人前往广场远端，验证远距离返回商人的 A* 路线")));
                await DailySquareNavigation.Wait(w,"square_route_probe",async()=>S((await w.Observe()).Frame["SquareNavigation"]?["State"])=="arrived");
                DailyJson.Write(Path.Combine(output,"far-arrived.json"),(await w.Observe()).Frame);
                await DailySquareNavigation.Stop(w);
            }
            var before=(await w.Observe()).Frame;
            DailyJson.Write(Path.Combine(output,$"merchant-{i+1}-before.json"),before);
            double began=w.Time;
            await trade.OpenShop();
            var opened=(await w.Observe()).Frame;
            if(!DailyNavigationDecision.Types(opened).Contains("ShopUI"))throw new InvalidOperationException("没有打开原生商店。");
            DailyJson.Write(Path.Combine(output,$"merchant-{i+1}-opened.json"),opened);
            DailyJson.Write(Path.Combine(output,$"merchant-{i+1}-evidence.json"),await w.Evidence("trade.navigation","trade.currency"));
            runs.Add(O(("run",i+1),("seconds",w.Time-began),("shopOpened",true),("route",opened["SquareNavigation"]?.DeepClone())));
            await trade.Close();
            DailyJson.Write(Path.Combine(output,"result.json"),O(("state",i==1?"passed":"running"),("runs",runs.DeepClone()),("purchases",0),("sales",0),("navmesh",false)));
            Console.WriteLine($"商人实机验证 {i+1}：已打开并关闭原生商店，耗时 {w.Time-began:F1} 秒");
        }
    }
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