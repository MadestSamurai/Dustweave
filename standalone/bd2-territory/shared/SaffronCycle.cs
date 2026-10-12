using System;
namespace BD2Territory
{
 public sealed class SaffronStatus
 {
  public string State{get;set;}="idle";public string Reason{get;set;}="";
  public int Harvested{get;set;}public int PotatoRounds{get;set;}
  public long BuffEnd{get;set;}
 }
 // Decisions share the server clock (milliseconds), not the desktop clock.
 public static class SaffronCycle
 {
  public const int Seed=15,Potato=2,Recipe=7,Dish=3071;
  public static readonly int[] Foods={3071,3111,3121};
  public static double HarvestSeconds(int count,double routeSeconds)=>count*2.0+routeSeconds*2+20;
  public static bool CanBuff(int count,bool allMature,bool reachable,bool capacity,bool affordable,double routeSeconds,double secondsAvailable)
   =>count>0&&allMature&&reachable&&capacity&&affordable&&routeSeconds>=0&&HarvestSeconds(count,routeSeconds)<secondsAvailable;
  public static bool CanPlantPotatoes(long now,long buffEnd,int growthSeconds,double harvestSeconds)
   =>growthSeconds>0&&harvestSeconds>=0&&buffEnd-now>(growthSeconds+harvestSeconds+20)*1000;
  public static long SellCount(long owned)=>Math.Max(0,owned-1);
  public static bool AllowHarvest(string phase,int seed,bool capturedField)
   =>capturedField&&(phase=="harvest"&&seed==Seed||(phase=="potatoes"||phase=="drain")&&seed==Potato);
  // A partially planted round is filled before harvesting. Never start another
  // round unless its growth AND harvest fit inside the remaining buff window.
  public static string PotatoAction(long now,long buffEnd,int growthSeconds,double harvestSeconds,bool filling,int empty,int mature,int growing,bool affordable,bool capacity)
  {
   if(filling&&empty>0&&CanPlantPotatoes(now,buffEnd,growthSeconds,harvestSeconds)&&affordable&&capacity)return "plant";
   if(mature>0)return "harvest";
   if(growing>0)return "wait";
   if(now>=buffEnd)return "replant";
   return empty>0&&CanPlantPotatoes(now,buffEnd,growthSeconds,harvestSeconds)&&affordable&&capacity?"plant":"drain";
  }
 }
}
