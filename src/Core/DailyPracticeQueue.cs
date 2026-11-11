using System.Text.Json.Nodes;
using static Dustweave.DailyData;
namespace Dustweave;

public sealed record DailyPracticeAchievement(int Kind,int Group,int ContentsGroup,long Target,long Current,bool Known)
{
    public long Remaining=>Math.Max(0,Target-Current);
    public static DailyPracticeAchievement[] Read(JsonObject state)=>Rows(state["Achievements"] as JsonArray??new JsonArray())
        .Select(r=>new DailyPracticeAchievement(I(r["Kind"]),I(r["Group"]),I(r["ContentsGroup"]),N(r["Target"]),N(r["Current"]),B(r["Known"])))
        .Where(a=>a.Target>0&&a.Current>=0).ToArray();
    public static DailyPracticeAchievement? Highest(IEnumerable<DailyPracticeAchievement> goals,int kind)=>goals.Where(g=>g.Kind==kind)
        .OrderByDescending(g=>g.Target).ThenBy(g=>g.ContentsGroup).ThenBy(g=>g.Group).FirstOrDefault();
    public static DailyPracticeAchievement From(JsonObject evidence,string readId,int kind)
    {
        var goal=Highest(Read(DailyData.State(evidence,readId,"$self")),kind)??throw new DailyBargainFault("practice.achievement_missing");
        if(!goal.Known)throw new DailyBargainFault("practice.achievement_unknown");
        return goal;
    }
    public DailyPracticeAchievement Refresh(JsonObject evidence,string readId)
    {
        var next=From(evidence,readId,Kind);
        Require(next.Group==Group&&next.ContentsGroup==ContentsGroup&&next.Target==Target&&next.Current>=Current,"bargain.preview_changed");
        return next;
    }
    public static bool Authorized(JsonObject op,JsonObject before,string readId,int kind)
    {
        if(!B(op["scope"]?["achievement"]))return false;
        var goal=From(before,readId,kind);
        Require(goal.Remaining>0&&goal.Target==N(op["scope"]?["achievementTarget"]),"bargain.preview_changed");
        return true;
    }
    public async Task<DailyPracticeAchievement> AfterUse(DailyWorkflow w,string readId)
    {
        double until=w.Time+10;
        do{
            var next=Refresh(await w.Evidence(readId),readId);
            if(next.Current>Current)return next;
            await w.Delay(200);
        }while(w.Time<until);
        // A confirmed cast is not a license to spend indefinitely if the game's
        // achievement counter does not advance. Refreshing adopts later progress.
        throw new DailyBargainFault("practice.achievement_stalled");
    }
}

