using System;
namespace BD2Daily.Live {
 internal static class TradeBoxRules {
  internal static bool Allowed(int type,int keep,int available,int wanted) {
   return type==9&&keep==0&&available>0&&wanted>0&&wanted<=available;
  }
 }
}
