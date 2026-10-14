using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public static class DailySaffron
{
    internal static JsonObject Command(string owner, int pid, long now, int potatoRounds=2, bool processing=true) => O(("OwnerId",owner),("ProcessId",pid),("Enabled",true),("UntilUtcTicks",now+TimeSpan.TicksPerSecond*3),("SaffronPotatoRounds",potatoRounds),("SaffronProcessing",processing),
        ("SaffronDaily",true),("Logging",false),("Mining",false),("Farming",true),("Cooking",processing),("FixedCrop",true),("FixedSeedId",15),("RecipeId",7),
        ("IntervalMs",100),("PlantingBudget",0),("CookingBatch",1000),("UseNavMesh",false),("UseVehicle",false),("DashRecovery",true),
        ("SellThreshold",9900),("CookingSaleBatch",100),("SellCookingBatches",false),("SellCookingForPlanting",false));
    public static async Task<JsonObject> Run(DailyWorkflow w)
    {
        if (!await DailyTravel.Enter(w,2,"square.pack",packType:13,surfaces:["AvatarLifeGameFieldDefaultUI"]))
            return DailyWorkflow.Skipped("领地卡带尚未开放，未使用料理");
        await DailyMiniGameChannel.Connect(w,"connect-territory");
        var channel=new DailyMiniGameChannel("BD2Territory",w.Context["actor"]!.AsArray());
        channel.Acquire();
        return await RunConnected(w,channel.Read,channel.Write);
    }
    internal static async Task<JsonObject> RunConnected(DailyWorkflow w,Func<string,JsonObject> read,Action<string,JsonNode> write)
    {
        long now=w.Driver.UtcTicks;var prior=read("control.json");
        Require(!B(prior["Enabled"])||N(prior["UntilUtcTicks"])<=now,"领地已有运行中的控制器，未接管");
        string owner=Guid.NewGuid().ToString("N");int pid=I(w.Context["actor"]![0]);var command=Command(owner,pid,now,w.Settings.SaffronPotatoRounds,w.Settings.SaffronProcessing);
        JsonObject state=new();double end=w.Time+19*60, unavailableSince=double.NaN, lastProgress=w.Time, naturalWaitEnd=0;long waitStamp=0;string last="",progress="";bool started=false;
        JsonObject Partial(string reason) => O(("state","partial"),("reason",reason),("saffron",state["Saffron"]),("territory",state));
        try
        {
            while(w.Time<end)
            {
                await w.Observe();state=read("latest.json");
                var current=read("control.json");
                if(B(current["Enabled"])&&N(current["UntilUtcTicks"])>w.Driver.UtcTicks&&S(current["OwnerId"])!=owner)
                    throw new StageHostException("identity","领地控制权已改变，未继续操作");
                bool fresh=DailyWeeklyFishing.Fresh(state,pid,w.Driver.UtcTicks);
                if(!fresh||!B(state["Ready"])) {
                    if(double.IsNaN(unavailableSince))unavailableSince=w.Time;
                    if(w.Time-unavailableSince>=8)
                        return Partial(started?"领地画面或连接暂不可用，已保留作物和料理效果；其余日常继续，可稍后重跑此环节":"领地画面或农田数据尚未就绪，未开始种收；其余日常继续");
                    // A visible transition is not a new cycle. Keep the existing
                    // owner alive, but never renew a stale observation's lease.
                    if(fresh&&started){command["UntilUtcTicks"]=w.Driver.UtcTicks+TimeSpan.TicksPerSecond*3;write("control.json",command);}
                    await w.Delay(150);continue;
                }
                unavailableSince=double.NaN;
                if(S(state["OwnerId"])==owner&&S(state["Error"])!="")return Partial("藏红花环节暂缓，已保留当前作物；其余日常继续，详情见诊断记录");
                var cycle=state["Saffron"] as JsonObject;
                if(S(state["OwnerId"])==owner&&cycle!=null)
                {
                    string signature=S(cycle["State"])+"|"+S(cycle["Reason"])+"|"+S(state["Reason"]);
                    if(signature!=last){last=signature;w.Driver.Diagnostics.Event("saffron_phase",O(("cycle",cycle),("reason",state["Reason"])));}
                    if(S(cycle["State"]) is "completed" or "skipped" or "partial")
                        return O(("state",cycle["State"]),("reason",cycle["Reason"]),("saffron",cycle));
                    string next=string.Join("|",S(cycle["State"]),S(cycle["Surveyed"]),S(cycle["Harvested"]),S(cycle["PotatoRounds"]),S(state["GatherReplies"]),S(state["Spent"]),S(state["Cooked"]),S(state["SoldItems"]),S(state["EmptyFields"]));
                    if(next!=progress){progress=next;lastProgress=w.Time;}
                    long remaining=N(cycle["WaitUntil"])-N(cycle["ServerNow"]);
                    if(waitStamp!=N(cycle["WaitUntil"])) {waitStamp=N(cycle["WaitUntil"]);naturalWaitEnd=w.Time+Math.Clamp(remaining/1000.0,0,900);}
                    bool naturalWait=S(cycle["State"]) is "potatoes" or "drain"&&N(cycle["ServerNow"])>0&&remaining is >0 and <=900_000;
                    if(naturalWait&&w.Time<naturalWaitEnd)lastProgress=w.Time;
                }
                if(w.Time-lastProgress>120)return Partial("藏红花种收连续两分钟没有进展，已保留当前作物；其余日常继续，可稍后重跑此环节");
                command["UntilUtcTicks"]=w.Driver.UtcTicks+TimeSpan.TicksPerSecond*3;
                write("control.json",command);started=true;await w.Delay(150);
            }
            return Partial("藏红花环节超时，已停止；其余日常继续");
        }
        catch(IOException error)
        {
            w.Driver.Diagnostics.Event("saffron_connection_unavailable",O(("reason",error.Message)));
            return Partial("藏红花连接暂不可用，已停止新增操作并保留作物；其余日常继续");
        }
        finally
        {
            try
            {
                if(S(read("control.json")["OwnerId"])==owner){command["Enabled"]=false;command["UntilUtcTicks"]=0;write("control.json",command);}
                // Let the runtime release its movement/tool before the next
                // stage navigates away. A missing pipe is bounded by the lease.
                double stopBy=w.Time+3;
                while(started&&w.Time<stopBy)
                {
                    var stopped=read("latest.json");
                    if(DailyWeeklyFishing.Fresh(stopped,pid,w.Driver.UtcTicks)&&!B(stopped["Enabled"]))break;
                    await w.Driver.DelayAsync(TimeSpan.FromMilliseconds(100));
                }
            }
            catch(IOException error){w.Driver.Diagnostics.Event("saffron_stop_unavailable",O(("reason",error.Message)));}
        }
    }
}
