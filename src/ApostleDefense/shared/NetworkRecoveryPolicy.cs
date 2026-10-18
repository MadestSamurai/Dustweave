using System;
namespace BD2ApostleDefense
{
 // Monotonic timestamps. A socket flag alone is never evidence of recent server traffic.
 public sealed class NetworkRecoveryPolicy
 {
  public const int PacketFreshSeconds=8, GraceSeconds=6, ResumeStableSeconds=2;
  public long LastPacket {get;private set;}
  public bool Deferred {get;private set;}
  public bool ServerClosed {get;private set;}
  public bool Recovering {get;private set;}
  public bool AwaitingRound {get;private set;}
  public int ProbeFailures {get;private set;}
  public int AvoidedDisconnects {get;private set;}
  public int NativeDisconnects {get;private set;}
  private long lostAt,stableSince;
  public void NewConnection(bool recovery){LastPacket=0;Deferred=false;ServerClosed=false;Recovering=recovery;AwaitingRound=recovery;lostAt=stableSince=0;}
  public void Packet(long now,bool roundReady=false){if(!ServerClosed){LastPacket=now;if(roundReady&&AwaitingRound){AwaitingRound=false;Recovering=true;stableSince=0;}}}
  public void ClosedByServer(){ServerClosed=true;Deferred=false;Recovering=true;AwaitingRound=true;LastPacket=stableSince=0;}
  public bool Fresh(long now){return LastPacket>0&&now>=LastPacket&&now-LastPacket<=TimeSpan.FromSeconds(PacketFreshSeconds).Ticks;}
  public bool Lost(long now,bool connected)
  {
   ProbeFailures++;
   if(ServerClosed||!connected){Deferred=false;Recovering=true;AwaitingRound=true;LastPacket=stableSince=0;NativeDisconnects++;return true;}
   if(!Deferred)lostAt=now;
   Deferred=true;Recovering=true;stableSince=0;return false;
  }
  public bool Restored(long now,bool connected)
  {
   if(!ServerClosed&&connected&&Fresh(now))
   {
    if(Deferred)AvoidedDisconnects++;
    Deferred=false;Recovering=true;stableSince=0;return false;
   }
   Deferred=false;Recovering=true;AwaitingRound=true;LastPacket=stableSince=0;return true;
  }
  // Called even if the external monitor never emits another lost edge.
  public bool Tick(long now,bool connected)
  {
   if(Deferred&&(!connected||(!Fresh(now)&&now-lostAt>=TimeSpan.FromSeconds(GraceSeconds).Ticks)))
   {Deferred=false;Recovering=true;AwaitingRound=true;LastPacket=stableSince=0;NativeDisconnects++;return true;}
   if(!connected){Recovering=true;AwaitingRound=true;LastPacket=stableSince=0;}
   else if(Recovering&&!ServerClosed&&Fresh(now)&&(!Deferred||LastPacket>=lostAt))
   {
    if(stableSince==0)stableSince=now;
    if(now-stableSince>=TimeSpan.FromSeconds(ResumeStableSeconds).Ticks)Recovering=false;
   }
   else if(Recovering)stableSince=0;
   return false;
  }
  public bool Hold(bool connected){return !connected||ServerClosed||Recovering||AwaitingRound;}
 }
}
