using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;

static class TalentPracticeCases
{
    sealed class Fixture : IDisposable
    {
        public WorkflowHarness H;
        public DailyBargainCharacter Character;
        public DailyTalentPractice Trainer;
        public List<DailyBargainProgress> Progress=[];
        public long Potions = 200, Pack = 1;
        public int Casts, Switches;
        public bool Stop, MissingResponse, NoEffect, Reset = true, Locked, Unsafe;
        public bool? NetworkBusy = false;
        public int? Gain;
        public Action? AfterCast;
        public double LoadSeconds;
        public double LandingAt = double.PositiveInfinity;
        public long Destination;
        public bool IdentityGap;
        public long? AchievementCount;
        public bool CountUse=true;
        public DailyPracticeAchievement Goal=>new(Character.Kind,88,0,4500,AchievementCount??0,true);
        public Fixture(string root, int kind = 12)
        {
            Character = new(10, 44, "Gray", 1, 5, kind * 100 + 5, 1, 0, 0, 3, 1, 5) { Kind = kind };
            H = new(root, [DailyTalentPracticeProof.Definition]);
            H.Frame["Scene"] = "Map0001_001"; H.Page("GameFieldDefaultUI", "_buttonPackList");
            Trainer = new(H.Workflow, () => Stop, _ => { }, Progress.Add);
            H.Readings = () => [
                WorkflowCases.Reading(DailyTalentPracticeProof.ReadId, ("$self", O(("Characters", new[] { Character }), ("Catalyst", Potions), ("NetworkBusy", NetworkBusy),("Achievements",AchievementCount==null?System.Array.Empty<DailyPracticeAchievement>():new[]{Goal})))),
                WorkflowCases.Reading("navigation.pack", (DailyTravel.CurrentPack + ".Id", Pack), (DailyTravel.CurrentPack + ".PackType", 0)),
                WorkflowCases.Reading("mainline.map", (DailyFieldRoute.MapPath, O(("id", Pack == 1 ? 1 : 21), ("packId", Pack)))),
                WorkflowCases.Reading("mainline.reset", ("GetWeeklyResetTime().Ticks", 10000)),
                WorkflowCases.Reading("mainline.travel", ("$self", O(("Pack", Pack), ("Map", Pack == 1 ? 1 : 21), ("Maps", new[] { O(("Id", Pack == 1 ? 1 : 21), ("MapType", Unsafe ? 1 : 0), ("Inside", false), ("Hunting", false), ("EnvironmentalHazard", false)) })))),
                WorkflowCases.Reading("route.quick", ("$self", O(("Targets", new JsonArray())))),
                WorkflowCases.Reading("mainline.owned_packs", ("$items", new[] { O(("Key", 1), ("Value.Id", 1)), O(("Key", Locked ? 9 : 2), ("Value.Id", Locked ? 9 : 2)) })),
                WorkflowCases.Reading("mainline.packs", ("$items", new[] { O(("Id", 1), ("PackType", 0)), O(("Id", 2), ("PackType", 0)) })),
                WorkflowCases.Reading("mainline.talent_rows", ("$self", new[] {
                    O(("Kind", kind), ("Character", 11), ("Group", Character.Group), ("Instance", 21), ("Gate", ""), ("Reason", ""), ("PackRestricted", false)),
                    O(("Kind", kind), ("Character", Character.Character), ("Group", Character.Group), ("Instance", 20), ("Gate", Character.Active ? "talent_wait:duration_active" : ""), ("Reason", ""), ("PackRestricted", false)) }))
            ];
            H.OnCommand = command =>
            {
                H.Time += .01;
                switch (S(command["Kind"]))
                {
                    case "mainline_menu": H.Page("QuickMenuUI"); break;
                    case "back": H.Page("GameFieldDefaultUI", "_buttonPackList"); break;
                    case "mainline_pack":
                        Switches++; Destination = N(command["Value"]);
                        if (LoadSeconds <= 0) Land();
                        else
                        {
                            LandingAt = H.Time + LoadSeconds;
                            H.Frame["Scene"] = "Empty"; H.Page("LoadingUI");
                            if (IdentityGap) H.Frame["Error"] = "Waiting for fresh account identity";
                        }
                        break;
                    case "mainline_talent":
                        if (N(command["Value"]) != 20 || Character.Active) throw new Exception("Wrong character or active talent cast");
                        Casts++; H.Event("dispatch.start", "request");
                        if (NoEffect) { AfterCast?.Invoke(); break; }
                        int gain = Gain ?? Character.PerUse;
                        Character = Character with { Experience = Math.Min(Character.Target, Character.Experience + gain), Active = true }; Potions -= Character.Cost;
                        if(AchievementCount!=null&&CountUse)AchievementCount++;
                        if (!MissingResponse) H.Event("dispatch.start", "response", ("TalentSkillInfo", O(("groupId", Character.Group))), ("AddTalentExp", gain), ("IsSuccess", true));
                        H.Page("GameFieldDefaultUI", "_buttonPackList"); AfterCast?.Invoke(); break;
                    default:
                        if (S(command["UiToken"]) == "GameFieldDefaultUI") H.Page("PackListUI");
                        else if (S(command["UiToken"]) == "MenuUI" && N(command["TargetId"]) == 10) H.Page("GameFieldDefaultUI", "_buttonPackList");
                        else throw new Exception("Unexpected practice command: " + command);
                        break;
                }
            };
            H.OnDelay = () => { if (H.Time >= LandingAt) Land(); };
            H.OnRead = () =>
            {
                if (!IdentityGap || !double.IsFinite(LandingAt)) return;
                // Exercise the real production reader, including its normally
                // shorter standalone timeout, with the virtual loading clock.
                var observation = new DailyStageObservation(S(H.Frame["AccountKey"]),
                    () => { H.Frame["AtUtcTicks"] = H.Ticks; return H.Frame; },
                    () => new DailySnapshot { ProcessId = I(H.Frame["ProcessId"]), ProcessStartTicks = N(H.Frame["ProcessStartTicks"]),
                        AccountKey = S(H.Frame["AccountKey"]), PlayerKey = S(H.Frame["PlayerKey"]), State = "identified", FrameUtcTicks = H.Ticks,
                        Guild = new() { ServerKey = S(H.Context["server"]), CycleKey = S(H.Context["cycle"]) } },
                    stopped: () => Stop, now: () => H.Ticks, timeout: TimeSpan.FromSeconds(20),
                    delay: t => { H.Time += t.TotalSeconds; H.OnDelay?.Invoke(); H.Publish(); return Task.CompletedTask; });
                observation.ReadFrameAsync().GetAwaiter().GetResult();
            };
            H.ChangeEvidence = evidence =>
            {
                if (IdentityGap && double.IsFinite(LandingAt))
                {
                    evidence["Frame"]!["Error"] = "Waiting for fresh account identity";
                    evidence["Frame"]!["AccountKey"] = ""; evidence["Frame"]!["PlayerKey"] = "";
                }
            };
        }
        public void Land()
        {
            Pack = Destination; LandingAt = double.PositiveInfinity;
            if (Reset) Character = Character with { Active = false };
            H.Frame["Error"] = ""; H.Frame["Scene"] = "Map000" + Pack + "_001"; H.Page("GameFieldDefaultUI", "_buttonPackList");
        }
        public void Dispose() => H.Dispose();
    }
    public static async Task Run(string root, List<string> checks)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks.Add(label); }
        await PracticeQueueCases.Run(checks);
        using(var f=new Fixture(Path.Combine(root,"practice-batched-maps")))
        {
            var chars=new[]{f.Character,f.Character with{Character=12,Kind=13},f.Character with{Character=14,Kind=2},f.Character with{Character=16}};
            var baseRead=f.H.Readings;var baseCommand=f.H.OnCommand;
            f.H.Readings=()=>{
                var rows=baseRead();
                rows[0]=WorkflowCases.Reading(DailyTalentPracticeProof.ReadId,("$self",O(("Characters",chars),("Catalyst",f.Potions),("NetworkBusy",false))));
                rows[^1]=WorkflowCases.Reading("mainline.talent_rows",("$self",new[]{O(("Kind",f.Character.Kind),("Character",f.Character.Character),("Group",f.Character.Group),("Instance",20),("Gate",f.Character.Active?"talent_wait:duration_active":""),("Reason",""))}));
                return rows;
            };
            f.H.OnCommand=command=>{
                baseCommand(command);
                if(S(command["Kind"])=="mainline_talent")chars=chars.Select(c=>c.Character==f.Character.Character?f.Character:c.Kind==f.Character.Kind?c with{Active=true}:c).ToArray();
                if(S(command["Kind"])=="mainline_pack")chars=chars.Select(c=>c with{Active=false}).ToArray();
            };
            var jobs=chars.Select(c=>DailyPracticeJob.Create(c.Kind,c.Character,false,c.Level)).ToList();
            Task<DailyPracticeSnapshot> Read()=>DailyTalentPractice.ReadSnapshot(f.H.Workflow);
            var order=new List<long>();
            await DailyPracticeQueue.RunByMap(jobs,Read,(_,_)=>throw new Exception("Unexpected merchant"),async(c,g,reset)=>{
                f.Character=chars.Single(x=>x.Character==c.Character);order.Add(c.Character);
                return await f.Trainer.RunRound(c,g,reset);
            },()=>f.Stop,()=>{});
            Check(jobs.All(j=>j.State=="completed")&&f.Casts==12&&f.Potions==140,"map batch completes all four characters with exact original medicine budget");
            Check(f.Switches==5&&order.Take(3).SequenceEqual(new long[]{10,12,14})&&order.LastIndexOf(10)<order.IndexOf(16),
                "different skills share a map; shared-kind characters keep order: 12 casts use five switches instead of eleven");
        }
        using(var f=new Fixture(Path.Combine(root,"practice-unsent-travel-timeout")))
        {
            int failures=0;
            f.H.Box.BeforeWrite=(_,name)=>
            {
                if(name=="command.json"&&f.Casts==1&&failures++<2)
                    throw new BD2.LocalIpc.PipeConnectTimeoutException("create",name,new TimeoutException());
            };
            Check(await f.Trainer.Run(f.Character)=="bargain.upgrade"&&failures>=2&&f.Casts==3&&f.Switches==2&&f.Potions==185,
                "practice recovers unsent navigation after a successful cast without losing progress or spending extra medicine");
        }
        using(var f=new Fixture(Path.Combine(root,"practice-receipt-timeout")))
        {
            int failures=0;double recoverAt=0;
            f.AfterCast=()=>recoverAt=f.H.Time+2;
            f.H.Box.BeforeRead=(_,name)=>
            {
                if(name.StartsWith("receipts~",StringComparison.Ordinal)&&f.H.Time<recoverAt){failures++;throw new TimeoutException();}
            };
            Check(await f.Trainer.Run(f.Character)=="bargain.upgrade"&&failures>=6&&f.Casts==3&&f.Switches==2&&f.Potions==185,
                "repeated post-cast receipt outages recover through full practice with exactly three casts");
        }
        using(var f=new Fixture(Path.Combine(root,"practice-lost-write-reply")))
        {
            var original=f.H.Box.AfterCommand;
            f.H.Box.AfterCommand=command=>
            {
                original?.Invoke(command);
                if(S(command["Kind"])=="mainline_talent")throw new TimeoutException("Reply lost after skill execution");
            };
            Check(await f.Trainer.Run(f.Character)=="bargain.upgrade"&&f.Casts==3&&f.Switches==2&&f.Potions==185,
                "lost skill submission replies use matching receipts and never replay successful consuming skills");
        }
        using(var f=new Fixture(Path.Combine(root,"loading-pipe-timeout")){LoadSeconds=19}){
            int timedOut=0;
            f.H.Box.BeforeRead=(_,name)=>{
                if(name=="snapshot.json"&&double.IsFinite(f.LandingAt)){timedOut++;throw new TimeoutException("PipeClient.CallOnce snapshot timeout");}
            };
            Check(await f.Trainer.Run(f.Character)=="bargain.upgrade"&&timedOut>0&&f.Casts==3&&f.Switches==2&&f.Potions==185,
                "19-second loading pipe outage resumes full practice with no duplicate cartridge entry or extra pill consumption");
        }
        using(var f=new Fixture(Path.Combine(root,"queue-budget-xp-progress")){AchievementCount=4490}){
            await f.Trainer.Run(f.Character);
            Check(f.Progress.First().Achievements?.Single().Current==4490&&f.Progress.Last().Achievements?.Single().Current==4493&&f.Progress.Last().Achievement==null&&f.Progress.Last().Spent==15,
                "field XP progress carries shared achievement counts and actual spending for queue totals");
        }
        foreach(string scenario in new[]{"max-level","partial","delayed","lost-response","pause"})
        using(var f=new Fixture(Path.Combine(root,"achievement-"+scenario)) {AchievementCount=4498}){
            f.Character=f.Character with{Experience=3,Level=5,MaxLevel=5,PerUse=0};f.Gain=0;
            if(scenario=="partial")f.Character=f.Character with{Level=1,Experience=2,PerUse=1};
            if(scenario=="lost-response")f.MissingResponse=true;
            if(scenario=="delayed"){
                f.CountUse=false;double incrementAt=double.PositiveInfinity;
                f.AfterCast=()=>incrementAt=f.H.Time+1;
                f.H.OnDelay=()=>{if(f.H.Time>=incrementAt){f.AchievementCount++;incrementAt=double.PositiveInfinity;}};
            }
            if(scenario=="pause")f.AfterCast=()=>f.Stop=true;
            string result=await f.Trainer.Run(f.Character,achievement:f.Goal);
            if(scenario=="pause"){
                Check(result=="bargain.stopped"&&f.Casts==1,"achievement pause settles one cast");
                f.Stop=false;f.AfterCast=null;
                result=await f.Trainer.Run(f.Character,achievement:f.Goal);
            }
            Check(result=="practice.achievement_complete"&&f.Casts==2&&f.AchievementCount==4500&&f.Potions==190,"achievement highest tier completes exact remaining uses: "+scenario);
            Check(await f.Trainer.Run(f.Character,achievement:f.Goal)=="practice.achievement_complete"&&f.Casts==2,"completed achievement never spends again: "+scenario);
        }
        using(var f=new Fixture(Path.Combine(root,"achievement-stalled")){AchievementCount=4499,CountUse=false}){
            f.Character=f.Character with{Experience=3};
            try{await f.Trainer.Run(f.Character,achievement:f.Goal);throw new Exception("Expected stagnant achievement pause");}
            catch(DailyBargainFault e){Check(e.Code=="practice.achievement_stalled"&&f.Casts==1,"stale achievement counter stops repeated spending");}
            f.AchievementCount=4500;
            Check(await f.Trainer.Run(f.Character,achievement:f.Goal)=="practice.achievement_complete"&&f.Casts==1,"later authoritative count resumes without duplicate use");
        }
        // Display language is metadata, not identity or an execution prerequisite.
        using (var f = new Fixture(Path.Combine(root, "practice-game-names"), 17))
        {
            var row = JsonSerializerNode(f.Character);
            row["Names"] = O(("Cn", "游戏简体名"), ("Tw", "遊戲繁體名"), ("En", "Client English name"));
            var parsed = DailyBargainCharacter.Parse(row);
            Check(parsed.NameIn("zh-CN") == "游戏简体名" && parsed.NameIn("zh-TW") == "遊戲繁體名" && parsed.NameIn("en-US") == "Client English name",
                "practice names select the game's corresponding language columns");
            parsed.AssertSameTier(f.Character);
            row.Remove("Names");
            Check(DailyBargainCharacter.Parse(row).NameIn("en-US") == "Gray", "missing display metadata preserves the game's readable name");
            row["Name"] = "";
            Check(DailyBargainCharacter.Parse(row).NameIn("en-US") == "#44", "missing names keep identity readable without blocking practice");
            var title = O(("Kind", 17), ("Names", O(("Cn", "游戏藏身"), ("Tw", "遊戲藏身"), ("En", "Client stealth"))));
            var reading = WorkflowCases.Reading(DailyTalentPracticeProof.ReadId, ("$self", O(("Talents", new[] { title }))));
            var evidence = O(("Readings", new[] { reading }));
            Check(DailyTalentPracticeProof.Talents(evidence).Single() is { Kind: 17, Names.En: "Client stealth" }, "talent titles read game metadata rather than translated class labels");
            Check(DailyTalentPracticeProof.Talents(await f.H.Workflow.Evidence(DailyTalentPracticeProof.ReadId)).Length == 0, "missing optional talent titles do not block reading XP");
        }
        foreach (int kind in new[] { 2, 6, 12, 13, 17 })
        using (var f = new Fixture(Path.Combine(root, "practice-kind-" + kind), kind))
        {
            Check(await f.Trainer.Run(f.Character) == "bargain.upgrade", "field practice fills tier: " + kind);
            Check(f.Casts == 3 && f.Switches == 2 && f.Potions == 185, "one cast per reset, no extra final switch: " + kind);
            Check(f.Character.Experience == 3 && f.H.Business.Records(f.H.Context).All(o => !DailyManagedBusiness.Pending(o)), "settled XP and journal: " + kind);
        }
        using (var f = new Fixture(Path.Combine(root, "practice-stop")))
        {
            f.AfterCast = () => f.Stop = true;
            Check(await f.Trainer.Run(f.Character) == "bargain.stopped" && f.Casts == 1 && f.Switches == 0, "stop settles cast without further travel");
        }
        using (var f = new Fixture(Path.Combine(root, "practice-limited")))
            Check(await f.Trainer.Run(f.Character, 2) == "practice.test_complete" && f.Casts == 2 && f.Switches == 1, "live verification round limit");
        using (var f = new Fixture(Path.Combine(root, "practice-menu-start")))
        {
            f.H.Page("MenuUI");
            var readings = f.H.Readings;
            f.H.Readings = () => readings().Where(r => !DailyNavigationDecision.Types(f.H.Frame).Contains("MenuUI") || S(r["Id"]) != "mainline.map").ToArray();
            Check(await f.Trainer.Run(f.Character, 1) == "practice.test_complete" && f.Casts == 1,
                "main-menu start does not require an already-loaded field map");
        }
        foreach (double load in new[] { 65d, 120d })
        using (var f = new Fixture(Path.Combine(root, "practice-slow-map-" + load)) { LoadSeconds = load })
        {
            Check(await f.Trainer.Run(f.Character) == "bargain.upgrade", "slow map loading completes practice: " + load);
            Check(f.Casts == 3 && f.Switches == 2 && f.Potions == 185, "slow loading does not replay entry or cast: " + load);
            Check(f.H.Time < load * 2 + 15, "ready maps resume promptly without a fixed delay: " + load);
        }
        using (var f = new Fixture(Path.Combine(root, "practice-slow-identity")) { LoadSeconds = 90, IdentityGap = true })
            Check(await f.Trainer.Run(f.Character, 2) == "practice.test_complete" && f.Casts == 2 && f.Switches == 1 && f.Potions == 190,
                "switch identity refresh inherits loading budget rather than failing after 20 seconds");
        foreach (string scenario in new[] { "timeout", "cancel", "identity-cancel" })
        using (var f = new Fixture(Path.Combine(root, "practice-load-recovery-" + scenario)) { LoadSeconds = 240, IdentityGap = scenario == "identity-cancel" })
        {
            var approved = f.Character;
            var delayed = f.H.OnDelay;
            if (scenario != "timeout") f.H.OnDelay = () =>
            {
                delayed?.Invoke();
                if (double.IsFinite(f.LandingAt) && f.H.Time >= f.LandingAt - 235) f.Stop = f.H.Stopped = true;
            };
            Exception? failure = null;
            try { await f.Trainer.Run(approved); } catch (Exception error) { failure = error; }
            Check(failure != null && f.Casts == 1 && f.Switches == 1 && f.Potions == 195,
                "incomplete loading never spends another cast or resends entry: " + scenario);
            if (scenario == "timeout")
                Check(failure is DailyBargainFault { Code: "practice.travel_timeout" } && f.H.Time is >= 180 and < 190,
                    "one loading deadline has a bounded actionable timeout");
            else Check(f.H.Time < 15, "stop interrupts loading and identity wait promptly: " + scenario);
            f.Stop = f.H.Stopped = false; f.LoadSeconds = 0; f.H.OnDelay = null; f.Land();
            Check(await f.Trainer.Run(f.Character, 1) == "practice.test_complete" && f.Casts == 2 && f.Switches == 1 && f.Potions == 190,
                "continue adopts the arrived safe map without an extra switch: " + scenario);
            Check(await f.Trainer.Run(f.Character) == "bargain.upgrade" && f.Casts == 3 && f.Potions == 185,
                "repeated continuation finishes remaining experience: " + scenario);
        }
        using (var f = new Fixture(Path.Combine(root, "practice-wrong-actor-during-load")) { LoadSeconds = 120 })
        {
            f.H.OnDelay = () => { if (double.IsFinite(f.LandingAt)) f.H.Context["actor"]![3] = new string('c', 64); };
            try { await f.Trainer.Run(f.Character); throw new Exception("Expected identity boundary"); }
            catch (DailyStepException error) when (error.Kind == "identity")
            { Check(f.Casts == 1 && f.Potions == 195, "loading grace never adopts a different account"); }
        }
        using (var f = new Fixture(Path.Combine(root, "practice-partial-final-gain"), 13))
        {
            f.Character = f.Character with { PerUse = 2 };
            Check(await f.Trainer.Run(f.Character) == "bargain.upgrade" && f.Casts == 2 && f.Character.Experience == 3 && f.Potions == 190,
                "final partial XP consumes exactly one normal cast then stops");
        }
        using (var f = new Fixture(Path.Combine(root, "practice-no-xp")) { Gain = 0 })
            Check(await f.Trainer.Run(f.Character) == "bargain.no_growth" && f.Casts == 1 && f.Switches == 0, "no XP stops before repeating travel");
        using (var f = new Fixture(Path.Combine(root, "practice-no-reset")) { Reset = false })
        {
            try { await f.Trainer.Run(f.Character); throw new Exception("Expected reset failure"); }
            catch (DailyBargainFault e) { Check(e.Code == "practice.effect_not_reset" && f.Casts == 1 && f.Switches <= 2, "missing reset has bounded exit without repeat cast"); }
            f.Reset = true;
            Check(await f.Trainer.Run(f.Character) == "bargain.upgrade" && f.Casts == 3, "reset recovery continues current XP");
        }
        foreach (string scenario in new[] { "locked", "unsafe", "short", "full" })
        using (var f = new Fixture(Path.Combine(root, "practice-" + scenario)))
        {
            f.Locked = scenario == "locked"; f.Unsafe = scenario == "unsafe";
            if (scenario == "short") f.Potions = 1;
            if (scenario == "full") f.Character = f.Character with { Experience = 3 };
            try { await f.Trainer.Run(f.Character); }
            catch (DailyBargainFault) { }
            Check(f.Casts == 0, "practice no waste: " + scenario);
        }
        foreach (string recovery in new[] { "same-session", "component-reloaded", "game-restarted" })
        using (var f = new Fixture(Path.Combine(root, "practice-lost-receipt-" + recovery)) { MissingResponse = true })
        {
            f.AfterCast = () => f.Stop = true;
            try { await f.Trainer.Run(f.Character); } catch (StageHostException) { }
            Check(f.Casts == 1, "lost receipt is not permission to repeat cast");
            f.MissingResponse = false;
            f.Stop = false; f.AfterCast = null;
            var pending = f.H.Business.Records(f.H.Context).Single(DailyManagedBusiness.Pending);
            if (recovery == "game-restarted") pending["before"]!["Frame"]!["ProcessStartTicks"] = 99;
            if (recovery == "component-reloaded") pending["before"]!["Frame"]!["Instance"] = "old-instance";
            f.H.Business.Save(pending);
            Check(await f.Trainer.Run(f.Character) == "bargain.upgrade" && f.Casts == 3, "current XP resumes without replaying lost request: " + recovery);
        }
        using (var f = new Fixture(Path.Combine(root, "practice-lost-receipt-in-run")) { MissingResponse = true })
        {
            f.AfterCast = () => f.MissingResponse = false;
            Check(await f.Trainer.Run(f.Character) == "bargain.upgrade" && f.Casts == 3 && f.Potions == 185,
                "missing receipt with real XP continues in the same run without an extra cast");
        }
        foreach (string recovery in new[] { "in-run", "retry", "reload", "restart" })
        using (var f = new Fixture(Path.Combine(root, "practice-no-effect-" + recovery)) { NoEffect = true })
        {
            var approved = f.Character;
            f.AfterCast = () => { f.NoEffect = false; f.Stop = recovery != "in-run"; };
            var result = await f.Trainer.Run(approved);
            if (recovery != "in-run")
            {
                Check(result == "bargain.stopped" && f.Casts == 1 && f.Potions == 200, "cancel preserves unconfirmed attempt: " + recovery);
                var pending = f.H.Business.Records(f.H.Context).Single(DailyManagedBusiness.Pending);
                if (recovery == "reload") pending["before"]!["Frame"]!["Instance"] = "retired-component";
                if (recovery == "restart") pending["before"]!["Frame"]!["ProcessStartTicks"] = 99;
                f.H.Business.Save(pending);
                f.Stop = false; f.AfterCast = null;
                result = await f.Trainer.Run(approved);
            }
            Check(result == "bargain.upgrade" && f.Casts == 4 && f.Potions == 185, "no-effect attempt recovers from native idle and completes: " + recovery);
            Check(!f.H.Business.Records(f.H.Context).Any(DailyManagedBusiness.Pending), "no stale lock after recovery: " + recovery);
            Check(await f.Trainer.Run(f.Character) == "bargain.upgrade" && f.Casts == 4, "repeated continue preserves completion: " + recovery);
        }
        foreach (bool? busy in new bool?[] { true, null })
        using (var f = new Fixture(Path.Combine(root, "practice-network-" + busy)) { NoEffect = true, NetworkBusy = busy })
        {
            try { await f.Trainer.Run(f.Character); throw new Exception("Expected pending request"); }
            catch (DailyBargainFault error) { Check(error.Code == "practice.pending" && f.Casts == 1, "busy or unreadable network never permits another consuming action"); }
            f.NetworkBusy = false; f.NoEffect = false;
            Check(await f.Trainer.Run(f.Character) == "bargain.upgrade" && f.Casts == 4, "network recovery continues without journal deletion");
        }
        using (var f = new Fixture(Path.Combine(root, "practice-repeated-no-effect")) { NoEffect = true })
        {
            try { await f.Trainer.Run(f.Character); throw new Exception("Expected bounded recovery"); }
            catch (DailyBargainFault error) { Check(error.Code == "practice.retry_exhausted" && f.Casts == 2 && f.Potions == 200, "repeated no-effect attempts stop after two, without spending medicine"); }
            f.NoEffect = false;
            Check(await f.Trainer.Run(f.Character) == "bargain.upgrade" && f.Casts == 5, "retry limit does not persist a permanent block");
        }
        using (var f = new Fixture(Path.Combine(root, "practice-quiet-window")) { NoEffect = true })
        {
            f.AfterCast = () => f.Stop = true;
            await f.Trainer.Run(f.Character);
            f.Stop = false; f.AfterCast = null;
            int waits = 0; double start = f.H.Time;
            f.H.OnDelay = () => { waits++; f.NetworkBusy = waits == 4; };
            await f.Trainer.Run(f.Character, 1);
            Check(f.H.Time - start >= 3 && f.Casts == 2, "intervening network activity resets native idle window; attempt cap includes no-effect calls");
        }
        using (var f = new Fixture(Path.Combine(root, "practice-persisted-unchanged"), 17))
        {
            f.Character = f.Character with { Experience = 449, Previous = 85, Target = 487, PerUse = 4, Level = 4 };
            f.Potions = 99490;
            var before = await f.H.Workflow.Evidence(DailyTalentPracticeProof.ReadId);
            var pending = f.H.Business.Create(f.H.Context, DailyTalentPracticeProof.Role, before,
                O(("character", f.Character.Character), ("group", f.Character.Group), ("kind", 17)),
                O(("ui", "QuickMenuUI"), ("operation", "mainline_talent"), ("value", 20)));
            pending["state"] = "unknown"; f.H.Business.Save(pending); f.H.Time = 45;
            var after = await f.H.Workflow.Evidence(DailyTalentPracticeProof.ReadId);
            Check(DailyTalentPracticeProof.IdleUnchanged(pending, after) != null, "unchanged native state can resolve the saved no-effect attempt");
            after["Frame"]!["AccountKey"] = "another-account";
            Check(DailyTalentPracticeProof.IdleUnchanged(pending, after) == null, "another account cannot retire the pending cast");
            f.Potions--;
            Check(DailyTalentPracticeProof.IdleUnchanged(pending, await f.H.Workflow.Evidence(DailyTalentPracticeProof.ReadId)) == null, "unexplained consumption is not treated as an unchanged no-effect attempt");
            f.Potions++;
            long firstRead = f.H.Ticks;
            f.H.ChangeEvidence = e => e["AtUtcTicks"] = firstRead + (long)((f.H.Time - 45) / 3) * TimeSpan.FromSeconds(3).Ticks;
            Check(await f.Trainer.Run(f.Character, 2) == "practice.test_complete" && f.Casts == 2 && f.Character.Experience == 457 && f.Potions == 99480,
                "cached reads do not reset recovery; saved unknown attempt resumes from 449 XP for exactly two new authorized casts");
            var saved = f.H.Business.Records(f.H.Context).Single(o => S(o["id"]) == S(pending["id"]));
            Check(S(saved["state"]) == "superseded" && !B(saved["reconciliation"]?["original_success_claimed"])
                && S(saved["reconciliation"]?["method"]) == "native_idle_unchanged_training_state", "recovery preserves unknown original outcome and records native idle evidence");
        }
        using (var f = new Fixture(Path.Combine(root, "practice-stale-observation")) { NoEffect = true })
        {
            f.AfterCast = () => f.Stop = true;
            await f.Trainer.Run(f.Character);
            f.Stop = false; f.AfterCast = null; long frozen = f.H.Ticks;
            f.H.ChangeEvidence = e => e["AtUtcTicks"] = frozen;
            try { await f.Trainer.Run(f.Character); throw new Exception("Expected fresh evidence requirement"); }
            catch (StageHostException error) when (error.ObservationTimedOut)
            { Check(f.Casts == 1 && f.Potions == 200, "one stale idle snapshot never authorizes another cast; the shared fresh-data wait pauses without consumption"); }
            f.H.ChangeEvidence = null; f.NoEffect = false;
            Check(await f.Trainer.Run(f.Character) == "bargain.upgrade" && f.Casts == 4, "restored fresh evidence continues after stale observation");
        }
        using (var f = new Fixture(Path.Combine(root, "practice-progress-with-refill")) { MissingResponse = true })
        {
            f.AfterCast = () => f.Stop = true;
            await f.Trainer.Run(f.Character);
            f.Potions += 40; f.Stop = false; f.AfterCast = null; f.MissingResponse = false;
            Check(await f.Trainer.Run(f.Character) == "bargain.upgrade" && f.Casts == 3 && f.Potions == 225,
                "authoritative XP progress remains usable after a separate potion refill");
        }
    }
    static JsonObject JsonSerializerNode(DailyBargainCharacter c) => System.Text.Json.JsonSerializer.SerializeToNode(c)!.AsObject();
}
