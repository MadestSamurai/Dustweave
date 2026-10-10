using System;
using Proto.Local;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Proto.Design.common;
using gamfs;
namespace BD2Daily.Live {
 [DataContract] internal sealed class TradeOfferObservation {
  [DataMember] public int Shop,Product,Item,Type,PriceType,Price,BasePrice,Limit,Remaining,NoBargain,Rate,ReputationDiscount;
 }
 [DataContract] internal sealed class TradeQuoteObservation {
  [DataMember] public int Shop,Item,Price,Rate;
 }
 [DataContract] internal sealed class TradeSupplyObservation {
  [DataMember] public string Date;
  [DataMember] public long ServerTicks;
  [DataMember] public bool BargainActive;
  [DataMember] public int BargainPercent;
  [DataMember] public int[] AvailableShops,UnavailableShops;
  [DataMember] public TradeOfferObservation[] Offers;
  [DataMember] public TradeQuoteObservation[] Quotes;
  [DataMember] public long[][] Favorites;
 }
 [DataContract] internal sealed class MerchantNavigationState {
  [DataMember] public int Npc,Instance;
  [DataMember] public bool Near,Reachable;
  [DataMember] public float Distance,X,Y,Z;
  [DataMember] public string Reason;
 }
 // Read-only source adaptation of ShopUI's native availability and pricing paths.
 // Does not send network requests, select products, or change UI state.
 internal static class TradeObservation {
  internal static NPCController Merchant(){
   if(!Singleton<PackManager>.ὪὫὢὨὯὭὦὪὦὨὣ.IsSquarePack())throw new InvalidOperationException("Merchant requires square");
   return Bridge.Find(typeof(NPCController)).Cast<NPCController>().Single(x=>x.ὠὫὥὧὭὭὥὠὭὤὣ!=null&&x.ὩὥὬὩὩὠὠὤὪὡὡ.Any(m=>m.ὫὠὫὯὠὢὥὧὢὥὥ==ὪὪὫὣὤὯὡὧὥὥὮ.Shop&&m.ὭὩὪὯὯὬὧὠὠὬὠ>0&&!new[]{3001,3003,3010}.Contains(m.ὭὩὪὯὯὬὧὠὠὬὠ)));
  }
  internal static MerchantNavigationState Navigation(){
   var merchant=Merchant();var player=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὪὨὯὢὫὮὨὩὮὡὬ;
   float distance=UnityEngine.Vector3.Distance(player.transform.position,merchant.ὩὬὧὢὧὬὠὡὦὥὧ);string reason="astar_planned_on_start";
   bool reachable=player.ὭὦὫὤὮὧὦὡὨὤὢ.ὬὨὡὫὭὮὠὡὧὦὭ!=null;
   var p=player.transform.position;
   return new MerchantNavigationState{Npc=merchant.ὠὫὥὧὭὭὥὠὭὤὣ.ὯὫὪὡὭὤὪὮὫὬὣ,Instance=merchant.GetInstanceID(),Near=merchant.ὩὤὨὮὥὦὫὭὫὭὨ,Reachable=reachable,Distance=distance,Reason=reason,X=p.x,Y=p.y,Z=p.z};
  }
  internal static TradeSupplyObservation Capture() {
   var ui=Bridge.Find(typeof(ShopUI)).Cast<ShopUI>().Single();
   var now=ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.Now();
   var active=ὨὭὨὨὯὬὣὠὪὦὬ.ὣὮὥὨὣὯὥὣὧὭὢ;
   var discountField=typeof(ShopUI).GetField("ὫὭὥὤὫὫὯὪὠὣὤ",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public);
   if(discountField==null)throw new MissingFieldException("ShopUI", "discountPercentByTalent");
   int discount=(int)discountField.GetValue(ui);
   if(active&&(discount<1||discount>60))throw new InvalidOperationException("Active shop discount is unknown");
   var rows=new List<TradeOfferObservation>();var available=new List<int>();var unavailable=new List<int>();
   int map=GameFieldManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὮὬὬὮὠὮὪὠὧὩὪ.MapGroupId;
   var common=ὨὭὨὨὯὬὣὠὪὦὬ.ὪὥὦὡὭὡὪὫὥὨὦ(1);
   foreach(var shop in ui.ὫὨὧὫὯὭὨὢὦὬὪ) {
    PackTable pack;
    if(!ὦὡὡὩὣὧὬὯὠὩὢ.ὩὦὭὡὯὤὮὩὧὯὢ(shop,out pack)){unavailable.Add(shop.Id);continue;}
    var observed=ὨὭὨὨὯὬὣὠὪὦὬ.ὪὥὦὡὭὡὪὫὥὨὦ(shop.Id);
    TradeStockRules.RequireFresh(shop.Id, observed==null?(long?)null:observed.ShopRemainTick, common==null?(long?)null:common.ShopRemainTick, now.Ticks);
    available.Add(shop.Id);
    int reputation=ὫὢὥὧὠὢὣὣὮὢὦ.ὪὫὢὨὯὭὦὪὦὨὣ.ὯὨὣὪὧὡὯὢὠὯὬ(map,shop.PackId);
    foreach(var product in ὦὡὡὩὣὧὬὯὠὩὢ.ὭὨὫὤὬὢὥὮὠὥὦ(shop.Id)) {
     int kind=product.ὢὭὩὠὪὯὮὧὩὣὧ;
     if(kind!=5&&kind!=12)continue;
     int rate=ὨὭὨὨὯὬὣὠὪὦὬ.ὧὥὭὭὡὮὬὬὧὥὣ(ὨὭὨὨὯὬὣὠὪὦὬ.ὡὡὣὩὪὭὯὭὦὯὥ,ὮὫὯὥὩὯὯὠὤὮὯ.Buy,shop.Id,product);
     int price=(int)((float)(product.ὩὨὢὫὭὢὥὨὮὡὤ*rate)/100f);
     if(active&&product.ὢὭὠὡὬὢὡὦὡὭὪ==0)price=(int)((float)product.ὩὨὢὫὭὢὥὨὮὡὤ*((float)(100-discount)/100f));
     else if(reputation>0)price=(int)((float)(price*(100-reputation))/100f);
     var purchased=observed==null?null:observed.ProductInfo.SingleOrDefault(p=>p.Id==product.ὯὫὪὡὭὤὪὮὫὬὣ);
     bool unlimited=product.ὯὯὢὨὢὦὡὨὤὢὬ();
     int limit=unlimited?ὨὭὭὯὧὢὣὢὤὫὥ.ὬὥὠὭὮὨὩὬὮὤὮ:product.ὠὥὬὠὨὧὡὩὨὤὯ;
     int remaining=TradeStockRules.Remaining(limit,unlimited,purchased==null?(int?)null:purchased.BuyCount);
     rows.Add(new TradeOfferObservation { Shop=shop.Id,Product=product.ὯὫὪὡὭὤὪὮὫὬὣ,Item=product.ὣὬὭὧὨὨὧὬὮὪὢ,Type=kind,
      PriceType=product.ὩὫὠὣὤὣὭὭὫὦὢ,Price=price,BasePrice=product.ὩὨὢὫὭὢὥὨὮὡὤ,Limit=limit,Remaining=remaining,NoBargain=product.ὢὭὠὡὬὢὡὦὡὭὪ,Rate=rate,ReputationDiscount=reputation });
    }
   }
   var quotes=new List<TradeQuoteObservation>();
   foreach(var item in ὦὡὡὩὣὧὬὯὠὩὢ.ὠὧὪὠὢὨὪὡὢὫὫ()) {
    if(item.ὢὭὩὠὪὯὮὧὩὣὧ!=5||!available.Contains(item.ὤὮὣὬὦὥὣὤὤὩὣ))continue;
    int shop=item.ὤὮὣὬὦὥὣὤὤὩὣ;
    int rate=ὨὭὨὨὯὬὣὠὪὦὬ.ὧὥὭὭὡὮὬὬὧὥὣ(ὨὭὨὨὯὬὣὠὪὦὬ.ὡὡὣὩὪὭὯὭὦὯὥ,ὮὫὯὥὩὯὯὠὤὮὯ.Sell,shop,item);
    if(rate==120&&!quotes.Any(q=>q.Item==item.ὣὬὭὧὨὨὧὬὮὪὢ))quotes.Add(new TradeQuoteObservation { Shop=shop,Item=item.ὣὬὭὧὨὨὧὬὮὪὢ,Rate=rate,Price=(int)((float)(item.ὩὨὢὫὭὢὥὨὮὡὤ*rate)/100f) });
   }
   if(rows.Count>1000)throw new InvalidOperationException("Unexpected trade supply size");
   return new TradeSupplyObservation { Favorites=ClientLocalInfo.GetAllFavoriteProductIdList().Select(x=>new long[]{x.Item1,x.Item2}).ToArray(),Date=now.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture),ServerTicks=now.Ticks,BargainActive=active,BargainPercent=discount,AvailableShops=available.ToArray(),UnavailableShops=unavailable.ToArray(),Offers=rows.ToArray(),Quotes=quotes.ToArray() };
  }
 }
}
