using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Proto.Net;
using Proto.Design.common;
using B=BD2Territory.Runtime.TerritoryBindings;
namespace BD2Territory.Runtime
{
 internal sealed partial class RuntimeEngine
 {
  private CookingProgress cooking;private string cookingPath="";private long lastCookAttempt;
  private bool CookingBusy()=>cooking!=null&&cooking.Pending;
  private void LoadCooking(string key)
  {
   cookingPath=Path.Combine(LocalStorage.DataRoot,"cooking-"+key+".json");cooking=ReadLayoutFile<CookingProgress>(cookingPath)??new CookingProgress{Account=key};
   if(cooking.Schema!=1||cooking.Account!=key||cooking.ConfirmedTotal<0||!new[]{"idle","pending","confirmed","rejected"}.Contains(cooking.State)||(cooking.Pending&&(cooking.Count<1||cooking.Count>1000||cooking.Recipe<1||cooking.ResultItem<1||string.IsNullOrEmpty(cooking.Token)||cooking.SubmittedTicks<=0)))throw new InvalidOperationException("Cooking journal identity mismatch");
  }
  private void SettleCooking()
  {
   if(cooking==null||!cooking.Pending)return;var reply=network.LastCookReply;if(reply==null||reply.Token!=cooking.Token)return;
   CookingPlanner.Confirm(cooking,reply);LocalStorage.WriteJsonAtomically(cookingPath,cooking);lastCrops=0;lastCatalog=0;LocalStorage.Log("Cooking "+cooking.State+" recipe="+cooking.Recipe+" count="+cooking.Count);
  }
  private int CookingCount(int recipeId,int limit)
  {
   var r=(LifeCookTable)B.Invoke("Tables.Cook",recipeId);if(r==null)return 0;
   var item=(LifeItemTable)B.Invoke("Tables.ItemById",r.ResultItemId);if(item==null||!(bool)B.Invoke("Layout.Unlocked",item.UnlockContentType,item.UnlockContentLevel))return 0;
   var items=B.Read("Inventory.Items",null) as IEnumerable<ItemDBInfo>;var defaults=(LifeDefaultTable)B.Read("Tables.Default",null);
   if(items==null||defaults==null||items.Count()>=defaults.DefaultConsumableInvenSlotCount)return 0;
   int owned=Convert.ToInt32(B.Invoke("Inventory.Count",item.Id));int capacity=Math.Max(0,item.StackCount-owned);
   var stocks=r.RequireItemUniqueId.Select(id=>((IEnumerable<int>)B.Invoke("Tables.ItemIds",id)).Sum(v=>Convert.ToInt64(B.Invoke("Inventory.Count",v)))).ToArray();
   return CookingPlanner.Count(limit,capacity,stocks,r.RequireItemCount.ToArray());
  }
  private bool CookingTick(TerritorySnapshot s,TerritoryControl c,long now)
  {
   if(cooking==null)return false;s.Cooked=cooking.ConfirmedTotal;s.CookingState=cooking.State;
   if(cooking.Pending){StopMotion();s.Reason="等待料理制作确认";if(now-cooking.SubmittedTicks>TimeSpan.FromSeconds(30).Ticks)throw new InvalidOperationException("料理结果未知，已保留记录；请核对游戏库存，不会重复制作");return true;}
   if(!c.Cooking||now-lastCookAttempt<TimeSpan.FromSeconds(5).Ticks)return false;lastCookAttempt=now;
   int count=CookingCount(c.RecipeId,c.CookingBatch);s.Cookable=count;if(count<=0)return false;
   var r=(LifeCookTable)B.Invoke("Tables.Cook",c.RecipeId);var selected=new List<ItemDBInfo>();
   for(int i=0;i<r.RequireItemUniqueId.Count;i++)
   {
    var ids=((IEnumerable<int>)B.Invoke("Tables.ItemIds",r.RequireItemUniqueId[i])).ToList();int needed=checked(r.RequireItemCount[i]*count);
    var part=(List<ItemDBInfo>)B.Invoke("Cooking.SelectItems",ids,needed);
    if(part==null||part.Any(v=>!ids.Contains(v.Id)||v.Count<=0)||part.Sum(v=>v.Count)!=needed)throw new InvalidOperationException("料理原料选择与配方不一致");selected.AddRange(part);
   }
   StopMotion();cooking=new CookingProgress{Account=account,Token=Guid.NewGuid().ToString("N"),Recipe=c.RecipeId,ResultItem=r.ResultItemId,Count=count,SubmittedTicks=now,ConfirmedTotal=cooking.ConfirmedTotal,State="pending"};
   // Persist intent before invoking the game's own sender; an uncertain result must never be replayed.
   LocalStorage.WriteJsonAtomically(cookingPath,cooking);network.ArmCooking(cooking);lastInput=now;
   B.Invoke("Cooking.Make",r.Id,count,selected,(Action)(()=>{}));s.Reason="正在制作所选料理";s.Target=recipes.FirstOrDefault(v=>v.Id==c.RecipeId)?.Name??c.RecipeId.ToString();return true;
  }
 }
}
