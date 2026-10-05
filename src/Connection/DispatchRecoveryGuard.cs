using System;
namespace BD2Daily.Live {
 // A two-second continuous observation, not a delay followed by an unchecked clear.
 public sealed class DispatchRecoveryGuard {
  string fingerprint="";double since,last;bool consumed;
  public bool Sample(string current,double now){
   if(consumed)throw new InvalidOperationException("Recovery already consumed");
   if(string.IsNullOrEmpty(current)){fingerprint="";return false;}
   if(current!=fingerprint||now<last||now-last>1){fingerprint=current;since=now;}
   last=now;return now-since>=2;
  }
  public void Consume(){consumed=true;}
 }
}
