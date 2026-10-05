namespace BD2SecretVision;
// Cells and edges are separate game state: a promoted edge need not have claimed cells.
public sealed class Topology {
 readonly Board b;readonly int w,h;readonly bool[] horizontal,vertical;readonly int[] seen,queue;int stamp;
 public Topology(Board board){Validate(board);b=board;w=b.Width;h=b.Height;horizontal=new bool[w*(h+1)];vertical=new bool[(w+1)*h];seen=new int[w*h];queue=new int[w*h];
  for(int y=0;y<=h;y++)for(int x=0;x<w;x++)horizontal[y*w+x]=Solid(Edge(b,new(x,y),new(x+1,y)));
  for(int y=0;y<h;y++)for(int x=0;x<=w;x++)vertical[y*(w+1)+x]=Solid(Edge(b,new(x,y),new(x,y+1)));
 }
 public static void Validate(Board b){if(!b.HasTopology||b.Width<3||b.Height<3||b.Rows.Length!=b.Height||b.Rows.Any(r=>r.Length!=b.Width)||b.HorizontalEdges.Any(r=>r.Length!=b.Width)||b.VerticalEdges.Any(r=>r.Length!=b.Width+1))throw new RouteFailure("component-topology-incomplete");}
 public static bool Solid(char edge)=>edge is 'C' or '#' or 'T';
 public static char Edge(Board b,Point a,Point z){
  if(Math.Abs(a.X-z.X)+Math.Abs(a.Y-z.Y)!=1)return '#';
  if(b.HasTopology){return a.Y==z.Y?b.HorizontalEdges[a.Y][Math.Min(a.X,z.X)]:b.VerticalEdges[Math.Min(a.Y,z.Y)][a.X];}
  // Cell-only synthetic boards are used by the legacy oracle. Live boards require edge data.
  var (x1,y1,x2,y2)=Adjacent(a,z);return b.Claimed(x1,y1)!=b.Claimed(x2,y2)?'C':'.';
 }
 public static (int,int,int,int) Adjacent(Point a,Point z)=>a.Y==z.Y?(Math.Min(a.X,z.X),a.Y-1,Math.Min(a.X,z.X),a.Y):(a.X-1,Math.Min(a.Y,z.Y),a.X,Math.Min(a.Y,z.Y));
 public static bool Safe(Board b,Point a,Point z)=>Edge(b,a,z) is 'C' or '#';
 public static bool Border(Board b,Point p)=>Directions.Any(d=>{var q=new Point(p.X+d.X,p.Y+d.Y);return q.X>=0&&q.Y>=0&&q.X<=b.Width&&q.Y<=b.Height&&Safe(b,p,q);});
 public static readonly Point[] Directions=[new(1,0),new(-1,0),new(0,1),new(0,-1)];
 public static string RouteKey(Point start,Point[] corners){var path=Expand(start,corners).ToArray();string Key(IEnumerable<Point> p)=>string.Join(';',p.Select(x=>$"{x.X},{x.Y}"));var a=Key(path);var z=Key(path.Reverse());return string.CompareOrdinal(a,z)<0?a:z;}
 public static IEnumerable<Point> Expand(Point start,IEnumerable<Point> corners){yield return start;foreach(var z in corners){if(start.X!=z.X&&start.Y!=z.Y)throw new RouteFailure("diagonal");while(start!=z){start=new(start.X+Math.Sign(z.X-start.X),start.Y+Math.Sign(z.Y-start.Y));yield return start;}}}
 public sealed record Prediction(int Cells,int RimCells,int NewEdges,int[] Filled);
 public Prediction Predict(Point start,Point[] corners,bool includeFilled=false){
  var changed=new List<(bool H,int Index)>();var a=start;
  foreach(var z in Expand(start,corners).Skip(1)){bool hor=a.Y==z.Y;int index=hor?a.Y*w+Math.Min(a.X,z.X):Math.Min(a.Y,z.Y)*(w+1)+a.X;var values=hor?horizontal:vertical;if(!values[index]){values[index]=true;changed.Add((hor,index));}a=z;}
  if(changed.Count==0)return new(0,0,0,[]);
  try{Flood();int count=0,rim=0;List<int>? filled=includeFilled?[]:null;
   for(int i=0;i<seen.Length;i++)if(seen[i]!=stamp&&b.Rows[i/w][i%w]=='.'){count++;int x=i%w,y=i/w;if(x==1||y==1||x==w-2||y==h-2)rim++;filled?.Add(i);}
   return new(count,rim,changed.Count,filled?.ToArray()??[]);
  }finally{foreach(var item in changed)(item.H?horizontal:vertical)[item.Index]=false;}
 }
 void Flood(){stamp++;int head=0,tail=0;
  void Add(int index){if(seen[index]!=stamp&&b.Rows[index/w][index%w]!='C'){seen[index]=stamp;queue[tail++]=index;}}
  for(int x=0;x<w;x++){Add(x);Add((h-1)*w+x);}for(int y=1;y<h-1;y++){Add(y*w);Add(y*w+w-1);}
  while(head<tail){int i=queue[head++],x=i%w,y=i/w;
   if(x+1<w&&!vertical[y*(w+1)+x+1])Add(i+1);
   if(x>0&&!vertical[y*(w+1)+x])Add(i-1);
   if(y+1<h&&!horizontal[(y+1)*w+x])Add(i+w);
   if(y>0&&!horizontal[y*w+x])Add(i-w);
  }
 }
}
