using Dustweave;
static class PracticeQueueCases
{
    public static async Task Run(List<string> checks)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add(label);}
        var a=new DailyBargainCharacter(10,44,"A",1,5,1205,1,0,0,10,1,5){Kind=12};
        var b=a with{Character=11,Cost=2,Experience=10};
        var c=a with{Character=12,Kind=13,Cost=1};
        var goal=new DailyPracticeAchievement(12,88,0,4500,4498,true);
        var xpA=DailyPracticeJob.Create(12,10,false);
        var ach=DailyPracticeJob.Create(12,0,true);
        var bound=DailyPracticeJob.Create(12,10,false,1);
        var legacy=System.Text.Json.JsonSerializer.Deserialize<DailyPracticeJob>("{\"Id\":\"old\",\"Kind\":12,\"Character\":10,\"Achievement\":false}")!;
        Check(legacy.Level==null,"legacy queue without level still loads");
        var keep=DailyPracticeJob.Create(13,12,false,1);
        var levels=new List<DailyPracticeJob>{bound,ach,bound with{State="completed",Id="done"},keep};
        var removed=DailyPracticeQueue.RefreshLevels(levels,[a with{Level=2},b,c]);
        Check(removed.Select(j=>j.Id).SequenceEqual(new[]{bound.Id,"done"})&&levels.Select(j=>j.Id).SequenceEqual(new[]{ach.Id,keep.Id}),
            "refresh removes changed XP levels including completed items and preserves remaining order and achievement");
        levels=[bound];
        Check(DailyPracticeQueue.RefreshLevels(levels,[a with{Experience=10}]).Length==0&&levels.Count==1,"same-level XP completion does not remove the costume");
        Check(DailyPracticeQueue.RefreshLevels(levels,[]).Length==0&&levels.Count==1,"missing character observation never deletes queued work");
        Check(DailyPracticeQueue.RefreshLevels(levels,[a with{Level=0}]).Length==0&&levels.Count==1,"unknown level cannot be treated as changed");
        Check(DailyPracticeQueue.RefreshLevels(levels,[a with{Kind=13,Level=2}]).Length==0,"level comparison matches both costume and skill identity");
        levels=[bound with{Level=2}];
        Check(DailyPracticeQueue.RefreshLevels(levels,[a]).Length==1&&levels.Count==0,"any confirmed level change invalidates the selected tier");
        levels=[legacy];
        Check(DailyPracticeQueue.RefreshLevels(levels,[a with{Level=2}],[a]).Length==1,"legacy queue compares previous live level when available");
        levels=[legacy];
        Check(DailyPracticeQueue.RefreshLevels(levels,[a]).Length==0&&levels[0].Level==1,"legacy queue adopts first known level without guessing old history");
        levels=System.Text.Json.JsonSerializer.Deserialize<List<DailyPracticeJob>>(System.Text.Json.JsonSerializer.Serialize(levels))!;
        Check(DailyPracticeQueue.RefreshLevels(levels,[a with{Level=2}]).Length==1,"persisted baseline detects upgrades across reopening");
        levels=[DailyPracticeJob.Create(12,10,true,1)];
        Check(levels[0].Level==null&&DailyPracticeQueue.RefreshLevels(levels,[a with{Level=2}]).Length==0,"achievement target remains skill-wide when cheapest costume upgrades");
        DailyPracticeBudget Budget(DailyPracticeJob[] list,DailyBargainCharacter[] chars,DailyPracticeAchievement[] goals)
            =>DailyPracticeQueue.Estimate(list,new(chars,100,goals));
        Check(Budget([xpA,DailyPracticeJob.Create(13,12,false)],[a,c],[])==new DailyPracticeBudget(60,20,0),"queue sums different skill costs independently of selected UI item");
        Check(Budget([xpA,xpA],[a],[])==new DailyPracticeBudget(50,10,0),"queue does not charge twice for repeated XP targets");
        Check(Budget([xpA,ach],[a,b],[goal])==new DailyPracticeBudget(50,10,0),"XP uses already satisfy following achievement goal");
        Check(Budget([ach,xpA],[a,b],[goal])==new DailyPracticeBudget(54,12,0),"achievement on cheaper other costume preserves later XP cost");
        Check(Budget([ach,xpA],[a],[goal])==new DailyPracticeBudget(50,10,0),"achievement XP contributes to later training on the same costume");
        Check(Budget([xpA,ach],[a,b],[goal with{Current=4485}])==new DailyPracticeBudget(60,15,0),"mixed queue charges only outstanding uses with the later cheapest costume");
        Check(Budget([xpA,ach],[a with{Experience=4},b],[goal with{Current=4489}])==new DailyPracticeBudget(40,11,0),"live progress reduces both XP and shared achievement remaining estimate");
        Check(Budget([xpA with{State="completed"},ach],[a,b],[goal])==new DailyPracticeBudget(4,2,0),"completed queue items are excluded on resume");
        Check(Budget([ach],[a,b],[goal with{Known=false}])==new DailyPracticeBudget(0,0,1),"missing achievement counter remains unknown rather than free");
        Check(Budget([xpA,ach],[],[goal])==new DailyPracticeBudget(0,0,2),"unread inventory cannot produce a zero-cost complete estimate");
        Check(Budget([ach],[],[goal with{Current=4500}])==new DailyPracticeBudget(0,0,0),"completed achievement costs nothing even without an owned costume");
        Check(Budget([xpA],[a with{PerUse=0}],[])==new DailyPracticeBudget(0,0,1),"unknown XP gain remains unestimated");
        Check(Budget([xpA],[a with{Experience=9,PerUse=3}],[])==new DailyPracticeBudget(5,1,0),"partial final XP use rounds up once");
        Check(Budget([ach],[b],[goal with{Target=long.MaxValue,Current=0}]).UnknownItems==1,"oversized estimate is reported unknown without breaking rendering");
        Check(Budget([xpA],[a],[]).Shortage(20)==30&&Budget([xpA],[a],[]).Shortage(100)==0,"queue compares remaining total with current pill stock");
        Check(a.Experience==0&&goal.Current==4498,"projection never mutates authoritative game progress");
        Check(DailyPracticeAchievement.Highest([goal with{Target=500},goal],12)==goal,"achievement selects highest definition, not current claim tier");
        Check(DailyPracticeAchievement.Highest([goal],2)==null,"rush without an achievement is not conflated with another class");
        Check(DailyPracticeQueue.Cheapest([a,b,c],12)==b,"cheapest selection includes capped owned skills and excludes other kinds");
        var jobs=new List<DailyPracticeJob>{DailyPracticeJob.Create(12,10,false),DailyPracticeJob.Create(12,0,true),DailyPracticeJob.Create(13,12,false)};
        var called=new List<long>();bool stop=false;int snapshots=0;
        Task<DailyPracticeSnapshot> Read(){snapshots++;return Task.FromResult(new DailyPracticeSnapshot([a,b,c],100,[goal]));}
        async Task<string> Execute(DailyBargainCharacter character,DailyPracticeAchievement? target){await Task.Yield();called.Add(character.Character);if(called.Count==1)stop=true;return target==null?"bargain.upgrade":"practice.achievement_complete";}
        await DailyPracticeQueue.Run(jobs,Read,Execute,()=>stop,()=>{});
        Check(jobs[0].State=="completed"&&jobs[1].State=="pending"&&called.SequenceEqual(new long[]{10}),"queue stop preserves completed and pending entries");
        stop=false;
        await DailyPracticeQueue.Run(jobs,Read,Execute,()=>stop,()=>{});
        Check(jobs.All(j=>j.State=="completed")&&called.SequenceEqual(new long[]{10,11,12})&&snapshots==3,"queue resume refreshes each entry and preserves order without replay");
        jobs=[DailyPracticeJob.Create(12,10,false),DailyPracticeJob.Create(13,12,false)];
        await DailyPracticeQueue.Run(jobs,()=>Task.FromResult(new DailyPracticeSnapshot([a,c],15,[])),(ch,g)=>{called.Add(ch.Character);return Task.FromResult("bargain.upgrade");},()=>false,()=>{});
        Check(jobs[0].State=="blocked"&&jobs[1].State=="completed","unaffordable XP entry does not block affordable independent entry");
        jobs=[DailyPracticeJob.Create(12,0,true)];
        foreach(bool known in new[]{false,true}){
            int executed=0;
            await DailyPracticeQueue.Run(jobs,()=>Task.FromResult(new DailyPracticeSnapshot([a,b],2,[goal with{Known=known}])),(ch,g)=>{executed++;return Task.FromResult("practice.achievement_complete");},()=>false,()=>{});
            Check(known?executed==1&&jobs[0].State=="completed":executed==0&&jobs[0].State=="blocked","unknown count stays recoverable; partial pill budget can start: "+known);
        }
        jobs=[DailyPracticeJob.Create(12,10,false),DailyPracticeJob.Create(13,12,false)];
        try{await DailyPracticeQueue.Run(jobs,Read,(ch,g)=>throw new TimeoutException(),()=>false,()=>{});}catch(TimeoutException){}
        Check(jobs.All(j=>j.State=="pending")&&jobs[0].Message=="practice.queue_retry","uncertain action remains resumable and stops dependent control work");
        stop=false;called.Clear();
        await DailyPracticeQueue.Run(jobs,Read,(ch,g)=>Task.FromResult("bargain.upgrade"),()=>false,()=>{});
        Check(jobs.All(j=>j.State=="completed"),"queue retries after restored conditions");
        jobs=[bound,DailyPracticeJob.Create(13,12,false,1)];called.Clear();int saved=0;
        await DailyPracticeQueue.Run(jobs,()=>Task.FromResult(new DailyPracticeSnapshot([a with{Level=2},c],100,[])),
            (ch,g)=>{called.Add(ch.Character);return Task.FromResult("bargain.upgrade");},()=>false,()=>saved++);
        Check(called.SequenceEqual(new long[]{12})&&jobs.Count==1&&jobs[0].State=="completed"&&saved>0,
            "run without refresh removes upgraded costume without spending pills and continues next entry");
        jobs=[legacy];
        try{await DailyPracticeQueue.Run(jobs,Read,(ch,g)=>throw new TimeoutException(),()=>false,()=>{});}catch(TimeoutException){}
        Check(jobs[0].Level==1&&jobs[0].State=="pending","first live run saves legacy level before a consuming attempt and retains it after interruption");
    }
}
