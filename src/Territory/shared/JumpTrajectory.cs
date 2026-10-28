using System;
using System.Collections.Generic;
namespace BD2Territory
{
 // Native FieldActionManager.PlayJump: velocity 5, gravity 9.8 * 1.5.
 // MoveController integrates gravity before displacement and averages horizontal speed.
 internal static class JumpTrajectory
 {
  internal static IEnumerable<RoutePoint> Samples(double speed,double maximum,double acceleration,double input,double frame)
  {
   double x=0,y=0,vertical=5;
   for(int i=0;i*frame<1.05;i++)
   {double next=Math.Min(maximum,Math.Max(0,speed)+acceleration*frame);x+=(speed+next)*.5*frame*input;speed=next;
    vertical-=14.7*frame;y+=vertical*frame;yield return new RoutePoint(x,y,(i+1)*frame);if(y<-.5)yield break;}
  }
 }
}
