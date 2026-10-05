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
  private SalesProgress sales;private string salesPath="";private long lastSaleScan,lastSurplusProbe,lastSalesRefresh;private int lastSurplusGather=-1;
  private bool SalesBusy()=>sales!=null&&sales.Pending;
  private ItemDBInfo[] SaleInventory()=>((IEnumerable<ItemDBInfo>)B.Read("Inventory.Items",null)??throw new InvalidOperationException("等待领地库存同步")).Select(x=>x.Clone()).ToArray();
  private long SaleCurrency()=>Convert.ToInt64(B.Invoke("Inventory.Currency",B.EnumObject("CurrencyKind","LocalMileage")));
  private void LoadSales(string key)
  {
   salesPath=Path.Combine(LocalStorage.DataRoot,"sales-"+key+".json");sales=ReadLayoutFile<SalesProgress>(salesPath)??new SalesProgress{Account=key};SurplusSales.Validate(sales,key);
  }
  private void SettleSales()
  {
   if(!SalesBusy())return;var reply=network.LastSaleReply;if(reply==null||reply.Token!=sales.Token||!reply.Matches)return;
   var user=(UserDBInfo)B.Read("Account.User",null);if(user==null||user.OwnerIndex.ToString()!=sales.Account)throw new InvalidOperationException("售卖确认期间账号发生变化，已保留记录");
   if(!SurplusSales.Confirm(sales,reply,SaleInventory().ToDictionary(x=>x.InvenIndex,x=>x.Count),SaleCurrency()))return;
   SaveSalesOutcome();lastSaleScan=DateTime.UtcNow.Ticks;lastSurplusProbe=lastSaleScan;lastSurplusGather=network.GatherReplies;lastCrops=0;lastCatalog=0;
   LocalStorage.Log("Territory sale "+sales.State+" token="+sales.Token+" items="+sales.Lines.Sum(x=>(long)x.Count)+" currency="+reply.Reward+" threshold="+sales.Threshold+" balancesMatched="+sales.BalancesMatched);
  }
  private void SaveSalesOutcome()
  {
   // Preserve evidence before the next sale overwrites the active journal.
   string history=Path.Combine(LocalStorage.DataRoot,"sales-history");Directory.CreateDirectory(history);
   LocalStorage.WriteJsonAtomically(Path.Combine(history,sales.SubmittedTicks+"-"+Guid.NewGuid().ToString("N")+".json"),sales);
   LocalStorage.WriteJsonAtomically(salesPath,sales);
  }
  private bool RecoverSales(TerritorySnapshot s,long now)
  {
   StopMotion();s.Reason="等待领地售卖确认";
   if(network.NativeBusy){s.Reason="等待游戏网络请求结束后核对售卖";return true;}
   var user=(UserDBInfo)B.Read("Account.User",null);
   if(user==null||user.OwnerIndex.ToString()!=sales.Account)throw new InvalidOperationException("售卖确认期间账号发生变化，已保留记录");
   if(SurplusSales.Reconcile(sales,network.LastSalesSnapshot,user.OwnerIndex.ToString(),true))
   {
    SaveSalesOutcome();network.FinishSalesRecovery(sales.Token);lastCrops=0;lastCatalog=0;lastWorldRead=0;lastSurplusProbe=0;
    LocalStorage.Log("Territory sale reconciled from server inventory token="+sales.Token+" currency="+sales.Recovery.Currency+" stocks="+sales.Recovery.Items.Length+"; old sale outcome remains unknown; no replay");
    s.Reason="已同步库存，继续自动化；旧售卖未重复执行";return true;
   }
   if(now-sales.SubmittedTicks<TimeSpan.FromSeconds(30).Ticks)return true;
   s.Reason="正在从服务器重新核对领地库存；不会重复旧售卖";
   if(now-lastSalesRefresh<TimeSpan.FromSeconds(30).Ticks)return true;
   if(!network.BeginSalesRefresh(sales))return true;
   lastSalesRefresh=now;LocalStorage.Log("Territory sale recovery requested token="+sales.Token+" ageSeconds="+TimeSpan.FromTicks(now-sales.SubmittedTicks).TotalSeconds);
   // The native LifeInfo callback refreshes items AND currency. LifeUserInfo alone does not refresh items.
   B.Invoke("Sales.Refresh",new object[]{null});return true;
  }
  private SaleLine[] UrgentSurplus(TerritoryControl c,long now)
  {
   if(!SurplusSales.ProbeDue(now,lastSurplusProbe,network.GatherReplies,lastSurplusGather))return new SaleLine[0];
   lastSurplusProbe=now;lastSurplusGather=network.GatherReplies;return PlanSurplus(c.SellThreshold);
  }
  private SaleLine[] PlanSurplus(int threshold)
  {
   var rows=(IEnumerable<LifeSellItemTable>)B.Invoke("Sales.Rows",1);
   if(rows==null)throw new InvalidOperationException("等待领地售卖表同步");
   var byItem=rows.Where(x=>x.GroupId==1&&x.ItemType==66&&x.PriceType==64&&x.PriceId==0&&x.ItemCount==1&&x.PriceCount>0).GroupBy(x=>x.ItemId).Where(g=>g.Count()==1).ToDictionary(g=>g.Key,g=>g.Single());
   var stocks=SaleInventory().Select(x=>{
    LifeSellItemTable row;byItem.TryGetValue(x.Id,out row);var item=(LifeItemTable)B.Invoke("Tables.ItemById",x.Id);
    return new SaleStock{Index=x.InvenIndex,Item=x.Id,Count=x.Count,Locked=x.KeepFlag!=0||x.IsDisableSlot,Kind=x.Type,Group=row==null?0:row.GroupId,Row=row==null?0:row.Id,UnitCount=row==null?0:row.ItemCount,Price=row==null?0:row.PriceCount,Eligible=row!=null&&item!=null&&item.Type>=1&&item.Type<=3};
   }).ToArray();
   return SurplusSales.Plan(stocks,threshold);
  }
  private bool SalesTick(TerritorySnapshot s,TerritoryControl c,long now,SaleLine[] prepared=null)
  {
   if(sales==null)return false;
   if(sales.Pending)return RecoverSales(s,now);
   if(network.NativeBusy||network.Waiting)return false;
   if(prepared==null&&now-lastSaleScan<TimeSpan.FromSeconds(5).Ticks)return false;lastSaleScan=now;
   var lines=prepared??PlanSurplus(c.SellThreshold);if(lines.Length==0)return false;
   // No gathering/cooking/planting is active here. Recheck the lease before any irreversible request.
   var live=control;if(live==null||!live.Valid(DateTime.UtcNow.Ticks,pid)||live.OwnerId!=c.OwnerId||live.SellThreshold!=c.SellThreshold)return false;
   StopMotion();sales=new SalesProgress{Account=account,Token=Guid.NewGuid().ToString("N"),State="pending",Threshold=c.SellThreshold,SubmittedTicks=now,CurrencyBefore=SaleCurrency(),Lines=lines,ConfirmedItems=sales.ConfirmedItems,ConfirmedCurrency=sales.ConfirmedCurrency};
   SurplusSales.Validate(sales,account);LocalStorage.WriteJsonAtomically(salesPath,sales);network.ArmSales(sales);lastInput=now;
   var request=lines.Select(x=>new SellItemInfo{InvenIndex=x.Index,GroupId=x.Group,Id=x.Row,SellCount=x.Count}).ToList();
   LocalStorage.Log("Territory sale prepared token="+sales.Token+" lines="+lines.Length+" items="+lines.Sum(x=>(long)x.Count)+" threshold="+sales.Threshold+" balancesMatched="+sales.BalancesMatched);
   B.Invoke("Sales.Send",request,null);s.Reason="正在售卖超过保留数量的领地物品";s.Target="";return true;
  }
 }
}
