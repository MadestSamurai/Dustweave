extern alias apostle;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using N=apostle::BD2ApostleDefense;

internal static class ApostlePerformanceCases
{
 // Frozen against the pre-optimization production policy, including scores and reasons.
 // Synthetic observations only; no captured accounts, client tables or RNG state.
 public static void Run(List<string> cases)
 {
  var policy=new N.AnnealedPolicy();
  string hash=Fingerprint((s,t,m,r)=>policy.Decide(s,t,m,r),policy.Confirm,out var kinds);
  if(hash!="3358864F0DF683AF75382724F8898BC634BC6DB34D4CE81DC5432F3AEC2857BF")throw new Exception("Apostle production decision transcript changed: "+hash);
  foreach(string kind in new[]{"wait","summon","sell","upgrade","move"})if(!kinds.Contains(kind))throw new Exception("Missing action coverage: "+kind);
  cases.Add("apostle performance: 4096 synthetic decisions, scores and confirmed-state transitions match frozen production policy");
 }
 public static string Fingerprint(Func<N.Snapshot,long,Func<int,bool>,bool,N.Decision> decide,Action<N.Decision,N.Snapshot,long> confirm,out HashSet<string> kinds)
 {
  var random=new Random(251014);var text=new StringBuilder();kinds=new();
  var defs=Enumerable.Range(0,40).Select(i=>new N.UnitDef{Id=i+1,Grade=i/5+1,Element=i%5,Attack=15*(i+1),UpAttack=4*(i+1),Interval=.2+i%6*.15,Range=1.5+i%7,SplashRange=i%3==0?1.5:0,Sell=10+i,Weight=40-i}).ToArray();
  var boards=Enumerable.Range(0,68).Select(i=>new N.Board{Id=10+i*3,X=i%10-4.5,Z=i/10-3}).ToArray();
  var path=new N.Point[]{new(){Id=2,X=-6,Z=-5},new(){Id=5,X=6,Z=-5},new(){Id=8,X=6,Z=5},new(){Id=11,X=-6,Z=5}};
  var waves=Enumerable.Range(1,50).Select(i=>new N.WaveDef{Id=i,Element=i%5,Hp=100*i*i,Count=i%5==0?1:60,Duration=30,Boss=i%5==0,Speed=1,SpawnInterval=.5,EnemyTable=i}).ToArray();
  for(int n=0;n<4096;n++)
  {
   int wave=n%50+1;var cells=boards.OrderBy(_=>random.Next()).Take(1+random.Next(68)).ToArray();
   var s=new N.Snapshot{At=638000000000000000L+n*TimeSpan.TicksPerSecond/4,ProcessId=10,ProcessStart=20,Account=new('a',64),Room="synthetic-"+(n/50),Ready=true,MyView=true,Stage="playing",Wave=wave,SecondsLeft=1+random.Next(29),Gold=random.Next(150),CanSummon=n%7!=0,EnemyCount=random.Next(90),Boards=boards,Path=path,FrameSeconds=1.0/new[]{30,60,120,144}[n%4],Catalog=new(){Key="synthetic-performance",Units=defs,Waves=waves,SummonCost=25,MaxGrade=8,MaxUpgrade=99,GameOverCount=100,Advantage=1,Penalty=.5}};
   s.Units=cells.Select((b,i)=>new N.Unit{Index=i*7+1,Id=1+random.Next(defs.Length),Grid=b.Id,Ready=true}).ToArray();
   s.Levels=Enumerable.Range(0,5).Select(_=>random.Next(60)).ToArray();s.UpgradeCosts=Enumerable.Range(0,5).Select(_=>1+random.Next(120)).ToArray();s.CanUpgrade=Enumerable.Range(0,5).Select(_=>random.Next(8)>0).ToArray();
   s.Enemies=Enumerable.Range(0,n%5==4?1:8).Select(i=>new N.Enemy{Id=i+1,Table=wave,Element=wave%5,Hp=waves[wave-1].Hp,Speed=1,X=-6+i,Z=-5,NextPoint=5}).ToArray();
   if(n%64==0)
   {
    s.Catalog.Units=new[]{new N.UnitDef{Id=1,Grade=1,Element=0,Attack=1,UpAttack=1,Interval=1,Range=100,Sell=25},new N.UnitDef{Id=2,Grade=7,Element=0,Attack=10000000,UpAttack=10000000,Interval=.2,Range=100,Sell=100}};
    s.Units=boards.Take(3).Select((b,i)=>new N.Unit{Index=i+1,Id=i==0?1:2,Grid=b.Id,Ready=true}).ToArray();
    s.Catalog.Key="synthetic-sale";s.CanSummon=false;s.CanUpgrade=new bool[5];s.EnemyCount=0;s.Gold=100;
   }
   var action=decide(s,s.At,i=>n%9!=0||i%3!=0,n%17==0);kinds.Add(action.Kind);
   text.AppendLine(JsonSerializer.Serialize(action,new JsonSerializerOptions{IncludeFields=true}));
   if(n%3!=0)confirm(action,s,s.At);
  }
  return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString().Replace("\r\n","\n"))));
 }
}
