using System;
using System.Linq;

namespace BD2Equipment.Live {
 // Amounts are for one requested resource, not for the whole reward bundle.
 // A random choice between unrelated extras has a fixed contribution of zero.
 public static class SupplyRewardMath {
  public static long Fixed(int dropType,int dropCount,int[] ratios,long[] amounts){
   if(dropType==1)return amounts.Aggregate(0L,(sum,n)=>checked(sum+n));
   if(dropType!=0||ratios.Length!=amounts.Length||dropCount<1)throw new InvalidOperationException("Unsupported hunt reward rule");
   var possible=amounts.Where((n,i)=>ratios[i]>0).Distinct().ToArray();
   if(possible.Length==0&&amounts.Length==0)return 0;
   if(possible.Length!=1)throw new InvalidOperationException("Target hunt resource does not have a fixed yield");
   return checked(possible[0]*dropCount);
  }
 }
}
