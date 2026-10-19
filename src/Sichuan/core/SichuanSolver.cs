using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
namespace BD2Sichuan;

public sealed record SichuanMove(int First, int Second, int[] Path);
public sealed record SichuanSolution(string Status, SichuanMove[] Moves, int Nodes, double Milliseconds, bool Reused=false);

public static class SichuanSolver
{
    public static bool Pairable(int id)=>id/1000 is 1 or 2 or 11 or 13;
    public static bool Supported(int id)=>id==0 || id is >=1001 and <=1020 or >=2001 and <=2030 or 10001 or >=11001 and <=11003 or >=12001 and <=12003 or 13001;
    public static bool Cleared(int[] cells)=>!cells.Any(id=>id!=0 && id/1000!=10);
    public static string Hash(int width,int height,int[] cells)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(width+"x"+height+":"+string.Join(",",cells))));
    public static void Validate(int w,int h,int[] cells)
    {
        if(w<1 || h<1 || w>32 || h>32 || cells.Length!=w*h || cells.Any(id=>!Supported(id)))
            throw new ArgumentException("盘面尺寸或方块类型不受支持，请等待有效采集。");
    }
    // One bounded BFS per starting tile. State includes incoming direction and turn count.
    public static List<SichuanMove> Moves(int w,int h,int[] cells,CancellationToken token=default)
    {
        Validate(w,h,cells);var result=new List<SichuanMove>();
        var dx=new[]{1,0,-1,0};var dy=new[]{0,1,0,-1};
        for(int a=0;a<cells.Length;a++)
        {
            token.ThrowIfCancellationRequested();if(!Pairable(cells[a]))continue;
            var queue=new Queue<(int cell,int dir,int turns,int[] path)>();
            var best=Enumerable.Repeat(3,cells.Length*4).ToArray();var found=new HashSet<int>();
            for(int d=0;d<4;d++)queue.Enqueue((a,d,0,new[]{a}));
            while(queue.Count>0)
            {
                token.ThrowIfCancellationRequested();var s=queue.Dequeue();int x=s.cell%w+dx[s.dir],y=s.cell/w+dy[s.dir];
                if(x<0||x>=w||y<0||y>=h)continue;int b=y*w+x;
                if(b==a)continue;
                var path=s.path.Append(b).ToArray();
                if(cells[b]!=0)
                {
                    if(b>a && cells[b]==cells[a] && found.Add(b))result.Add(new(a,b,path));
                    continue;
                }
                if(best[b*4+s.dir]<=s.turns)continue;best[b*4+s.dir]=s.turns;
                queue.Enqueue((b,s.dir,s.turns,path));
                if(s.turns<2)
                {queue.Enqueue((b,(s.dir+1)%4,s.turns+1,path));queue.Enqueue((b,(s.dir+3)%4,s.turns+1,path));}
            }
        }
        return result;
    }
    public static int[] Apply(int w,int h,int[] cells,SichuanMove move)
    {
        if(!Moves(w,h,cells).Any(m=>m.First==move.First && m.Second==move.Second))throw new ArgumentException("这一对当前无法连通。");
        return ApplyKnown(cells,move);
    }
    private static int[] ApplyKnown(int[] cells,SichuanMove m)
    {
        var copy=(int[])cells.Clone();var id=copy[m.First];copy[m.First]=copy[m.Second]=0;
        if(id/1000==11)for(int i=0;i<copy.Length;i++)if(copy[i]==id+1000)copy[i]=0;
        return copy;
    }
    public static SichuanSolution Solve(int w,int h,int[] cells,int budgetMs=750,CancellationToken token=default,Action<SichuanMove>? quick=null,float elapsed=0,float timerBonus=0)
    {
        Validate(w,h,cells);var watch=Stopwatch.StartNew();int nodes=0;bool expired=false;
        var seen=new HashSet<string>();var route=new List<SichuanMove>();SichuanMove[] fallback=Array.Empty<SichuanMove>();
        int Priority(SichuanMove m,int[] b)
        {
            var id=b[m.First];int score=id/1000==11?b.Count(v=>v==id+1000)*1000:0;
            if(id/1000==13)score+=(int)(Math.Min(elapsed,timerBonus)*10);
            foreach(var i in new[]{m.First,m.Second})
            {int x=i%w,y=i/w;foreach(var (xx,yy) in new[]{(x-1,y),(x+1,y),(x,y-1),(x,y+1)})if(xx>=0&&xx<w&&yy>=0&&yy<h&&b[yy*w+xx]!=0)score+=2;}
            return score-m.Path.Length;
        }
        bool Search(int[] b)
        {
            token.ThrowIfCancellationRequested();nodes++;
            if(Cleared(b))return true;
            if(nodes>25000 || (nodes>1 && watch.ElapsedMilliseconds>=budgetMs)){expired=true;return false;}
            var key=string.Join(",",b);if(seen.Contains(key))return false;
            var moves=Moves(w,h,b,token).OrderByDescending(m=>Priority(m,b)).ToArray();
            if(nodes==1 && moves.Length>0){fallback=new[]{moves[0]};quick?.Invoke(moves[0]);}
            foreach(var m in moves)
            {
                route.Add(m);if(Search(ApplyKnown(b,m)))return true;route.RemoveAt(route.Count-1);
                if(expired)return false;
            }
            seen.Add(key);return false;
        }
        bool solved=Search((int[])cells.Clone());
        return new(solved?"route_found":expired?"budget_exhausted":"no_static_route",solved?route.ToArray():fallback,nodes,watch.Elapsed.TotalMilliseconds);
    }
    public static SichuanSolution? Continue(int w,int h,int[] before,int[] after,SichuanSolution previous)
    {
        if(previous.Status!="route_found" || previous.Moves.Length<1)return null;
        int[] expected=Apply(w,h,before,previous.Moves[0]);
        return expected.SequenceEqual(after)?new("route_found",previous.Moves.Skip(1).ToArray(),0,0,true):null;
    }
}
