#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
namespace BD2Sichuan
{
    // At most three straight segments. Allocates a path only for a legal pair.
    public static class SichuanFastSolver
    {
        public static bool Pairable(int id)=>id>=1001&&id<=1020||id>=2001&&id<=2030||id>=11001&&id<=11003||id==13001;
        public static bool Cleared(int[] b)=>!b.Any(id=>id!=0&&id!=10001);
        public static int[] Path(int w,int h,int[] b,int a,int z)
        {
            if(w<1||h<1||w>32||h>32||b==null||b.Length!=w*h||a<0||z<0||a>=b.Length||z>=b.Length||a==z||!Pairable(b[a])||b[a]!=b[z])return null;
            bool Segment(int from,int to)
            {
                int x=from%w,y=from/w,tx=to%w,ty=to/w;if(x!=tx&&y!=ty)return false;
                int dx=Math.Sign(tx-x),dy=Math.Sign(ty-y);
                for(;;){int i=y*w+x;if(i!=a&&i!=z&&b[i]!=0)return false;if(x==tx&&y==ty)return true;x+=dx;y+=dy;}
            }
            int[] Build(int p,int q)
            {
                var list=new List<int>{a};int prev=a;
                foreach(var end in new[]{p,q,z})
                {int dx=Math.Sign(end%w-prev%w),dy=Math.Sign(end/w-prev/w);while(prev!=end){prev+=dx+dy*w;if(list.Contains(prev))return null;list.Add(prev);}}
                return list.ToArray();
            }
            if(Segment(a,z))return Build(a,a);
            int p1=a/w*w+z%w,p2=z/w*w+a%w;
            if(Segment(a,p1)&&Segment(p1,z))return Build(p1,p1);
            if(Segment(a,p2)&&Segment(p2,z))return Build(p2,p2);
            for(int x=0;x<w;x++){int p=a/w*w+x,q=z/w*w+x;if(Segment(a,p)&&Segment(p,q)&&Segment(q,z)){var path=Build(p,q);if(path!=null)return path;}}
            for(int y=0;y<h;y++){int p=y*w+a%w,q=y*w+z%w;if(Segment(a,p)&&Segment(p,q)&&Segment(q,z)){var path=Build(p,q);if(path!=null)return path;}}
            return null;
        }
        public static SichuanFastMove Choose(SichuanSnapshot s)
        {
            var b=s.Cells;var pairs=new List<Tuple<int,int,int>>();
            for(int a=0;a<b.Length;a++)if(Pairable(b[a]))for(int z=a+1;z<b.Length;z++)if(b[z]==b[a])
            {
                int priority=b[a]/1000==11?-10000:b[a]==13001&&s.ElapsedSeconds>=s.TimerBonusSeconds?-5000:0;
                priority+=Math.Abs(a%s.Width-z%s.Width)+Math.Abs(a/s.Width-z/s.Width);pairs.Add(Tuple.Create(priority,a,z));
            }
            pairs.Sort((a,bp)=>a.Item1.CompareTo(bp.Item1));
            foreach(var pair in pairs){var path=Path(s.Width,s.Height,b,pair.Item2,pair.Item3);if(path!=null)return new SichuanFastMove{First=pair.Item2,Second=pair.Item3,Path=path};}
            return null;
        }
    }
}
