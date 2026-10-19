using System;
using System.Collections.Generic;
namespace BD2SecretVision.Rules {
public static class BorderEscapeRules {
 public struct Vertex {public int X,Y;public Vertex(int x,int y){X=x;Y=y;}}
 public static IEnumerable<Vertex[]> DrawingRoutes(Vertex start,int width,int height,Func<Vertex,Vertex,bool> drawable,Func<Vertex,Vertex,bool> safe,Func<Vertex,bool> border){
  foreach(var d in new[]{new Vertex(1,0),new Vertex(-1,0),new Vertex(0,1),new Vertex(0,-1)})foreach(int sign in new[]{1,-1})foreach(int length in new[]{1,2,4,8,12,20})foreach(int depth in new[]{1,2,4,8,12}){
   var side=new Vertex(-d.Y*sign,d.X*sign);var corners=new[]{new Vertex(start.X+d.X*depth,start.Y+d.Y*depth),new Vertex(start.X+d.X*depth+side.X*length,start.Y+d.Y*depth+side.Y*length),new Vertex(start.X+side.X*length,start.Y+side.Y*length)};
   var points=new List<Vertex>();var v=start;bool active=false,closed=false,valid=true;
   foreach(var target in corners){while(v.X!=target.X||v.Y!=target.Y){var next=new Vertex(v.X+Math.Sign(target.X-v.X),v.Y+Math.Sign(target.Y-v.Y));if(next.X<1||next.Y<1||next.X>=width||next.Y>=height||!drawable(v,next)){valid=false;break;}if(!safe(v,next))active=true;if(points.Count==0&&!active){valid=false;break;}points.Add(next);v=next;if(active&&border(v)){closed=true;break;}}if(!valid||closed)break;}
   if(valid&&closed)yield return points.ToArray();
  }
 }
 public static bool TimingAllows(double available,double required,bool imminentBorder){return available>=required||(imminentBorder&&required<=1.2);}
 public static double SweptDistance(double ax,double ay,double bx,double by){double dx=bx-ax,dy=by-ay,len=dx*dx+dy*dy;double t=len<=1e-12?0:Math.Max(0,Math.Min(1,-(ax*dx+ay*dy)/len));return Math.Sqrt((ax+t*dx)*(ax+t*dx)+(ay+t*dy)*(ay+t*dy));}
}
}