public sealed record DailyPracticeSnapshot(DailyBargainCharacter[] Characters,long Potions,DailyPracticeAchievement[] Achievements);
public sealed record DailyPracticeBudget(long Potions,long Uses,int UnknownItems)
{
    public long Shortage(long stock)=>Math.Max(0,Potions-Math.Max(0,stock));
}
public sealed record DailyPracticeJob(string Id,int Kind,long Character,bool Achievement,string State="pending",string Message="",int? Level=null)
{
    public static DailyPracticeJob Create(int kind,long character,bool achievement,int? level=null)=>new(Guid.NewGuid().ToString("N"),kind,achievement?0:character,achievement,Level:achievement?null:level);
}
public static class DailyPracticeQueue
{
    // XP entries authorize the selected tier only. Achievement entries authorize
    // a skill-wide use target and deliberately have no costume-level binding.
    public static DailyPracticeJob[] RefreshLevels(IList<DailyPracticeJob> jobs,IEnumerable<DailyBargainCharacter> current,
        IEnumerable<DailyBargainCharacter>? previous=null)
    {
        var now=current.ToArray();var before=previous?.ToArray()??[];
        var removed=new List<DailyPracticeJob>();
        for(int i=0;i<jobs.Count;i++)
        {
            var job=jobs[i];if(job.Achievement)continue;
            var c=now.FirstOrDefault(c=>c.Character==job.Character&&c.Kind==job.Kind);
            if(c==null||c.Level<=0)continue; // Unread progress is not evidence of an upgrade.
            int? baseline=job.Level??before.FirstOrDefault(c=>c.Character==job.Character&&c.Kind==job.Kind)?.Level;
            if(baseline is >0&&baseline!=c.Level){removed.Add(job);jobs.RemoveAt(i--);}
            else if(job.Level is not >0)jobs[i]=job with{Level=c.Level};
        }
        return removed.ToArray();
    }
    // Display-only projection, independent of current affordability. Project in
    // queue order: XP training also advances use-count goals, and achievement
    // training also earns XP. Never charge twice for that shared progress.
    public static DailyPracticeBudget Estimate(IEnumerable<DailyPracticeJob> jobs,DailyPracticeSnapshot state)
    {
        var characters=state.Characters.ToArray();
        var goals=state.Achievements.GroupBy(g=>g.Kind).ToDictionary(g=>g.Key,g=>DailyPracticeAchievement.Highest(g,g.Key)!);
        long pills=0,uses=0;int unknown=0;
        foreach(var job in jobs.Where(j=>j.State!="completed"))
        {
            goals.TryGetValue(job.Kind,out var goal);
            if(job.Achievement&&goal is{Known:true,Remaining:0})continue;
            var c=job.Achievement?Cheapest(characters,job.Kind):characters.FirstOrDefault(c=>c.Character==job.Character&&c.Kind==job.Kind);
            if(c==null){unknown++;continue;}
            if(!job.Achievement&&c.Full)continue;
            if(c.Cost<=0||(job.Achievement?goal is not{Known:true}:c.PerUse<=0)){unknown++;continue;}
            try
            {
                long n=job.Achievement?goal!.Remaining:c.Remaining/c.PerUse+(c.Remaining%c.PerUse==0?0:1);
                long nextPills=checked(pills+checked(n*c.Cost)),nextUses=checked(uses+n);
                // Clamp before multiplying so large achievement counts cannot
                // overflow XP or imply an automatic talent-book upgrade.
                long gain=c.Full||c.PerUse<=0?0:n>=c.Uses?c.Remaining:checked(n*c.PerUse);
                var next=c with{Experience=checked(c.Experience+gain)};
                characters=characters.Select(x=>x.Character==c.Character&&x.Kind==c.Kind?next:x).ToArray();
                if(goal is{Known:true})goals[job.Kind]=goal with{Current=goal.Current+Math.Min(n,goal.Remaining)};
                pills=nextPills;uses=nextUses;
            }
            catch(OverflowException){unknown++;}
        }
        return new(pills,uses,unknown);
    }
    public static DailyBargainCharacter? Cheapest(IEnumerable<DailyBargainCharacter> characters,int kind)=>characters
        .Where(c=>c.Kind==kind&&c.Cost>0).OrderBy(c=>c.Cost).ThenBy(c=>c.Character).FirstOrDefault();
    public static async Task Run(IList<DailyPracticeJob> jobs,Func<Task<DailyPracticeSnapshot>> read,
        Func<DailyBargainCharacter,DailyPracticeAchievement?,Task<string>> run,Func<bool> stopped,Action changed)
    {
        for(int i=0;i<jobs.Count&&!stopped();i++)
        {
            var job=jobs[i];if(job.State=="completed")continue;
            var state=await read();DailyPracticeAchievement? goal=null;
            var c=job.Achievement?Cheapest(state.Characters,job.Kind):state.Characters.FirstOrDefault(c=>c.Character==job.Character&&c.Kind==job.Kind);
            if(!job.Achievement&&c is{Level:>0}){
                // Also reconcile when Run is pressed without a manual refresh.
                if(job.Level is >0&&job.Level!=c.Level){jobs.RemoveAt(i--);changed();continue;}
                if(job.Level is not >0){job=job with{Level=c.Level};jobs[i]=job;changed();}
            }
            string? skip=null;bool done=false;
            if(job.Achievement){goal=DailyPracticeAchievement.Highest(state.Achievements,job.Kind);if(goal==null)skip="practice.achievement_missing";else if(!goal.Known)skip="practice.achievement_unknown";else if(goal.Remaining==0){skip="practice.achievement_complete";done=true;}}
            if(skip==null&&c==null)skip="bargain.character_unavailable";
            if(skip==null&&!job.Achievement&&c!.Full){skip=c.Level>=c.MaxLevel?"bargain.maximum":"bargain.upgrade";done=true;}
            if(skip==null&&(job.Achievement?state.Potions<c!.Cost:new DailyBargainPlan(c!,state.Potions).CanStart==false))skip="practice.queue_insufficient";
            if(skip!=null){jobs[i]=job with{State=done?"completed":"blocked",Message=skip};changed();continue;}
            if(stopped())break;
            jobs[i]=job with{State="running",Message=""};changed();
            try{
                string result=await run(c!,goal);
                bool complete=result is "bargain.upgrade" or "bargain.maximum" or "practice.achievement_complete";
                jobs[i]=job with{State=complete?"completed":"pending",Message=result};changed();
                if(!complete)break;
            }catch{
                jobs[i]=job with{State="pending",Message="practice.queue_retry"};changed();throw;
            }
        }
    }
}
