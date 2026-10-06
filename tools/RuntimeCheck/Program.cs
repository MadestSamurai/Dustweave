using System.Text.Json;
using System.Text.Json.Nodes;
using BD2Daily;
using BD2.LocalIpc;

if(args.Length!=2||args[0] is not ("observe" or "home" or "harbor-cycle" or "stages" or "resume-stages" or "weekly-retry" or "talent-audit" or "npc-query" or "trade-data-audit" or "trade-retry" or "trade-menu-audit" or "cafeteria-recover" or "roulette-single" or "square-crossing" or "square-inspect" or "square-merchant"))throw new ArgumentException("observe|home|harbor-cycle|stages|resume-stages|weekly-retry|talent-audit|npc-query|trade-data-audit|trade-retry|trade-menu-audit|cafeteria-recover <evidence-directory>");
string output=Path.GetFullPath(args[1]);
if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new InvalidOperationException("Evidence directory must be new or empty; previous observations are preserved.");
Directory.CreateDirectory(output);
string root=DailyIdentity.DataRoot;
var host=new DailyGameHost(root);var game=host.Find()??throw new InvalidOperationException("Game not running.");
var dailyPipe=new PipeClient(root,game.ProcessId,game.StartTicks);
var livePipe=new PipeClient(Path.Combine(root,"live"),game.ProcessId,game.StartTicks);
bool stop=false;DailyQueueSession? queue=null;Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stop=true;queue?.Stop();};
JsonObject? Read(PipeClient pipe,string name){var bytes=pipe.Read(name);return bytes==null?null:JsonNode.Parse(bytes)!.AsObject();}
JsonObject Capture(){
    if(host.Find()!=game)throw new InvalidOperationException("Game process changed.");
    var frame=Read(livePipe,"snapshot.json");var identity=Read(dailyPipe,"snapshot.json");
    return new(){["version"]=DailyIdentity.Version,["at"]=DateTimeOffset.UtcNow.ToString("O"),["game"]=JsonSerializer.SerializeToNode(game),
        ["daily"]=identity,["fingerprint"]=dailyPipe.Fingerprint(),["expectedFingerprint"]=BD2Daily.Compatibility.DailyHookCompiler.Fingerprint,["suite"]=Read(new PipeClient(Path.Combine(root,"suite"),game.ProcessId,game.StartTicks),"status.json"),["frame"]=frame,["pending"]=Read(livePipe,"command.json"),["paused"]=livePipe.Read("pause")!=null,
        ["plan"]=frame==null?null:JsonSerializer.SerializeToNode(DailyHomeDecision.Inspect(frame,DailyNavigationPolicy.Load()))};
}
void Report(JsonObject capture){var frame=capture["frame"]?.AsObject();Console.WriteLine(JsonSerializer.Serialize(new{
    process=game.ProcessId,bridge=frame?["BridgeVersion"],scene=frame?["Scene"],error=frame?["Error"],
    surfaces=frame==null?[]:DailyNavigationDecision.Types(frame).Order(StringComparer.Ordinal).ToArray(),plan=capture["plan"],paused=capture["paused"],pending=capture["pending"]!=null}));}
