using System;
namespace BD2Territory
{
 // Public DTO: the runtime stores this with DataContractJsonSerializer.
 public sealed class SaffronJournal
 {
  public string Account{get;set;}="";public string Pending{get;set;}="";public string State{get;set;}="";
  public string Reason{get;set;}="";public long BuffEnd{get;set;}public int Harvested{get;set;}
  public long At{get;set;}public long Currency{get;set;}public long[] Stocks{get;set;}=new long[0];
  public int PotatoRounds{get;set;}public int PotatoRoundLimit{get;set;}
  public bool PotatoRoundPlanted{get;set;}public bool Filling{get;set;}
  public string WorkState{get;set;}="";
 }
 public sealed class SaffronStatus
 {
  public string State{get;set;}="idle";public string Reason{get;set;}="";
  public int Harvested{get;set;}public int PotatoRounds{get;set;}
  public int Surveyed{get;set;}
  public long BuffEnd{get;set;}
  public long ServerNow{get;set;}public long WaitUntil{get;set;}
  public int EmptyFields{get;set;}public int PotatoFields{get;set;}public int PotatoRoundLimit{get;set;}
 }
 // Decisions share the server clock (milliseconds), not the desktop clock.
 public static class SaffronCycle
 {
  public const int Seed=15,Potato=2,Recipe=7,Dish=3071;
  public static readonly int[] Foods={3071,3111,3121};
  public static double HarvestSeconds(int count,double routeSeconds)=>count*2.0+routeSeconds*2+20;
  // Resume from native crops and food expiries, never from an old phase alone.
  public static string ResumePhase(int empty,int saffron,int potatoes,int other,bool saffronMature,bool continueCycle,bool replanted)
  {
   if(saffron>0)return saffronMature?"survey":replanted?"replant":"unripe";
   if(potatoes>0)return "potatoes";
   return empty>0&&continueCycle?"potatoes":"replant";
  }
  public static bool CanBuff(int count,bool allMature,bool reachable,bool capacity,bool affordable,double routeSeconds,double secondsAvailable)
   =>count>0&&allMature&&reachable&&capacity&&affordable&&routeSeconds>=0&&HarvestSeconds(count,routeSeconds)<secondsAvailable;
  public static long SellCount(long owned)=>Math.Max(0,owned-1);
  public static bool ObservePotatoRound(SaffronJournal journal,int total,int empty,int growing)
  {
   if(growing>0&&!journal.PotatoRoundPlanted){journal.PotatoRoundPlanted=true;return true;}
   if(total>0&&empty==total&&growing==0&&journal.PotatoRoundPlanted){
    journal.PotatoRoundPlanted=false;journal.Filling=false;journal.PotatoRounds++;return true;
   }
   return false;
  }
  // A round is complete only after all its planted crops have been harvested.
  // Food expiry does not change the requested number of potato rounds.
  public static string PotatoAction(int completed,int target,bool filling,int empty,int mature,int growing,bool affordable,bool capacity)
  {
   if(filling&&empty>0&&completed<target&&affordable&&capacity)return "plant";
   if(mature>0)return "harvest";
   if(growing>0)return "wait";
   if(completed>=target)return "replant";
   return empty>0&&affordable&&capacity?"plant":"limited";
  }
 }
}
