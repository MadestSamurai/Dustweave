using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public static class DailySaffron
{
    internal static JsonObject Command(string owner, int pid, long now) => O(("OwnerId",owner),("ProcessId",pid),("Enabled",true),("UntilUtcTicks",now+TimeSpan.TicksPerSecond*3),
        ("SaffronDaily",true),("Logging",false),("Mining",false),("Farming",true),("Cooking",true),("FixedCrop",true),("FixedSeedId",15),("RecipeId",7),
        ("IntervalMs",100),("PlantingBudget",0),("CookingBatch",1000),("UseNavMesh",false),("UseVehicle",false),("DashRecovery",true),
        ("SellThreshold",9900),("CookingSaleBatch",100),("SellCookingBatches",false),("SellCookingForPlanting",false));
    public static async Task<JsonObject> Run(DailyWorkflow w)
    {
        if (!await DailyTravel.Enter(w,2,"square.pack",packType:13,surfaces:["AvatarLifeGameFieldDefaultUI"]))
            return DailyWorkflow.Skipped("领地卡带尚未开放，未使用料理");
        await DailyMiniGameChannel.Connect(w,"connect-territory");
        var channel=new DailyMiniGameChannel("BD2Territory",w.Context["actor"]!.AsArray());
        channel.Acquire();
        long now=w.Driver.UtcTicks;var prior=channel.Read("control.json");
        Require(!B(prior["Enabled"])||N(prior["UntilUtcTicks"])<=now,"领地已有运行中的控制器，未接管");
        string owner=Guid.NewGuid().ToString("N");int pid=I(w.Context["actor"]![0]);var command=Command(owner,pid,now);
        JsonObject state=new();double end=w.Time+19*60, ready=w.Time+8;string last="";bool observed=false;
        try
        {
            while(w.Time<end)
            {
                await w.Observe();state=channel.Read("latest.json");
                if(!DailyWeeklyFishing.Fresh(state,pid,w.Driver.UtcTicks)){
                    if(!observed&&w.Time<ready){await w.Delay(150);continue;}
                    throw new StageHostException("adapter","领地观察已过期，未继续操作");
                }
                observed=true;
                if(!B(state["Ready"])) {Require(w.Time<ready,"领地农田数据尚未就绪，未使用料理");await w.Delay(200);continue;}
                Require(S(state["Error"])=="",S(state["Error"]));
                var current=channel.Read("control.json");
                Require(!B(current["Enabled"])||N(current["UntilUtcTicks"])<=w.Driver.UtcTicks||S(current["OwnerId"])==owner,"领地控制权已改变，未继续操作");
                var cycle=state["Saffron"] as JsonObject;
                if(S(state["OwnerId"])==owner&&cycle!=null)
                {
                    string signature=S(cycle["State"])+"|"+S(cycle["Reason"]);
                    if(signature!=last){last=signature;w.Driver.Diagnostics.Event("saffron_phase",O(("cycle",cycle),("reason",state["Reason"])));}
                    if(S(cycle["State"]) is "completed" or "skipped" or "partial")
                        return O(("state",cycle["State"]),("reason",cycle["Reason"]),("saffron",cycle));
                }
                command["UntilUtcTicks"]=w.Driver.UtcTicks+TimeSpan.TicksPerSecond*3;
                channel.Write("control.json",command);await w.Delay(150);
            }
            return O(("state","partial"),("reason","藏红花环节超时，已停止；其余日常继续"));
        }
        finally
        {
            if(S(channel.Read("control.json")["OwnerId"])==owner){command["Enabled"]=false;command["UntilUtcTicks"]=0;channel.Write("control.json",command);}
        }
    }
}
