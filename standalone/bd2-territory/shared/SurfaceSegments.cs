using System;
using System.Collections.Generic;
namespace BD2Territory
{
 public static class SurfaceSegments
 {
  // Split at EVERY terrain-cell boundary as well as short physical intervals. Uniform
  // sampling alone misses a narrow water corner between two otherwise dry samples.
  public static double[] Cuts(double x0,double z0,double x1,double z1,double length)
  {
   var cuts=new SortedSet<double>{0,1};int count=Math.Max(1,(int)Math.Ceiling(length/.1));
   for(int i=1;i<count;i++)cuts.Add((double)i/count);
   Add(cuts,x0,x1);Add(cuts,z0,z1);var result=new double[cuts.Count];cuts.CopyTo(result);return result;
  }
  private static void Add(SortedSet<double> cuts,double a,double b)
  {
   if(Math.Abs(b-a)<1e-12)return;
   for(double edge=Math.Floor(Math.Min(a,b))+1;edge<Math.Max(a,b);edge++)
   {double t=(edge-a)/(b-a);if(t>0&&t<1)cuts.Add(t);}
  }
 }
}
