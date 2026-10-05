using System;
namespace BD2Territory
{
 public static class TerritoryIdentity
 {
  public const string LiveEntries="runtime.json|control.json|latest.json|layout-request.json|layout-status.json|layout-world.json";
  public const string RuntimeName="BD2Territory.Runtime26";
  public static string DataRoot=>System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2Territory");
  public static bool IsGameProcessName(string name)=>string.Equals(name,"BrownDust II",StringComparison.OrdinalIgnoreCase)||string.Equals(name,"BrownDust II.exe",StringComparison.OrdinalIgnoreCase);
 }
 public sealed class TerritoryRuntimeStatus { public string State{get;set;}="";public string Error{get;set;}="";public string Runtime{get;set;}="";public string AtUtc{get;set;}="";public int ProcessId{get;set;} }
 public class TerritorySettings
 {
  public bool Logging{get;set;}=true;public bool Mining{get;set;}=true;public bool Farming{get;set;}=true;
  public bool UseNavMesh{get;set;}=false;
  public bool DashRecovery{get;set;}=true;public bool UseVehicle{get;set;}=true;
  public bool FixedCrop{get;set;}=false;public int FixedSeedId{get;set;}=0;
  // Capacity protection is mandatory; ignore the obsolete saved opt-out.
  public bool AutoSell{get{return true;}set{}}public int SellThreshold{get;set;}=9900;
  public int RecipeId{get;set;}=3;public bool Cooking{get;set;}=false;public int CookingBatch{get;set;}=100;
  public int IntervalMs{get;set;}=500;public long PlantingBudget{get;set;}=1400;
  public bool ValidSettings()=>SurplusSales.ValidThreshold(SellThreshold)&&FixedSeedId>=0&&CookingBatch>=1&&CookingBatch<=1000&&RecipeId>0&&IntervalMs>=100&&IntervalMs<=60000&&PlantingBudget>=0&&PlantingBudget<=10000000;
 }
 public sealed class TerritoryControl:TerritorySettings
 {
  public string LayoutToken{get;set;}="";
  public string OwnerId{get;set;}="";public int ProcessId{get;set;}public long UntilUtcTicks{get;set;}public bool Enabled{get;set;}
  public bool Valid(long now,int pid)=>Enabled&&!string.IsNullOrEmpty(OwnerId)&&ProcessId==pid&&UntilUtcTicks>now&&UntilUtcTicks<=now+TimeSpan.FromSeconds(15).Ticks&&ValidSettings();
 }
 public sealed class CropStock
 {
  public int SeedId{get;set;}public int IngredientId{get;set;}public string Name{get;set;}="";public int Required{get;set;}public int Yield{get;set;}=1;public long Inventory{get;set;}public int Growing{get;set;}
  public int Price{get;set;}public int GrowthSeconds{get;set;}
 }
 public sealed class TerritorySnapshot
 {
  public int Schema{get;set;}=1;public string Runtime{get;set;}=TerritoryIdentity.RuntimeName;public int ProcessId{get;set;}public long CapturedUtcTicks{get;set;}
  public bool Ready{get;set;}public bool Enabled{get;set;}public string OwnerId{get;set;}="";public string Scene{get;set;}="";public string Reason{get;set;}="等待连接";public string Error{get;set;}="";public string Target{get;set;}="";public string Network{get;set;}="";
  public string FarmState{get;set;}="";
  public string WorkPhase{get;set;}="";public int WorkRemaining{get;set;}
  public int RetryTargets{get;set;}public int Trees{get;set;}public int Ores{get;set;}public int Mature{get;set;}public int Fields{get;set;}public int EmptyFields{get;set;}
  public int GatherReplies{get;set;}public int ReceivedItems{get;set;}public long Spent{get;set;}public int BatchSeedId{get;set;}public int BatchPlanted{get;set;}public int BatchTotal{get;set;}public int CompletedBatches{get;set;}
  public RecipeOption[] Seeds{get;set;}=new RecipeOption[0];public int ActiveFixedSeedId{get;set;}
  public RecipeOption[] Recipes{get;set;}=new RecipeOption[0];public int ActiveRecipeId{get;set;}
  public long SoldItems{get;set;}public long SaleCurrency{get;set;}public string SalesState{get;set;}="";
  public int Cookable{get;set;}public long Cooked{get;set;}public string CookingState{get;set;}="";
  public CropStock[] Crops{get;set;}=new CropStock[0];
 }
}