DailyCommandDriver? driver=null; IDisposable? activity=null;
try{
    var before=Capture();DailyJson.Write(Path.Combine(output,"before.json"),before);Report(before);
    if(args[0]=="observe")return;
    if(args[0]=="talent-audit"){
        var entries=new JsonArray();string business=Path.Combine(root,"live","business");
        if(Directory.Exists(business))foreach(string path in Directory.EnumerateFiles(business,"*.json")){
            var op=DailyJson.TryRead<JsonObject>(path);if(op==null||!DailyFieldTalentProof.Owns(op)||op["state"]?.GetValue<string>()!="unknown")continue;
            string commandId=op["command_id"]?.GetValue<string>()??"";if(!GuildStore.ValidId(commandId))continue;
            var receipt=Read(livePipe,"receipts~"+commandId+".json")??DailyJson.TryRead<JsonObject>(Path.Combine(root,"live","steps",commandId,"result.json"))?["receipt"]?.AsObject();
            if(receipt==null)continue;
            entries.Add(new JsonObject{["id"]=op["id"]!.DeepClone(),["scope"]=op["scope"]!.DeepClone(),["legacy_research_rejected"]=DailyFieldTalentProof.LegacyResearchRejected(op,receipt,op["events"]?.AsArray()??new())});
        }
        var audit=new JsonObject{["entries"]=entries,["actions"]=0};DailyJson.Write(Path.Combine(output,"audit.json"),audit);Console.WriteLine(audit.ToJsonString());return;
    }
    var identity=before["daily"]??throw new InvalidOperationException("Identity missing.");
    string account=identity["AccountKey"]?.GetValue<string>()??"";
    var options=new JsonSerializerOptions{IncludeFields=true};
    DailySnapshot? ReadDaily(){var value=Read(dailyPipe,"snapshot.json");return value?.Deserialize<DailySnapshot>(options);}
    var observation=new DailyStageObservation(account,()=>Read(livePipe,"snapshot.json"),ReadDaily,()=>stop,expectedGame:game);
    var bound=await observation.ReadFrameAsync();
    if(args[0]=="trade-data-audit"){
        string package=DailyTools.Unified(AppContext.BaseDirectory)?AppContext.BaseDirectory:Directory.GetParent(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!.FullName;
        var data=await DailyTradeData.PrepareAsync(package,root,game,bound,()=>stop,Console.WriteLine);
        if(!JsonNode.DeepEquals(bound.Context,(await observation.ReadFrameAsync()).Context))throw new InvalidOperationException("Identity changed during read-only trade data audit.");
        DailyJson.Write(Path.Combine(output,"trade-data-proof.json"),new JsonObject{["directory"]=data.Directory,["error"]=data.Error,["proof"]=data.Proof.DeepClone(),["game_input"]=false});
        data.AssertReady();return;
    }
    if(args[0] is "stages" or "resume-stages" or "weekly-retry" or "trade-retry"){
        string package=DailyTools.Unified(AppContext.BaseDirectory)?AppContext.BaseDirectory:Directory.GetParent(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!.FullName;
        if(!File.Exists(Path.Combine(package,"BD2DailyAssistant.exe"))||!DailyTools.Available(package,"live"))throw new InvalidOperationException("Stages verification requires a complete package's diagnostics directory.");
        using var sessions=new AccountSessions();
        if(sessions.Read().CurrentKey!=account)throw new InvalidOperationException("Current saved account differs from the running game; no switch or input.");
        var preferences=new DailyPreferenceStore(root).Read(account);
        var tasks=(args[0]=="trade-retry"?new[]{"trade"}:args[0]=="weekly-retry"?new[]{"weekly_mainline","weekly_npc","weekly_steal"}:new[]{"guild","room","mail"}).Where(t=>DailyStageCatalog.Enabled(t,preferences)).ToArray();
        if(tasks.Length==0)throw new InvalidOperationException("No enabled managed basics selected; settings preserved.");
        queue=new(root,new PackagedDailyQueueExecutor(package));
        QueueRetryRequest? retry=null;
        if(args[0] is "weekly-retry" or "trade-retry"){
            var prior=queue.ReadView(account);
            tasks=tasks.Where(t=>prior.Stages.Any(s=>s.Task==t&&DailyQueueRetry.CanSelect(s))).ToArray();
            if(tasks.Length==0||prior.State=="running"||prior.Record.Length==0)throw new InvalidOperationException("No original unfinished selected stage; no new queue or other tasks started.");
            DailyJson.Write(Path.Combine(output,"original-queue-view.json"),prior);
            retry=new(account,prior.Record,tasks);
        }
        if(args[0]=="resume-stages"){
            var prior=queue.ReadView(account);
            if(prior.Stages.Count==0||prior.Stages.Any(s=>s.Task is not ("guild" or "room" or "mail")))throw new InvalidOperationException("Resume requires the original managed-basics test queue; other queues preserved.");
        }
        using var timeout=new Timer(_=>{stop=true;queue.Stop();},null,TimeSpan.FromMinutes(args[0] is "weekly-retry" or "trade-retry"?15:4),Timeout.InfiniteTimeSpan);
        queue.Progress+=view=>Console.WriteLine(JsonSerializer.Serialize(new{state=view.State,stages=view.Stages}));
        var view=args[0]=="resume-stages"?await queue.RunAsync(account,resume:true):retry!=null?await queue.RunAsync(account,retry:retry):await queue.RunAsync(account,selection:new QueuePlanRequest(account,tasks));
        DailyJson.Write(Path.Combine(output,"queue-view.json"),view);
        if(view.Record.Length>0)File.Copy(view.Record,Path.Combine(output,"queue-record.json"));
        DailyJson.Write(Path.Combine(output,"result.json"),new{state=view.State,selected=tasks,stages=view.Stages,version=DailyIdentity.Version,gameTouched=true,accountSwitched=false,engine="packaged-dotnet-queue"});
        Environment.ExitCode=view.State=="completed"?0:2;return;
    }
    if(args[0]=="cafeteria-recover"){
        if(bound.Frame["Scene"]?.GetValue<string>()!="Map3007_001" || !DailyNavigationDecision.Types(bound.Frame).Contains("CafeteriaFieldDefaultUI"))
            throw new InvalidOperationException("Recovery requires the already loaded cafeteria; no account or cartridge switch.");
        activity=DailyToolControl.Acquire(root);
    }
    if(args[0]=="roulette-single") activity=DailyToolControl.Acquire(root);
    var mailbox=new DailyPipeMailbox(root,game);
    driver=new(root,mailbox,observation.ReadFrameAsync,()=>stop);driver.Bind(bound.Context);driver.Acquire("live");
    if(mailbox.Read("live","command.json")!=null)throw new InvalidOperationException("Existing input pending; preserved.");
    if(args[0]=="harbor-cycle"&&(bound.Frame["Scene"]?.GetValue<string>()!="Map3010_Lobby"||DailyHomeDecision.Inspect(bound.Frame,DailyNavigationPolicy.Load()).Kind!="ready"))
        throw new InvalidOperationException("Harbor verification requires the already observed fishing lobby's ready MenuUI.");
    mailbox.Delete("live","pause");
    if(args[0]=="trade-menu-audit"){
        var evidence=await driver.EvidenceAsync(["mainline.talent_rows","mainline.context","trade.currency","trade.inventory","trade.native","trade.bargain","dispatch.talent"]);DailyJson.Write(Path.Combine(output,"native-talent-evidence.json"),evidence);return;
    }
    var navigation=new DailyStageNavigation(observation.ReadFrameAsync,()=>stop,recoverySeconds:120);int step=0;
    async Task<JsonObject> Relay(string op,string? stage,JsonObject? values){
        var action=values?["action"]?.AsObject();
        Console.WriteLine(JsonSerializer.Serialize(new{operation=op,action}));
        JsonObject result=op switch{
            "navigation_step"=>await driver.NavigationAsync(action??throw new InvalidOperationException("Missing action.")),
            "home_recovery"=>await driver.HomeRecoveryAsync(values?["kind"]?.GetValue<string>()??""),
            _=>throw new InvalidOperationException("Runtime check cannot execute business: "+op)};
        DailyJson.Write(Path.Combine(output,"steps",(++step).ToString("D3")+".json"),new JsonObject{
            ["operation"]=op,["arguments"]=values?.DeepClone(),["result"]=result.DeepClone()});
        return result;
    }
    if(args[0] is "square-crossing" or "square-inspect" or "square-merchant"){
        if(!DailySuite.Enabled)throw new InvalidOperationException("广场诊断必须使用完整日常工具和统一控制锁。");
        var proofs=DailySquare.Proofs().ToArray();
        var business=new DailyManagedBusiness(root,driver,proofs,()=>stop);
        var workflow=new DailyWorkflow(root,AppContext.BaseDirectory,bound.Context,driver,business,navigation,proofs,()=>stop,Relay,Console.WriteLine);
        if(args[0]=="square-merchant")await DailySquareCrossing.Merchant(workflow,output);
        else await DailySquareCrossing.Run(workflow,output,args[0]=="square-inspect");return;
    }
    if(args[0]=="cafeteria-recover"){
        var proofs=DailyCafeteria.Proofs().ToArray();
        var business=new DailyManagedBusiness(root,driver,proofs,()=>stop);
        foreach(var proof in proofs)business.RequireResolved(bound.Context,proof.Role);
        var workflow=new DailyWorkflow(root,AppContext.BaseDirectory,bound.Context,driver,business,navigation,proofs,()=>stop,Relay,Console.WriteLine);
        var cafeteriaResult=await DailyCafeteria.Run(workflow);
        DailyJson.Write(Path.Combine(output,"result.json"),cafeteriaResult);
        Console.WriteLine(cafeteriaResult.ToJsonString());
        Environment.ExitCode=cafeteriaResult["state"]?.GetValue<string>()=="completed"?0:2;return;
    }
    if(args[0]=="roulette-single"){
        var proofs=DailyEventRewards.Proofs().ToArray();
        var business=new DailyManagedBusiness(root,driver,proofs,()=>stop);
        foreach(var proof in proofs)business.RequireResolved(bound.Context,proof.Role);
        var workflow=new DailyWorkflow(root,AppContext.BaseDirectory,bound.Context,driver,business,navigation,proofs,()=>stop,Relay,Console.WriteLine);
        if(!await workflow.Has("EventUI")){
            await workflow.Home("event_rewards");
            await workflow.Step("MenuUI","_buttonEvent",expect:"EventUI");
        }
        var state=await DailyEventRewards.WaitPage(workflow);
        var catalog=DailyEventRewards.Select(DailyEventRewards.Page(state),workflow.Settings.Events)
            .Where(r=>DailyData.N(r["eventType"])==19).ToArray();
        if(catalog.Length!=1)throw new InvalidOperationException("单抽验证要求当前仅有一个已启用转盘，避免选错活动。");
        await workflow.Step("EventUI",operation:"reward_select",value:checked((int)DailyData.N(catalog[0]["id"])));
        var page=DailyEventRewards.Page(await DailyEventRewards.WaitPage(workflow,DailyData.N(catalog[0]["id"])));
        DailyJson.Write(Path.Combine(output,"selected-page.json"),page);
        var singleResult=await DailyEventRewards.SingleRoulette(workflow,page);
        DailyJson.Write(Path.Combine(output,"result.json"),singleResult);
        var afterPage=DailyEventRewards.Page(await workflow.Evidence(DailyEventRewards.Prefixes));
        DailyJson.Write(Path.Combine(output,"after-page.json"),afterPage);
        Console.WriteLine(singleResult.ToJsonString());
        Console.WriteLine(JsonSerializer.Serialize(new{remaining=afterPage["Balance"],nextAction=DailyEventRewards.Action(afterPage),batch=afterPage["Batch"]}));
        return;
    }
    if(args[0]=="harbor-cycle"){
        await Relay("navigation_step",null,new JsonObject{["action"]=new JsonObject{
            ["ui"]="MenuUI",["back"]=true,["absent"]="MenuUI",["reason"]="Return to the already loaded fishing harbor for startup verification"}});
        var harbor=await observation.ReadFrameAsync();
        if(!DailyNavigationDecision.Types(harbor.Frame).Contains("AvatarFishingHarborUI"))throw new InvalidOperationException("Harbor did not return; preserved observed scene.");
        DailyJson.Write(Path.Combine(output,"harbor.json"),harbor.Frame);
    }
    if(args[0]=="npc-query"){
        var current=await driver.ObserveAsync();
        if(DailyNavigationDecision.Types(current.Frame).Contains("MenuUI"))await Relay("navigation_step",null,new JsonObject{["action"]=new JsonObject{["ui"]="MenuUI",["back"]=true,["absent"]="MenuUI",["reason"]="Read NPC progress in the already loaded field"}});
        var field=await driver.ObserveAsync();if(!DailyNavigationDecision.Types(field.Frame).Contains("GameFieldDefaultUI"))throw new InvalidOperationException("NPC read verification requires the existing native field UI; no cartridge or account switch.");
        var state=await driver.EvidenceAsync(["mainline.map"]);int pack=DailyEvidence.Reading(state,"mainline.map","ὮὬὬὮὠὮὪὠὧὩὪ")!["packId"]!.GetValue<int>();
        var query=await driver.SubmitAsync(new(){["ui"]="GameFieldDefaultUI",["operation"]="weekly_npc_query",["value"]=pack,["reason"]="Read global weekly NPC progress without accepting or executing a quest"});
        string proof=Path.Combine(root,"live","weekly-npc-queries",query["id"]!.GetValue<string>()+".json");File.Copy(proof,Path.Combine(output,"native-query-proof.json"));
        var returned=await navigation.RecoverAsync("guild",bound.Context,"read-only NPC query verification",Relay);
        DailyJson.Write(Path.Combine(output,"result.json"),new JsonObject{["pack"]=pack,["query"]=query,["returned"]=returned,["npc_mutations"]=0});
        if(returned["safe"]?.GetValue<bool>()!=true)Environment.ExitCode=2;return;
    }
    var result=await navigation.RecoverAsync("guild",bound.Context,"explicit startup runtime verification",Relay);
    DailyJson.Write(Path.Combine(output,"result.json"),result);Console.WriteLine(result.ToJsonString());
    if(result["safe"]?.GetValue<bool>()!=true)Environment.ExitCode=2;
}catch(Exception error){DailyJson.Write(Path.Combine(output,"error.json"),new{kind=error is StageHostException stage?stage.Kind:"runtime",error=error.ToString(),at=DateTimeOffset.UtcNow});Console.Error.WriteLine(error.Message);Environment.ExitCode=1;}
finally{
    if(args[0] is "square-crossing" or "square-inspect" or "square-merchant")try{livePipe.Write("pause",[]);}catch{}
    driver?.Dispose(); activity?.Dispose();
    try{var after=Capture();DailyJson.Write(Path.Combine(output,"after.json"),after);Report(after);}catch(Exception error){DailyJson.Write(Path.Combine(output,"after-error.json"),new{error=error.Message});}
}

