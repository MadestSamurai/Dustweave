using System.Text.Json.Nodes;
using P = Dustweave.Soul.NativeBattleKernel.Placement;
namespace Dustweave.Soul;

internal sealed partial class NativeFastPlanner
{
    // Equal continuation must never exchange current survival for idle cash;
    // in the last wave future value is zero for every candidate.
    internal static (double Future,double Current,int Economy) ContinuationRank(double future,double current,int cost)=>(future,current,-cost);
    // Exact finite-lives recurrence for a *specified* sequence of win rates.
    // It is conditional on the acquired roster; never a claim about unknown
    // future shop offers or a calibrated end-to-end completion probability.
    internal static (double Clear,double Seconds) RetryValue(double[] rates,double[] duration,int lives)
    {
        if(rates.Length!=duration.Length||lives<1||rates.Any(p=>p<0||p>1))throw new ArgumentException("Invalid horizon");
        var p=new double[rates.Length+1,lives+1];var t=new double[rates.Length+1,lives+1];
        for(int l=1;l<=lives;l++)p[rates.Length,l]=1;
        for(int w=rates.Length-1;w>=0;w--)for(int l=1;l<=lives;l++){
            p[w,l]=rates[w]*p[w+1,l]+(1-rates[w])*p[w,l-1];
            t[w,l]=duration[w]+rates[w]*t[w+1,l]+(1-rates[w])*t[w,l-1];
        }
        return(p[0,lives],t[0,lives]);
    }
    readonly Dictionary<string,(double Clear,double Margin,double Seconds,JsonArray Evidence)> horizonMemo=[];
    JsonNode[]? stageRows;
    readonly Dictionary<int,byte[]> horizonTemplates=[];
    readonly Dictionary<string,double> growthScores=[];
    double GrowthPotential(Candidate c)
    {
        string key=Key(c.Board);if(growthScores.TryGetValue(key,out var known))return known;
        stageRows??=Load("IBStageTable").Where(n=>N(n,"groupId")==group&&N(n,"id")>=wave).OrderBy(n=>N(n,"id")).ToArray();
        var checkpoints=stageRows.Where(n=>N(n,"id")>wave&&(N(n,"id")%5==0||N(n,"id")==wave+1)).Take(2).ToArray();
        if(checkpoints.Length==0)return growthScores[key]=0;
        double total=0;
        foreach(var row in checkpoints){
            int w=N(row,"id"),available=budget-c.Cost+stageRows.Where(n=>N(n,"id")>wave&&N(n,"id")<=w).Sum(n=>N(n,"entryGainCredit"));
            if(!horizonTemplates.TryGetValue(w,out var battle))horizonTemplates[w]=battle=kernel.Compile(new JsonObject{["stage"]=new JsonObject{["groupId"]=group,["id"]=w},["blue"]=new JsonObject{["items"]=new JsonArray()}});
            var projected=new List<P>(c.Board);
            // Optimistic but feasible level allocation is a search heuristic,
            // not the final forecast. No item absent from this bundle enters.
            foreach(var p in c.Board.Where(p=>stock[p.Inventory].Kind==1).OrderByDescending(p=>units[(p.Id,p.Level+1)]["maxDamage"]?.GetValue<int>()??0)){
                for(int level=stock[p.Inventory].Maximum;level>p.Level;level--){
                    var changed=p with{Level=level};int price=Cost(changed)-Cost(p);if(price>available)continue;
                    projected[projected.IndexOf(p)]=changed;available-=price;break;
                }
            }
            projected=BindEquipment(projected);kernel.RebuildBlue(battle,projected);
            var rs=kernel.Run(battle,[953171,1847183,3817271,7419137]);
            total+=(double)rs.Count(r=>r.Winner==1)/rs.Length+.35*rs.Average(r=>Remaining(r.Blue.Hp,r.Blue.MaxHp)-Remaining(r.Red.Hp,r.Red.MaxHp));
        }
        return growthScores[key]=total/checkpoints.Length;
    }
    (double,double,double,double) GrowthRank(Candidate c)
    {
        // Future potential starts influencing search before finalists are
        // discarded. A separate current-wave champion remains in the pool.
        return c.Rate>=.9375?(1,GrowthPotential(c),c.Rate,-.01*c.Cost-.0001*c.Seconds):(0,c.Rate,c.Margin,c.Rate==0?c.Seconds:-c.Seconds);
    }
    void OfferDecision(Candidate? c)
    {
        if(c is null||elite.Any(e=>Key(e.Board)==Key(c.Board)))return;
        elite.Add(c);var current=elite.OrderByDescending(e=>e.Rank).Take(4).ToArray();
        var ranked=elite.OrderByDescending(GrowthRank).ToArray();var chosen=ranked.Take(8).Concat(current).DistinctBy(e=>Key(e.Board)).ToList();
        foreach(var x in ranked.GroupBy(e=>(e.Cost/Math.Max(1,budget/5),string.Join(',',e.Board.Where(p=>stock[p.Inventory].Kind==1).Select(p=>p.Id).Order()))).Select(g=>g.First())){
            if(!chosen.Contains(x))chosen.Add(x);if(chosen.Count>=24)break;
        }
        elite.Clear();elite.AddRange(chosen);
    }
    (double Clear,double Margin,double Seconds,JsonArray Evidence) Continuation(Candidate candidate,int[] seeds,Func<bool> within)
    {
        string key=Key(candidate.Board);if(horizonMemo.TryGetValue(key,out var saved))return saved;
        stageRows??=Load("IBStageTable").Where(n=>N(n,"groupId")==group&&N(n,"id")>=wave).OrderBy(n=>N(n,"id")).ToArray();
        var evidence=new JsonArray();var rates=new List<double>{candidate.Rate};var durations=new List<double>{candidate.Seconds+2*Transactions(candidate)};
        var board=BindEquipment(candidate.Board);int available=budget-candidate.Cost;double margin=0;int count=0;
        // Three consecutive future waves. Upgrade choices are feasible in
        // every prefix, preserve items and bindings, and never spend a future
        // reward early. The next real decision replans from actual inventory.
        foreach(var row in stageRows.Skip(1).Take(3)){
            if(!within())break;
            int next=N(row,"id");available+=N(row,"entryGainCredit");
            if(!horizonTemplates.TryGetValue(next,out var battle))horizonTemplates[next]=battle=kernel.Compile(new JsonObject{["stage"]=new JsonObject{["groupId"]=group,["id"]=next},["blue"]=new JsonObject{["items"]=new JsonArray()}});
            (double Rate,double Margin,double Time) Score(List<P> layout){
                kernel.RebuildBlue(battle,layout);var rs=kernel.Run(battle,seeds);
                return((double)rs.Count(r=>r.Winner==1)/rs.Length,rs.Average(r=>Remaining(r.Blue.Hp,r.Blue.MaxHp)-Remaining(r.Red.Hp,r.Red.MaxHp)),rs.Average(r=>(double)r.Seconds));
            }
            var best=Score(board);int spent=0;
            // Bounded upgrade branch search, not the old all-or-nothing
            // upgrade projection. Comparison uses common random numbers.
            for(int depth=0;depth<2&&within();depth++){
                List<P>? chosen=null;var selected=best;int price=0;
                foreach(var member in board.Where(p=>stock[p.Inventory].Kind==1).ToArray()){
                    if(!within())break;
                    int maximum=stock[member.Inventory].Maximum;
                    foreach(int level in new[]{member.Level+1,maximum}.Where(l=>l>member.Level&&l<=maximum).Distinct()){
                        if(!within())break;
                        var changed=new List<P>(board);changed[changed.IndexOf(member)]=member with{Level=level};
                        int cost=Cost(member with{Level=level})-Cost(member);
                        if(cost>available)continue;changed=BindEquipment(changed);
                        var trial=Score(changed);
                        if(DecisionRank(trial.Rate,trial.Margin,trial.Time).CompareTo(DecisionRank(selected.Rate,selected.Margin,selected.Time))>0){selected=trial;chosen=changed;price=cost;}
                    }
                }
                if(chosen is null)break;board=chosen;best=selected;available-=price;spent+=price;
            }
            rates.Add(best.Rate);durations.Add(best.Time);margin+=best.Margin;count++;
            evidence.Add(new JsonObject{["wave"]=next,["winRate"]=best.Rate,["trials"]=seeds.Length,["upgradeCost"]=spent,["remainingCoins"]=available});
        }
        var value=RetryValue(rates.ToArray(),durations.ToArray(),N(state["play"],"life"));
        var result=(value.Clear,count==0?candidate.Margin:margin/count,value.Seconds,evidence);
        // Do not memoize a deadline-truncated forecast as a complete horizon.
        if(count==Math.Min(3,stageRows.Length-1))horizonMemo[key]=result;
        return result;
    }
}
