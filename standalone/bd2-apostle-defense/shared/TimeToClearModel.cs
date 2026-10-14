using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BD2ApostleDefense
{
 // Frozen stage rule selected on normal randomized games. No device-speed eligibility gate.
 // Frame duration only estimates the game's native attack cadence, as in the existing policy.
 public sealed partial class TimeToClearModel
 {
  public const string Policy="clear50-time-to-clear";
  private readonly Dictionary<int,double[]> coverage=new Dictionary<int,double[]>();
  private string geometry="";
  public static int Gate(int wave){return wave>=5&&wave<=45&&wave%5==0?wave/5-1:-1;}
  public static bool AtCheckpoint(Snapshot s)
  {
   var wave=s.Catalog.Waves.FirstOrDefault(w=>w.Id==s.Wave);
   // Attaching midway through a wave must not score an untrained late-wave board.
   return Gate(s.Wave)>=0&&wave!=null&&wave.Duration-s.SecondsLeft>=.75&&wave.Duration-s.SecondsLeft<=3;
  }
  private static bool Finite(double x){return !double.IsNaN(x)&&!double.IsInfinity(x);}
  private static double Log(double x){return Math.Max(-7,Math.Min(7,Math.Log(Math.Max(.001,x))));}
  private static Vector2 V(Point p){return new Vector2((float)p.X,(float)p.Z);}
  private static bool ValidPoint(Point p){return p!=null&&Finite(p.X)&&Finite(p.Z)&&Math.Abs(p.X)<1e6&&Math.Abs(p.Z)<1e6;}
  public bool TryScore(Snapshot s,out double score,out double threshold)
  {
   score=0;threshold=0;double[] features;
   if(!TryFeatures(s,out features))return false;
   int gate=Gate(s.Wave);threshold=Thresholds[gate];
   for(int j=0;j<Weights.Length;j++)score+=Weights[j]*(features[j]-Means[gate][j])/Scales[gate][j];
   return Finite(score);
  }
  public bool TryFeatures(Snapshot s,out double[] features)
  {
   features=null;
   if(s==null||Gate(s.Wave)<0||s.Levels==null||s.Levels.Length!=5||s.Levels.Any(x=>x<0||x>99)||s.Gold<0||s.EnemyCount<0||s.EnemyCount>=100||!Finite(s.SecondsLeft)||s.SecondsLeft<=0||s.Path.Length<2||s.Boards.Length==0||s.Units.Length==0||s.Units.Any(u=>!u.Ready))return false;
   if(s.Path.Any(p=>!ValidPoint(p))||s.Boards.Any(p=>!ValidPoint(p))||s.Boards.Select(b=>b.Id).Distinct().Count()!=s.Boards.Length||s.Units.Select(u=>u.Grid).Distinct().Count()!=s.Units.Length||s.Catalog.Units.Select(d=>d.Id).Distinct().Count()!=s.Catalog.Units.Length)return false;
   var defs=s.Catalog.Units.ToDictionary(d=>d.Id);var columns=s.Boards.Select((b,i)=>new{b.Id,Index=i}).ToDictionary(x=>x.Id,x=>x.Index);
   if(s.Units.Any(u=>!defs.ContainsKey(u.Id)||!columns.ContainsKey(u.Grid)))return false;
   if(defs.Values.Any(d=>d.Element<0||d.Element>=5||d.Attack<0||d.UpAttack<0||!Finite(d.Range)||d.Range<=0||!Finite(d.Interval)||d.Interval<=0||!Finite(d.SplashRange)||d.SplashRange<0))return false;
   var current=s.Catalog.Waves.FirstOrDefault(w=>w.Id==s.Wave);var next=s.Catalog.Waves.Where(w=>w.Id>=s.Wave&&w.Boss).OrderBy(w=>w.Id).FirstOrDefault();var final=s.Catalog.Waves.FirstOrDefault(w=>w.Id==50);
   if(current==null||next==null||final==null||new[]{current,next,final}.Any(w=>!Finite(w.Hp)||w.Hp<=0||!Finite(w.Duration)||w.Duration<=0||w.Count<=0||!Finite(w.Speed)||w.Speed<0||!Finite(w.SpawnInterval)||w.SpawnInterval<0)||current.Duration-s.SecondsLeft<.75||s.SecondsLeft>current.Duration)return false;
   if(s.Enemies.Length!=s.EnemyCount||s.Enemies.Any(e=>!Finite(e.Hp)))return false;
   string key=Identity.Hash(s.Catalog.Key+string.Join(";",s.Boards.Cast<Point>().Concat(s.Path).Select(p=>p.Id+":"+p.X.ToString("R")+":"+p.Z.ToString("R")))+string.Join(";",defs.Values.Select(d=>d.Id+":"+d.Range.ToString("R"))));
   if(geometry!=key){coverage.Clear();geometry=key;}
   double now=Power(s,current,defs,columns,-1),boss=Power(s,next,defs,columns,-1);
   features=new[]{Log(boss/Math.Max(1,next.Hp*next.Count/next.Duration)),Log(now/Math.Max(1,current.Hp*current.Count/current.Duration)),Log(Power(s,final,defs,columns,-1)/Math.Max(1,final.Hp*final.Count/final.Duration)),Math.Log(1+s.Gold/100.0),s.Units.Length/32.0,s.Units.Count(u=>defs[u.Id].Grade==6)/3.0,(double)s.Units.Count(u=>defs[u.Id].Grade==7),(double)s.Units.Count(u=>defs[u.Id].Grade==8),s.EnemyCount/100.0,Math.Log(1+s.Levels.Sum())/5.0,(Power(s,next,defs,columns,0)+Power(s,next,defs,columns,4))/Math.Max(1,boss),current.Boss?Log(s.Enemies.Sum(e=>Math.Max(0,e.Hp))/Math.Max(1,s.SecondsLeft*now)):0};
   return features.All(Finite);
  }
  private double Power(Snapshot s,WaveDef w,Dictionary<int,UnitDef> defs,Dictionary<int,int> columns,int element)
  {
   double sum=0;
   foreach(var u in s.Units)
   {
    var d=defs[u.Id];if(element>=0&&d.Element!=element)continue;
    double range=Coverage(d,s)[columns[u.Grid]];
    double uptime=w.Boss?range:Math.Min(1,range*40/Math.Max(.1,(float)w.Speed*(float)(w.SpawnInterval*1000)/1000));
    double hit=(d.Attack+(double)d.UpAttack*s.Levels[d.Element])*Element(d.Element,w.Element);
    double splash=d.SplashRange>0?Math.Min(w.Count,1+2*(float)d.SplashRange/Math.Max(.25,(float)w.Speed*(float)(w.SpawnInterval*1000)/1000f)):1;
    double frame=Finite(s.FrameSeconds)&&s.FrameSeconds>0?Math.Max(1.0/240,Math.Min(1.0/10,s.FrameSeconds)):1.0/60;
    sum+=Math.Min(hit,w.Hp)*uptime*Math.Min(splash,8)/((float)d.Interval+frame);
   }
   return sum;
  }
  private static double Element(int a,int b){return ((a==0&&b==1)||(a==1&&b==2)||(a==2&&b==0)||(a==3&&b==4)||(a==4&&b==3))?2:((a==0&&b==2)||(a==1&&b==0)||(a==2&&b==1))?.5:1;}
  private double[] Coverage(UnitDef d,Snapshot s)
  {
   double[] values;if(coverage.TryGetValue(d.Id,out values))return values;
   values=new double[s.Boards.Length];float radius=(float)d.Range*(float)d.Range;
   for(int b=0;b<values.Length;b++)
   {
    double hit=0,total=0;
    for(int i=0;i<s.Path.Length;i++)
    {
     var a=V(s.Path[i]);var z=V(s.Path[(i+1)%s.Path.Length]);float length=Vector2.Distance(a,z);
     for(int n=0;n<128;n++){total+=length;if(Vector2.DistanceSquared(a+(z-a)*((n+.5f)/128),V(s.Boards[b]))<=radius)hit+=length;}
    }
    values[b]=total>0?hit/total:0;
   }
   coverage[d.Id]=values;return values;
  }
 }
}
