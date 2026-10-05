using System;
using System.Linq;
namespace BD2Territory
{
 // One native confirmation plants the actual preview group. Balance expected ingredient output, not batch counts.
 public sealed class RecipeBatchProgress
 {
  public int Schema{get;set;}=4;public string Account{get;set;}="";public int RecipeId{get;set;}=3;
  public int FixedSeedId{get;set;}
  public int SeedId{get;set;}public int PlannedCount{get;set;}public int Planted{get;set;}public int Batches{get;set;}
  public string[] PendingKeys{get;set;}=new string[0];public int PendingSeed{get;set;}
  public string PendingToken{get;set;}="";public int PendingCost{get;set;}public string PendingOwner{get;set;}="";
  public string BudgetOwner{get;set;}="";public long Spent{get;set;}
  public RecipeBatchProgress Copy(){var next=(RecipeBatchProgress)MemberwiseClone();next.PendingKeys=(string[])PendingKeys.Clone();return next;}
  public string LastReceipt{get;set;}="";
 }
 public static class RecipeBatchPlanner
 {
  // Upgrade only the immediately preceding journal format, preserving unresolved payments.
  public static RecipeBatchProgress Restore(RecipeBatchProgress p,string account)
  {
   if(p==null||p.Account!=account||string.IsNullOrEmpty(account)||p.RecipeId<=0||p.FixedSeedId<0||p.SeedId<0||p.Batches<0||p.Spent<0||p.PendingKeys==null||p.PendingToken==null||p.PendingOwner==null||p.BudgetOwner==null||p.LastReceipt==null)throw new InvalidOperationException("种植进度身份不匹配");
   var next=p.Copy();
   if(next.Schema==3)
   {
    if(next.Planted!=0&&next.Planted!=100||next.PendingToken.Length>0&&next.PendingKeys.Length!=100)throw new InvalidOperationException("旧播种进度不完整，保留记录等待核对");
    next.PlannedCount=next.PendingToken.Length>0||next.Planted==100?100:0;next.Schema=4;
   }
   if(next.Schema!=4||next.PlannedCount<0||next.Planted<0||next.Planted!=0&&next.Planted!=next.PlannedCount||next.PlannedCount>0&&next.SeedId<=0)throw new InvalidOperationException("种植批次进度异常");
   if(next.PendingToken.Length>0&&(next.Planted!=0||!PlantingTransaction.ValidKeys(next.PendingKeys)||next.PendingKeys.Length!=next.PlannedCount||next.PendingSeed!=next.SeedId||next.PendingSeed<=0||next.PendingCost<=0||next.PendingCost%next.PlannedCount!=0||string.IsNullOrEmpty(next.PendingOwner)))throw new InvalidOperationException("待确认播种记录不完整，保留记录等待核对");
   return next;
  }
  public static int Choose(RecipeBatchProgress progress,CropStock[] crops)
  {
   if(crops==null||crops.Length==0||crops.Length>20||crops.Any(c=>c==null)||crops.Select(c=>c.SeedId).Distinct().Count()!=crops.Length||crops.Any(c=>c.Required<=0||c.Yield<=0||c.Inventory<0||c.Growing<0))throw new InvalidOperationException("料理配方或库存不完整");
   if(!string.IsNullOrEmpty(progress.PendingToken))throw new InvalidOperationException("上次播种尚未确认，不能安排新批次");
   if(progress.PlannedCount<0||progress.Planted<0||progress.Planted!=0&&progress.Planted!=progress.PlannedCount)throw new InvalidOperationException("批次进度异常");
   if(progress.SeedId>0&&progress.Planted==0)
   {if(!crops.Any(c=>c.SeedId==progress.SeedId))throw new InvalidOperationException("当前批次作物不属于配方");return progress.SeedId;}
   // Add the expected output of plants already in the ground exactly once; ignore sell price for crop selection.
   var chosen=crops.OrderBy(c=>(c.Inventory+(double)c.Growing*c.Yield)/c.Required).ThenByDescending(c=>c.Required).ThenBy(c=>c.SeedId).First();
   progress.SeedId=chosen.SeedId;progress.PlannedCount=0;progress.Planted=0;return chosen.SeedId;
  }
  public static bool SwitchRecipe(RecipeBatchProgress p,int recipeId,bool batchActive)
  {
   return SwitchPlanting(p,recipeId,0,batchActive);
  }
  public static bool SwitchPlanting(RecipeBatchProgress p,int recipeId,int fixedSeedId,bool batchActive)
  {
   if(recipeId<=0||fixedSeedId<0)throw new ArgumentException("配方或作物编号无效");
   if(p.RecipeId==recipeId&&p.FixedSeedId==fixedSeedId)return true;
   if(batchActive||!string.IsNullOrEmpty(p.PendingToken))return false;
   p.RecipeId=recipeId;p.FixedSeedId=fixedSeedId;p.SeedId=0;p.PlannedCount=0;p.Planted=0;return true;
  }
  public static bool Confirm(RecipeBatchProgress p,int seed,int count,string receipt)
  {
   if(string.IsNullOrEmpty(receipt))throw new ArgumentException("缺少服务器回执身份");
   if(p.LastReceipt==receipt)return false;
   if(seed<=0||seed!=p.SeedId||p.Planted!=0||p.PlannedCount<=0||count!=p.PlannedCount)throw new InvalidOperationException("播种回执与当前批次不一致");
   p.Planted+=count;p.LastReceipt=receipt;p.Batches++;return true;
  }
 }
}
