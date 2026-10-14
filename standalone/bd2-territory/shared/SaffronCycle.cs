using System;
using System.Collections.Generic;
using System.Linq;
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
 public sealed class SaffronFoodRecipe
 {
  public int Food,Recipe;public int[] Ingredients=new int[0],Counts=new int[0];
 }
 public sealed class SaffronFoodPreparation
 {
  public int Food,Recipe;public bool Cook=>Recipe>0;
 }
 // Decisions share the server clock (milliseconds), not the desktop clock.
 public static class SaffronCycle
 {
  public const int Seed=15,Potato=2,Recipe=7,Dish=3071;
  public static readonly int[] Foods={3071,3111,3121};
  // Only missing servings are supplied here. Reserve shared ingredients once;
  // execution re-plans from native inventory after each confirmed serving.
  public static SaffronFoodPreparation[] PrepareFoods(SaffronFoodRecipe[] missing,IDictionary<int,long> stocks)
  {
   var available=new Dictionary<int,long>(stocks);var plan=new List<SaffronFoodPreparation>();
   if(missing.Select(x=>x.Food).Distinct().Count()!=missing.Length||missing.Any(x=>!Foods.Contains(x.Food)))throw new ArgumentException("Invalid yield food request");
   foreach(var food in missing){
    if(food.Recipe<0||food.Ingredients.Length!=food.Counts.Length||food.Counts.Any(x=>x<=0)||food.Ingredients.Any(x=>x<=0))throw new ArgumentException("Invalid yield food recipe");
    var needs=food.Ingredients.Select((id,i)=>new{Id=id,Count=food.Counts[i]}).GroupBy(x=>x.Id).ToDictionary(x=>x.Key,x=>x.Sum(v=>(long)v.Count));
    bool cook=food.Recipe>0&&needs.Count>0&&needs.All(x=>available.ContainsKey(x.Key)&&available[x.Key]>=x.Value);
    if(cook)foreach(var need in needs)available[need.Key]-=need.Value;
    plan.Add(new SaffronFoodPreparation{Food=food.Food,Recipe=cook?food.Recipe:0});
   }
   return plan.ToArray();
  }
  public static bool PreparedFoodConfirmed(int food,long[] before,long[] after)
  {
   int index=Array.IndexOf(Foods,food);
   return index>=0&&before!=null&&after!=null&&before.Length==Foods.Length&&after.Length==Foods.Length&&after[index]>before[index];
  }
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
  public static SaleLine[] PremiumSales(SaleStock[] stocks,IDictionary<int,int> saffronGrades)
   =>stocks.Where(x=>x.Count>0&&x.Eligible&&!x.Locked&&saffronGrades.ContainsKey(x.Item)&&(saffronGrades[x.Item]==2||saffronGrades[x.Item]==3))
    .OrderBy(x=>x.Index).Select(x=>new SaleLine{Index=x.Index,Item=x.Item,Count=x.Count,Before=x.Count,Group=x.Group,Row=x.Row,Price=x.Price}).ToArray();
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
