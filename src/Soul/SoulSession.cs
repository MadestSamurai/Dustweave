using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dustweave.Soul;

public sealed record SoulStatus(string State,string Message,string Player,int Dungeon,int Wave,int WaveCount,int Life,int Coins,int Completed,int Wins,int Losses,double Seconds,string Folder,double? SimulatedWinRate=null,int PlannedUnits=0,bool RiskyPlan=false,int Attempt=1,string DecisionPhase="",int EvaluatedPlans=0,double DecisionSeconds=0,int BuyCount=0,int UpgradeCount=0,int PlannedSpend=0,int CoinsAfter=0,int RefreshCount=0,int RefreshPrice=0,int[]? CompletedDungeons=null,int[]? Dungeons=null,int ProposedCandidates=0,int CandidateTarget=0,int SearchWorkers=0);

public sealed class SoulSession
{
    readonly string root;
    readonly DailyGameHost host;
    CancellationTokenSource? planning;
    volatile bool pause;
    Task? running;
    readonly object sync=new();
    string folder="",player="";
    SoulCampaign campaign=new();
    JsonObject? latest;
    JsonObject? decision;
    string decisionPhase="";int evaluatedPlans;double decisionSeconds;
    SoulSolverSettings solverSettings;
    int proposedCandidates,candidateTarget,searchWorkers;
    readonly Stopwatch timer=new();
    public bool Running => running is { IsCompleted:false };
    public event Action<SoulStatus>? Changed;
    public SoulStatus Status { get; private set; }=new("ready","", "",0,0,0,0,0,0,0,0,0,"");
    public SoulSession(string root){this.root=root;host=new DailyGameHost(root);solverSettings=SoulSolverSettings.Load(root);}
    public SoulSolverSettings Settings => Volatile.Read(ref solverSettings).Normalize();
    public void Configure(SoulSolverSettings settings){var next=settings.Normalize();next.Save(root);Volatile.Write(ref solverSettings,next);}
    static int N(JsonNode? node)=>node is null?0:int.Parse(node.ToString());
    void Report(string state,string message="")
    {
        int d=N(latest?["play"]?["dungeonId"]),w=N(latest?["play"]?["stageId"]);
        if(state is "defeated" or "retrying"&&d==0){d=campaign.LastDungeon;w=campaign.LastWave;}
        int index=Array.IndexOf(campaign.Dungeons,d);
        Status=new(state,message,player,d,w,index>=0?campaign.Waves[index]:0,N(latest?["play"]?["life"]),N(latest?["play"]?["coin"]),campaign.Completed.Count,campaign.Wins,campaign.Losses,timer.Elapsed.TotalSeconds,folder,
            decision?["recommendation"]?["battle"]?["winRate"]?.GetValue<double>(),N(decision?["recommendation"]?["deployedUnits"]),decision?["status"]?.ToString()=="best_available",1+campaign.FailedAttempts.GetValueOrDefault(d),decisionPhase,evaluatedPlans,decisionSeconds,
            decision?["recommendation"]?["path"]?.AsArray().Count(p=>p?["kind"]?.ToString()=="buy")??0,
            decision?["recommendation"]?["path"]?.AsArray().Where(p=>p?["kind"]?.ToString()=="upgrade").Select(p=>p!["inventoryIndex"]!.ToString()).Distinct().Count()??0,
            N(decision?["recommendation"]?["cost"]),N(decision?["recommendation"]?["coinAfter"]),N(latest?["play"]?["shopReloadCount"]),N(latest?["progress"]?["RefreshPrice"]),campaign.Completed.ToArray(),campaign.Dungeons.ToArray(),proposedCandidates,candidateTarget,searchWorkers);
        if(folder.Length>0){File.WriteAllText(Path.Combine(folder,"status.json"),JsonSerializer.Serialize(Status));File.AppendAllText(Path.Combine(folder,"timeline.jsonl"),JsonSerializer.Serialize(Status)+"\n");}
        Changed?.Invoke(Status);
    }
    public Task StartAsync(bool resume=false,string? expectedPlayer=null,string? evidenceFolder=null)
    {
        lock(sync){if(Running)throw new InvalidOperationException("Challenge is already running.");pause=false;planning=new();running=Task.Run(()=>Run(resume,expectedPlayer,evidenceFolder));return running;}
    }
    public void Pause(){lock(sync){pause=true;planning?.Cancel();}if(Running)Report("pausing");}
    public async Task StopAsync(){Pause();var task=running;if(task is not null)await task;}
    public static void PrepareOffline(string root,string client,string output){SoulAssets.Prepare(root,client);SoulHookCompiler.Prepare(output).GetAwaiter().GetResult();}
    async Task Run(bool resume,string? expectedPlayer,string? evidenceFolder)
    {
        IDisposable? lease=null;
        GameInstance? game=null;string account="",bridge="";bool attached=false;
        try
        {
            lease=DailyToolControl.Acquire(root);
            timer.Restart();Report("connecting");
            await host.PrepareAccessAsync(m=>Report("connecting",m),planning!.Token);
            game=host.Find()??throw new InvalidOperationException("请先打开游戏。 / Open the game first.");
            await host.ConnectAsync(game,m=>Report("connecting",m),planning.Token);
            DailySnapshot? identity=null;
            for(int i=0;i<40;i++){identity=host.ReadIdentity(game).Snapshot;if(identity is not null&&identity.FrameUtcTicks>DateTime.UtcNow.AddSeconds(-5).Ticks&&identity.AccountKey.Length>0)break;await Task.Delay(250,planning.Token);}
            if(identity is null||identity.FrameUtcTicks<DateTime.UtcNow.AddSeconds(-5).Ticks||identity.AccountKey.Length==0)throw new InvalidOperationException("Game identity is not ready.");
            if(expectedPlayer is not null&&identity.PlayerName!=expectedPlayer)throw new InvalidOperationException("Expected account: "+expectedPlayer);
            account=identity.AccountKey;player=identity.PlayerName;
            await DailySuite.ActivateAsync(host,root,"daily",m=>Report("connecting",m),planning.Token);
            string accountRoot=Path.Combine(root,"tools","soul","runs",account);
            Directory.CreateDirectory(accountRoot);
            string pointer=Path.Combine(root,"tools","soul",account+".json");Directory.CreateDirectory(Path.GetDirectoryName(pointer)!);
            if(resume&&File.Exists(pointer))folder=JsonNode.Parse(File.ReadAllText(pointer))!["folder"]!.GetValue<string>();
            else folder=evidenceFolder is null?Path.Combine(accountRoot,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")):Path.GetFullPath(evidenceFolder);
            if(resume&&evidenceFolder is not null&&!Path.GetFullPath(folder).Equals(Path.GetFullPath(evidenceFolder),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Resume evidence folder differs from the saved challenge.");
            Directory.CreateDirectory(folder);
            if(resume&&File.Exists(Path.Combine(folder,"campaign.json")))campaign=JsonSerializer.Deserialize<SoulCampaign>(File.ReadAllText(Path.Combine(folder,"campaign.json")))!;
            else campaign=new();
            if(campaign.Dungeons.Length>0&&(campaign.AccountKey!=account||campaign.PlayerKey!=identity.PlayerKey))throw new InvalidDataException("Saved challenge belongs to another account.");
            File.WriteAllText(pointer,JsonSerializer.Serialize(new{folder}));
            if(campaign.Status=="defeated"){campaign.RetryDefeatedDungeon();campaign.Save(folder);}
            string managed=Path.Combine(Path.GetDirectoryName(game.Executable)!,Path.GetFileNameWithoutExtension(game.Executable)+"_Data","Managed");
            SoulAssets.Prepare(root,Path.Combine(managed,"Assembly-CSharp.dll"));
            bridge=Path.Combine(folder,"connection");
            Report("connecting");await SoulHookCompiler.Prepare(bridge);planning.Token.ThrowIfCancellationRequested();
            await SoulHookCompiler.Attach(bridge,game,host,account);attached=true;
            var mailbox=new DailyPipeMailbox(root,game);
            var observation=DailyStageObservation.Attach(root,account,()=>pause);
            using var driver=new DailyCommandDriver(root,mailbox,observation.ReadFrameAsync,()=>pause);
            driver.SubmissionGuard=()=>{
                var current=host.ReadIdentity(game).Snapshot;
                SoulResume.CheckIdentity(host.Find()==game,account,identity.PlayerKey,current?.AccountKey,current?.PlayerKey);
            };
            var first=await observation.ReadFrameAsync();await new DailyManagedBootstrap(root,driver,observation.ReadFrameAsync,()=>false).BeginAsync(first.Context);
            string Id()=>DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+"-"+Guid.NewGuid().ToString("N")[..6];
            async Task<JsonObject> Read(){await SoulEvidence.Capture(mailbox,driver,folder,Id());return latest=JsonNode.Parse(File.ReadAllText(Path.Combine(folder,"live-state-latest.json")))!["state"]!.AsObject();}
            async Task Send(JsonObject action){await SoulEvidence.Capture(mailbox,driver,folder,Id(),action:action);await Read();}
            JsonObject Click(string ui,string field)=>new(){["ui"]=ui,["field"]=field,["timeout"]=20,["reason"]="soul-five-dungeon-challenge"};
            JsonArray Guard(JsonObject state)=>new(new[]{"season","dungeonId","stageId","coin"}.Select(k=>(JsonNode)JsonValue.Create(N(state["play"]![k]))!).ToArray());
            JsonObject Action(string kind,JsonArray items,int value=0,string ui="InventoryBattleStageUI")=>new(){["ui"]=ui,["operation"]=kind,["target_id"]=0,["value"]=value,["items"]=items,["timeout"]=20,["reason"]="soul-five-dungeon-challenge"};
            DateTime waiting=DateTime.UtcNow;bool openingCartridge=false,syncGameProgress=resume;DateTime? recoveringUntil=null,activeUntil=null,returningUntil=null;
            while(!pause)
            {
                if(File.Exists(Path.Combine(folder,"pause.request"))){pause=true;break;}
                try
                {
                var frame=(await driver.ObserveAsync()).Frame;var surfaces=frame["Surfaces"]!.AsArray();
                if(returningUntil.HasValue){
                    if(surfaces.Any(s=>s?["Type"]?.ToString()=="InventoryBattleLobbyUI")||!surfaces.Any(s=>s?["Type"]?.ToString()=="InventoryBattleStageUI"))returningUntil=null;
                    else{
                        if(DateTime.UtcNow>=returningUntil)throw new TimeoutException("游戏尚未完成返回关卡列表；等待游戏恢复后点「继续游戏进度」。 / Wait for the dungeon list to finish loading, then Resume game progress.");
                        await Task.Delay(200,planning.Token);continue;
                    }
                }
                if(syncGameProgress&&SoulResume.InBattle(frame)){
                    activeUntil??=DateTime.UtcNow.AddSeconds(240);
                    if(DateTime.UtcNow>=activeUntil)throw new TimeoutException("请等待游戏当前战斗结束后继续。 / Wait for the current battle to finish, then Resume.");
                    Report("battling");await SoulEvidence.Capture(mailbox,driver,folder,Id(),battle:true,waitBattleOnly:true);continue;
                }
                if(campaign.PendingDungeon!=0&&!syncGameProgress){
                    if(SoulResume.InBattle(frame)){
                        activeUntil??=DateTime.UtcNow.AddSeconds(240);
                        if(DateTime.UtcNow>=activeUntil)throw new TimeoutException("游戏战斗仍在进行，请等待结束后点「继续游戏进度」。 / Wait for the current battle to finish, then Resume game progress.");
                        Report("battling");await SoulEvidence.Capture(mailbox,driver,folder,Id(),battle:true,waitBattleOnly:true);continue;
                    }
                    var surface=surfaces.FirstOrDefault(s=>s?["Type"]?.ToString() is "InventoryBattleResultUI")
                        ??surfaces.FirstOrDefault(s=>s?["Type"]?.ToString() is "InventoryBattleLobbyUI" or "InventoryBattleStageUI");
                    if(surface is not null){
                        Report("reconnecting");
                        await Send(Action("soul_progress",new JsonArray(),ui:surface["Type"]!.ToString()));
                        var response=JsonNode.Parse(File.ReadAllText(Path.Combine(folder,"last-progress-response.json")))!.AsObject();
                        var previous=JsonSerializer.SerializeToNode(campaign);
                        var recovered=JsonSerializer.Deserialize<SoulCampaign>(previous!.ToJsonString())!;
                        string settlementFile=Path.Combine(folder,"last-battle-response.json");
                        var settlement=File.Exists(settlementFile)?JsonNode.Parse(File.ReadAllText(settlementFile))!.AsObject():null;
                        string outcome=recovered.Reconcile(response,settlement);
                        if(outcome=="active"){
                            activeUntil??=DateTime.UtcNow.AddSeconds(120);
                            if(DateTime.UtcNow>=activeUntil)throw new TimeoutException("游戏仍显示战斗未结束。请在游戏中完成或恢复当前战斗，再点「继续游戏进度」；已完成关卡保留。 / Finish or resume the active battle in the game, then Resume game progress. Completed dungeons are kept.");
                            if(surface["Type"]!.ToString()=="InventoryBattleLobbyUI"){
                                // Re-enter the game's existing run; do not wait
                                // on the lobby for a battle it has not reopened.
                                var state=latest!;
                                await Send(Action("soul_enter",new JsonArray(N(state["progress"]!["RegularSeason"]),N(state["play"]!["dungeonId"]),N(state["play"]!["stageId"]),N(state["play"]!["coin"])),campaign.PendingDungeon,"InventoryBattleLobbyUI"));
                                waiting=DateTime.UtcNow;continue;
                            }
                            Report("battling");await Task.Delay(2000,planning.Token);continue;
                        }
                        DailyJson.Write(Path.Combine(folder,Id()+"-battle-recovery.json"),new{outcome,previous,response,gameplayReplayed=false});
                        recovered.Save(folder);campaign=recovered;activeUntil=null;recoveringUntil=null;waiting=DateTime.UtcNow;
                        Report(outcome=="current_progress"?"resumed":outcome);
                        // Refresh surfaces after the read; never act on the
                        // frame from before the recovery request.
                        continue;
                    }
                }
                var result=surfaces.SingleOrDefault(s=>s?["Type"]?.ToString()=="InventoryBattleResultUI");
                if(result is not null)
                {
                    var fields=result["Targets"]!.AsArray().Where(t=>t?["Route"]?.ToString()=="ui"&&t["Enabled"]?.GetValue<bool>()==true).Select(t=>t!["Field"]!.ToString()).ToArray();
                    var next=fields.Where(f=>f is "_objNextStageButton" or "_objRetryButton" or "_objExitButton").ToArray();
                    string? field=next.Contains("_objNextStageButton")?"_objNextStageButton":next.Contains("_objRetryButton")?"_objRetryButton":next.Contains("_objExitButton")?"_objExitButton":null;
                    if(field is not null){Report("advancing");await SoulSubmit.Send(driver,Click("InventoryBattleResultUI",field));waiting=DateTime.UtcNow;await Task.Delay(400);continue;}
                }
                else if(surfaces.Any(s=>s?["Type"]?.ToString()=="InventoryBattleLobbyUI"))
                {
                    var state=await Read();
                    if(syncGameProgress){
                        Report("reconnecting");
                        await Send(Action("soul_progress",new JsonArray(),ui:"InventoryBattleLobbyUI"));state=latest!;
                        var response=JsonNode.Parse(File.ReadAllText(Path.Combine(folder,"last-progress-response.json")))!.AsObject();
                        string settlementFile=Path.Combine(folder,"last-battle-response.json");
                        var settled=File.Exists(settlementFile)?JsonNode.Parse(File.ReadAllText(settlementFile))!.AsObject():null;
                        var resumed=campaign.ResumeFromGame(state,response,account,identity.PlayerKey,settled);
                        DailyJson.Write(Path.Combine(folder,Id()+"-game-progress-resume.json"),new{previous=campaign,resumed,response,gameplayReplayed=false});
                        resumed.Save(folder);campaign=resumed;syncGameProgress=false;activeUntil=null;recoveringUntil=null;
                        Report("entering");
                    }
                    if(campaign.Dungeons.Length==0&&N(state["play"]!["dungeonId"])>0){Report("retrying");await SoulRecovery.Restart(mailbox,driver,folder,Id(),freshStart:true);continue;}
                    campaign.Initialize(state,account,identity.PlayerKey);
                    if(campaign.Target==0){campaign.Status="complete";campaign.Save(folder);Report("complete");return;}
                    int active=N(state["play"]!["dungeonId"]);
                    if(active!=0&&active!=campaign.Target)throw new InvalidOperationException("当前已有另一关进度；请先结束该关再从第一关开始。 / Another dungeon is active.");
                    Report("entering");
                    bool confirming=surfaces.Any(s=>s?["Type"]?.ToString()=="MessagePopupUI");
                    await Send(Action(confirming?"soul_confirm_enter":"soul_enter",new JsonArray(N(state["progress"]!["RegularSeason"]),active,N(state["play"]!["stageId"]),N(state["play"]!["coin"])),campaign.Target,confirming?"MessagePopupUI":"InventoryBattleLobbyUI"));
                    campaign.Status="running";campaign.Save(folder);waiting=DateTime.UtcNow;continue;
                }
                else if(surfaces.Any(s=>s?["Type"]?.ToString()=="InventoryBattleStageUI"))
                {
                    if(syncGameProgress){
                        // Native Back preserves the active run and opens the
                        // season list. Unlike GiveUp, it spends/resets nothing.
                        await SoulSubmit.Send(driver,Click("InventoryBattleStageUI","_objBackButton"));waiting=DateTime.UtcNow;returningUntil=waiting.AddSeconds(60);continue;
                    }
                    var state=await Read();
                    if(N(state["play"]!["dungeonId"])==0&&!state["play"]!["isStageEnter"]!.GetValue<bool>()){
                        // The server cleared the run while the old preparation
                        // page was still open. Leave that page, not the campaign.
                        await SoulSubmit.Send(driver,Click("InventoryBattleStageUI","_objBackButton"));waiting=DateTime.UtcNow;returningUntil=waiting.AddSeconds(60);continue;
                    }
                    if(campaign.Dungeons.Length==0){Report("retrying");await SoulRecovery.Restart(mailbox,driver,folder,Id(),freshStart:true);continue;}
                    if(N(state["play"]!["dungeonId"])!=campaign.Target)throw new InvalidOperationException("Dungeon differs from this challenge.");
                    if(state["play"]!["isStageEnter"]!.GetValue<bool>())throw new InvalidOperationException("A battle is already active; wait for its result.");
                    // Stage entry already reads the authoritative shop/inventory.
                    // A second full read replaces protobuf keys retained by live
                    // views and races native shop loading. Wait for that load only.
                    var shopUntil=DateTime.UtcNow.AddSeconds(20);
                    while(state["progress"]!["ShopReady"]?.GetValue<bool>()!=true&&DateTime.UtcNow<shopUntil){await Task.Delay(250);state=await Read();}
                    if(state["progress"]!["ShopReady"]?.GetValue<bool>()!=true)throw new InvalidDataException("Native shop display did not finish loading.");
                    if(state["progress"]!["Views"]!.AsArray().Any(v=>v?["Owner"]?.ToString()=="other"&&v["State"]?.ToString()=="Shop")){
                        await Send(Action("soul_views",Guard(state)));state=latest!;
                        if(state["progress"]!["Views"]!.AsArray().Any(v=>v?["Owner"]?.ToString()=="other"&&v["State"]?.ToString()=="Shop"))throw new InvalidDataException("Unowned shop views remain after native pool recovery.");
                    }
                    var wave=N(state["play"]!["stageId"]);var dungeon=campaign.Target;string planFile=Path.Combine(folder,Id()+"-plan.json");
                    JsonObject? plan=null;
                    for(int attempt=0;!pause;attempt++)
                    {
                        var settings=Settings;
                        candidateTarget=settings.ForChapter(Array.IndexOf(campaign.Dungeons,dungeon));searchWorkers=settings.Workers;
                        proposedCandidates=0;decisionPhase="combinations";evaluatedPlans=0;decisionSeconds=0;
                        decision=null;Report("planning");
                        // Search passes have fixed seeds. Previous wins,
                        // losses and dungeon retries never steer the model.
                        using(var planner=new NativeFastPlanner(state,710231,allowLiquidation:true){Cancellation=planning.Token}){
                            planner.Progress=(phase,count,elapsed)=>{decisionPhase=phase;evaluatedPlans=count;decisionSeconds=elapsed;proposedCandidates=planner.CandidateProposals;Report("planning");};
                            plan=planner.SearchPortfolio(0,null,candidateTarget,settings.WorkerLimit);
                            proposedCandidates=planner.CandidateProposals;
                        }
                        plan["chapterIndex"]=Array.IndexOf(campaign.Dungeons,dungeon);
                        File.WriteAllText(planFile,plan.ToJsonString());
                        if(File.Exists(Path.Combine(folder,"pause.request"))){pause=true;break;}
                        decision=plan;var spending=SoulShopPolicy.Choose(plan,state);
                        File.WriteAllText(Path.Combine(folder,Id()+"-spending.json"),spending.ToJsonString());
                        if(spending["action"]!.ToString()=="fight")break;
                        if(spending["action"]!.ToString()=="secure"){
                            // Secure the complete evaluated improvement before
                            // losing this shop. Receipts and native layout are
                            // verified exactly as for the final battle plan.
                            NativePlanExecutor.UseBestAvailable(plan,state);decision=plan;
                            File.WriteAllText(planFile,plan.ToJsonString());
                            Report("applying");await NativePlanExecutor.Run(mailbox,driver,folder,Id(),planFile);
                            state=await Read();decision=null;
                            // Replan the actual newly owned inventory first;
                            // never reuse synthetic shop indices after a buy.
                            continue;
                        }
                        int price=N(state["progress"]!["RefreshPrice"]),reload=N(state["play"]!["shopReloadCount"]);
                        if(price<=0||price>N(state["play"]!["coin"]))throw new InvalidDataException("Refresh decision has no finite funded transaction");
                        Report("refreshing");var guard=Guard(state);guard.Add(reload);guard.Add(price);int coins=N(state["play"]!["coin"]);
                        await Send(Action("soul_refresh",guard));state=latest!;
                        if(N(state["play"]!["shopReloadCount"])!=reload+1||N(state["play"]!["coin"])!=coins-price)throw new InvalidDataException("Shop refresh outcome differs from its receipt.");
                    }
                    if(pause)break;
                    if(plan is null)throw new InvalidDataException("Search produced no legal candidate");
                    NativePlanExecutor.UseBestAvailable(plan,state);
                    decision=plan;
                    File.WriteAllText(planFile,plan.ToJsonString());
                    Report("applying");await NativePlanExecutor.Run(mailbox,driver,folder,Id(),planFile);
                    if(pause)break;
                    var before=await Read();int life=N(before["play"]!["life"]);
                    campaign.BeginBattle(dungeon,wave,before);campaign.Save(folder);Report("battling");
                    await SoulEvidence.Capture(mailbox,driver,folder,Id(),battle:true);
                    var after=await Read();var evt=JsonNode.Parse(File.ReadAllText(Path.Combine(folder,"last-battle-response.json")))!;
                    var response=JsonNode.Parse(evt["Values"]![0]!["Json"]!.GetValue<string>())!;
                    bool cleared=N(response["isDungeonCleared"])==1;
                    bool won=cleared||N(after["play"]!["stageId"])>wave;
                    if(!won&&N(after["play"]!["life"])>=life)throw new InvalidDataException("Battle result could not be reconciled with progress.");
                    campaign.Settle(won);campaign.Save(folder);Report(won?"won":"lost");waiting=DateTime.UtcNow;
                    if(!won&&N(response["life"])==0&&(N(after["play"]!["life"])==0||N(after["play"]!["dungeonId"])==0)){
                        campaign.Status="defeated";campaign.Save(folder);Report("defeated");
                        campaign.RetryDefeatedDungeon();campaign.Save(folder);decision=null;Report("retrying");
                        // The native result exits to the lobby. Target still
                        // names this dungeon; cleared dungeons are preserved.
                        continue;
                    }
                }
                else
                {
                    if(!openingCartridge&&surfaces.Any(s=>s?["Type"]?.ToString()=="PackListUI")){
                        await SoulSubmit.Send(driver,Action("soul_open",new JsonArray(),3012,"PackListUI"));openingCartridge=true;waiting=DateTime.UtcNow;continue;
                    }
                    var menu=surfaces.SingleOrDefault(s=>s?["Type"]?.ToString()=="MenuUI");
                    var field=surfaces.SingleOrDefault(s=>s?["Type"]?.ToString()=="GameFieldDefaultUI");
                    if(!openingCartridge&&menu is not null&&menu["Targets"]!.AsArray().Any(t=>t?["Field"]?.ToString()=="_buttonPack"&&t["Enabled"]?.GetValue<bool>()==true)){
                        await SoulSubmit.Send(driver,Click("MenuUI","_buttonPack"));waiting=DateTime.UtcNow;await Task.Delay(400);continue;
                    }
                    if(!openingCartridge&&menu is null&&field is not null&&field["Targets"]!.AsArray().Any(t=>t?["Field"]?.ToString()=="_buttonPackList"&&t["Enabled"]?.GetValue<bool>()==true)){
                        await SoulSubmit.Send(driver,Click("GameFieldDefaultUI","_buttonPackList"));waiting=DateTime.UtcNow;await Task.Delay(400);continue;
                    }
                    // Only identify the native Soul entry, never a text/coordinate match.
                    var entries=surfaces.SelectMany(s=>s?["Targets"]?.AsArray()??new()).Where(t=>t?["Enabled"]?.GetValue<bool>()==true&&t["Route"]?.ToString()=="ui"&&t["Field"]?.ToString().Contains("InventoryBattle",StringComparison.OrdinalIgnoreCase)==true).ToArray();
                    if(entries.Length==1){var surface=surfaces.Single(s=>s!["Targets"]!.AsArray().Contains(entries[0]));await SoulSubmit.Send(driver,Click(surface!["Type"]!.ToString(),entries[0]!["Field"]!.ToString()));waiting=DateTime.UtcNow;}
                    else if(DateTime.UtcNow-waiting>TimeSpan.FromSeconds(30))throw new InvalidOperationException("请打开死魂之盘关卡列表后开始。 / Open the Soul dungeon list, then start.");
                }
                await Task.Delay(400);
                recoveringUntil=null;
                }
                catch(Exception error) when(!pause&&SoulResume.Transient(error)){
                    recoveringUntil??=DateTime.UtcNow.AddSeconds(120);
                    if(DateTime.UtcNow>=recoveringUntil)throw new TimeoutException("等待自动重连超时。恢复网络并进入原账号后，点「继续游戏进度」即可重新读取进度；无需从第一关开始。 / Reconnection timed out. Restore the connection and original account, then Resume game progress; you do not need to start over.",error);
                    Report("reconnecting");openingCartridge=false;waiting=DateTime.UtcNow;
                    await Task.Delay(500,planning.Token);
                }
            }
            campaign.Status="paused";campaign.Save(folder);Report("paused");
        }
        catch(OperationCanceledException) when(pause){campaign.Status="paused";if(folder.Length>0)campaign.Save(folder);Report("paused");}
        catch(StageHostException) when(pause){campaign.Status="paused";if(folder.Length>0)campaign.Save(folder);Report("paused");}
        catch(DailyStepException) when(pause){campaign.Status="paused";if(folder.Length>0)campaign.Save(folder);Report("paused");}
        catch(Exception error){campaign.Status="needs_attention";if(folder.Length>0){campaign.Save(folder);File.WriteAllText(Path.Combine(folder,"error.txt"),error.ToString());}Report("error",error.Message);}
        finally
        {
            // Restore the shared component only after command driver/lease disposal.
            if(attached&&game is not null&&host.Find()==game&&host.ReadIdentity(game).Snapshot?.AccountKey==account)
            {
                try{await SoulHookCompiler.Attach(bridge,game,host,account,restore:true);}
                catch(Exception error){if(folder.Length>0)File.WriteAllText(Path.Combine(folder,"restore-error.txt"),error.ToString());Report("error","连接组件恢复失败，请重新连接游戏。 / Reconnect the game. "+error.Message);}
            }
            timer.Stop();lock(sync){planning?.Dispose();planning=null;}lease?.Dispose();
        }
    }
}
