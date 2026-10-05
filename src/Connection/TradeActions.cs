using System;
using Proto.Local;
using gamfs;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Proto.Net;
using Proto.Design.common;
using Product=ὢὫὡὥὩὠὧὫὧὨὤ;
using Mode=ὮὫὯὥὩὯὯὠὤὮὯ;
namespace BD2Daily.Live {
 // Only normal UI preview/confirmation paths. No custom requests or inventory writes.
 internal static class TradeActions {
  const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
  static object Get(object o,string name){var t=o.GetType();while(t!=null){var f=t.GetField(name,Flags);if(f!=null)return f.GetValue(o);t=t.BaseType;}throw new MissingFieldException(name);}
  static object Call(object o,string name,params object[] args){return o.GetType().GetMethod(name,Flags).Invoke(o,args);}
  static void Require(bool b,string error){if(!b)throw new InvalidOperationException(error);}
  static void Click(UIBase ui,string field){ui.OnClickUI((GameObject)Get(ui,field));}
  static int Discount(ShopUI shop){return (int)Get(shop,"ὫὭὥὤὫὫὯὪὠὣὤ");}
  static ShopUI Shop(){return Bridge.Find(typeof(ShopUI)).Cast<ShopUI>().Single();}
  static Dictionary<int,List<Product>> Favorites(Command c,bool add) {
   var shop=Shop();var observed=TradeObservation.Capture();
   Require(shop.ὯὣὡὫὨὫὡὧὩὬὠ==Mode.Buy,"Purchase tab required");
   long total=0;var keys=new HashSet<string>();
   for(int i=0;i<c.Items.Length;i+=4){int sid=checked((int)c.Items[i]),pid=checked((int)c.Items[i+1]),qty=checked((int)c.Items[i+2]),price=checked((int)c.Items[i+3]);
    var offer=observed.Offers.Single(x=>x.Shop==sid&&x.Product==pid);
    Require(keys.Add(sid+":"+pid)&&offer.Type==5&&offer.PriceType==4&&offer.Remaining==qty&&qty>0&&offer.Price==price,"Favorite plan differs from current full stock/price");
    total=checked(total+(long)qty*price);
   }
   Require(total==c.Value&&total>0&&ὣὡὧὡὦὣὣὬὨὪὫ.ὭὨὣὥὧὨὮὪὯὯὠ(ὥὯὯὠὣὪὦὢὫὠὮ.Gold)>=total,"Favorite total/balance changed");
   if(add)for(int i=0;i<c.Items.Length;i+=4)ClientLocalInfo.AddFavorite((int)c.Items[i],(int)c.Items[i+1]);
   var args=new object[]{null,0};Require((int)Call(shop,"ὪὩὬὦὢὡὧὡὪὨὠ",args)==0,"Native favorite list unavailable");
   var all=(Dictionary<int,List<Product>>)args[0];var selected=new Dictionary<int,List<Product>>();
   foreach(var pair in all){var rows=pair.Value.Where(p=>keys.Contains(pair.Key+":"+p.ὯὫὪὡὭὤὪὮὫὬὣ)).ToList();if(rows.Count>0)selected.Add(pair.Key,rows);}
   Require(selected.Sum(x=>x.Value.Count)==keys.Count,"Missing selected favorite");
   Call(shop,"ὥὮὢὥὥὤὤὪὯὦὣ",selected);
   foreach(var pair in selected)foreach(var p in pair.Value){var o=observed.Offers.Single(x=>x.Shop==pair.Key&&x.Product==p.ὯὫὪὡὭὤὪὮὫὬὣ);Require(p.ὣὡὢὩὥὥὨὯὩὥὯ==o.Price,"Native favorite price changed");}
   return selected;
  }
  static string Signature(Dictionary<int,List<Product>> d){return string.Join("|",d.SelectMany(x=>x.Value.Select(p=>x.Key+":"+p.ὯὫὪὡὭὤὪὮὫὬὣ+":"+p.ὠὥὬὠὨὧὡὩὨὤὯ+":"+p.ὣὡὢὩὥὥὨὯὩὥὯ+":"+p.ὤὧὤὬὧὫὡὥὮὫὩ)).OrderBy(x=>x));}
  static Product BuyProduct(Command c,bool refresh){var shop=Shop();var o=TradeObservation.Capture().Offers.Single(x=>x.Shop==c.Items[0]&&x.Product==c.Items[1]);
   Require(shop.ὯὣὡὫὨὫὡὧὩὬὠ==Mode.Buy&&(o.Type==5||o.Type==12)&&o.PriceType==4&&c.Items[2]>0&&c.Items[2]<=o.Remaining&&c.Items[3]==o.Price,"Purchase stock/price changed");
   Require(c.Value==checked(c.Items[2]*c.Items[3])&&ὣὡὧὡὦὣὣὬὨὪὫ.ὭὨὣὥὧὨὮὪὯὯὠ(ὥὯὯὠὣὪὦὢὫὠὮ.Gold)>=c.Value,"Purchase budget changed");
   if(refresh)shop.RefreshShop((int)c.Items[0],Discount(shop));
   Require(shop.ὬὥὫὥὮὤὭὥὪὥὣ==c.Items[0],"Wrong shop");
   var p=shop.GetProducts().Single(x=>x.ὯὫὪὡὭὤὪὮὫὬὣ==c.Items[1]);Require(p.ὣὡὢὩὥὥὨὯὩὥὯ==o.Price,"Displayed purchase price changed");return p;
  }
  static Product SellProduct(Command c,bool refresh){var shop=Shop();var n=TradeObservation.Capture();
   Require(shop.ὯὣὡὫὨὫὡὧὩὬὠ==Mode.Sell,"Sale tab required");
   var quote=n.Quotes.Single(x=>x.Shop==c.Items[0]&&x.Item==c.Items[1]);Require(quote.Rate==120&&quote.Price==c.Items[4]&&c.Items[3]>0&&c.Value==checked(c.Items[3]*c.Items[4]),"120% sale quote changed");
   var inventory=(IDictionary)typeof(ὣὡὧὡὦὣὣὬὨὪὫ).GetField("ὧὥὤὯὩὤὬὯὦὤὮ",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).GetValue(null);
   var item=((IDictionary)inventory[ὥὯὯὠὣὪὦὢὫὠὮ.Food]).Values.Cast<ItemDBInfo>().Single(x=>x.InvenIndex==c.Items[2]);
   Require(item.Id==c.Items[1]&&item.KeepFlag==0&&item.Count>=c.Items[3],"Sale stack missing/protected");
   if(refresh)shop.RefreshShop((int)c.Items[0],Discount(shop));
   Require(shop.ὬὥὫὥὮὤὭὥὪὥὣ==c.Items[0],"Wrong sale shop");
   var p=shop.GetProducts().Single(x=>x.ὯὠὥὣὪὭὨὥὫὩὮ==c.Items[2]);Require(p.ὣὡὢὩὥὥὨὯὩὥὯ==quote.Price&&p.ὤὧὤὬὧὫὡὥὮὫὩ==120,"Displayed sale price changed");return p;
  }
  internal static void Execute(Command c,UIBase ui){
   if(c.Kind=="trade_menu"){
    Require(BattlePlayManager.ὪὫὢὨὯὭὦὪὦὨὣ.ὨὢὤὪὪὢὩὢὬὠὦ.ToString()=="BMT_NONE","Field idle required");
    ὩὭὨὪὨὨὮὣὪὣὥ.ὢὣὠὮὩὭὧὭὦὪὨ(delegate(QuickMenuUI menu){menu.SetSpecificTalentSkill((ὪὯὯὢὨὮὨὠὥὤὬ)7);menu.SetMenuByPlayer();});
   }else if(c.Kind=="trade_talent"){
    var row=Bridge.Find(typeof(QuickMenuTalentSkillLoopScrollItem)).Cast<QuickMenuTalentSkillLoopScrollItem>().Single(x=>x.GetInstanceID()==c.Value);
    Require(row.GetComponentInParent<QuickMenuUI>()==ui,"Talent menu changed");var skill=(TalentSkillTable)Get(row,"ὣὡὪὭὤὨὭὣὪὧὩ");
    Require(skill.ClassType==7||skill.ClassType==16,"Not a trade talent");
    Require(!((GameObject)Get(row,"_objDisableImage")).activeSelf&&!((GameObject)Get(row,"_goBlock")).activeSelf,"Native talent disabled");row.OnClick();
   }else if(c.Kind=="trade_favorite_preview"){
    var dict=Favorites(c,true);ὩὭὨὪὨὨὮὣὪὣὥ.ὢὣὠὮὩὭὧὭὦὪὨ(delegate(BuyFavoritePopupUI popup){popup.SetUI(dict,dict.Sum(x=>x.Value.Count));});
   }else if(c.Kind=="trade_favorite_confirm"){
    var expected=Favorites(c,false);var actual=(Dictionary<int,List<Product>>)Get(ui,"ὤὦὣὥὭὣὫὨὡὢὮ");
    Require(Signature(actual)==Signature(expected)&&!(bool)Get(ui,"ὧὮὮὯὠὬὯὥὧὥὤ"),"Favorite popup changed/busy");Click(ui,"_buttonBuy");
   }else if(c.Kind=="trade_buy_preview"||c.Kind=="trade_sell_preview"){
    bool buy=c.Kind=="trade_buy_preview";var p=buy?BuyProduct(c,true):SellProduct(c,true);var shop=Shop();int qty=checked((int)c.Items[buy?2:3]);
    int max=buy?TradeObservation.Capture().Offers.Single(x=>x.Shop==c.Items[0]&&x.Product==c.Items[1]).Remaining:p.ὤὦὭὧὩὩὬὡὩὥὯ;
    ὩὭὨὪὨὨὮὣὪὣὥ.ὢὣὠὮὩὭὧὭὦὪὨ(delegate(ShopPopupUI popup){popup.SetShopPopup(p,max,(int)c.Items[0],buy?Mode.Buy:Mode.Sell,shop.ὩὤὬὪὤὦὧὭὣὥὠ);((SliderSelectCount)Get(popup,"_sliderCount")).SetCountByMinMax(qty);});
   }else if(c.Kind=="trade_buy_confirm"||c.Kind=="trade_sell_confirm"){
    bool buy=c.Kind=="trade_buy_confirm";var p=buy?BuyProduct(c,false):SellProduct(c,false);int qty=checked((int)c.Items[buy?2:3]);
    Require((int)Get(ui,"ὯὥὬὦὮὫὩὣὪὫὠ")==c.Items[0]&&(int)Get(ui,"ὯὬὪὥὦὪὨὫὯὠὮ")==p.ὣὬὭὧὨὨὧὬὮὪὢ&&(int)Get(ui,"ὠὡὫὥὣὪὬὪὡὢὦ")==c.Items[buy?3:4],"Product popup changed");
    Require((long)Get(ui,"ὪὦὯὢὪὩὯὧὠὬὠ")== (buy?0:c.Items[2])&&((SliderSelectCount)Get(ui,"_sliderCount")).ὮὢὥὯὥὧὤὭὨὨὪ==qty,"Product quantity/stack changed");
    if(buy)Require((int)Get(ui,"ὢὭὣὥὯὯὧὬὧὡὪ")==c.Items[1],"Product ID changed");Click(ui,"_objSellButton");
   }else if(c.Kind=="trade_cook_preview"){
    var menu=(CookingSelectUI)ui;var recipe=menu.CookingRecipeDatas.Single(x=>x.ὣὭὧὢὦὨὩὯὢὦὨ==c.Value);
    Require(recipe.ὡὭὮὧὩὯὡὬὥὢὤ&&recipe.ὯὩὥὢὯὮὢὠὠὣὪ&&recipe.ὬὤὨὤὥὤὫὮὫὠὬ,"Recipe not usable");menu.OnCookingUI(recipe);
   }else if(c.Kind=="trade_cook_quantity"){
    var material=(RenewalCraftMaterial)Get(ui,"_craftMaterial");Require(c.Value>0&&c.Value<=material.MaxCraftCount,"Cooking batch too large");Call(ui,"ὡὬὫὭὨὨὪὤὪὮὣ",c.Value,false);
   }else if(c.Kind=="trade_cook_confirm"){
    var model=(ὡὮὨὥὤὥὠὮὨὦὮ)Get(ui,"ὫὫὮὪὠὥὧὤὮὠὬ");var recipe=model.ὭὥὫὤὦὦὬὦὪὣὣ;var material=(RenewalCraftMaterial)Get(ui,"_craftMaterial");var button=(CraftButton)Get(ui,"_craftButton");
    Require(model.ὣὭὧὢὦὨὩὯὢὦὨ==c.Items[0]&&material.craftCount==c.Items[1]&&!button.objDisableButton.activeSelf&&!(bool)Get(ui,"ὤὬὣὢὫὫὧὭὭὫὪ"),"Cooking preview changed/busy");
    var ch=(CharDBInfo)Get(ui,"ὪὤὣὨὯὩὩὠὦὦὢ");var skill=ὯὤὮὦὮὬὨὢὩὪὦ.ὩὫὩὤὤὤὢὣὥὤὨ(ὯὤὮὦὮὬὨὢὩὪὦ.ὮὠὯὦὥὤὣὯὣὥὭ(ὢὭὤὣὠὦὥὠὢὣὩ.ὣὢὨὠὦὥὯὢὥὠὪ(ch.Id).TalentId).TalentSkillGroupId,recipe.ὭὭὭὥὣὣὦὬὮὢὥ);
    Require(checked((long)skill.CatalystValue*material.craftCount)==c.Value&&material.craftCount>0&&material.craftCount<=material.MaxCraftCount,"Cooking cost changed");
    for(int i=0;i<recipe.ὧὬὩὭὫὠὨὩὦὡὮ.Count;i++){List<ItemDBInfo> items;Require(ὣὡὧὡὦὣὣὬὨὪὫ.ὮὬὡὯὠὫὥὪὦὡὫ(ὥὯὯὠὣὪὦὢὫὠὮ.Food,recipe.ὧὬὩὭὫὠὨὩὦὡὮ[i],recipe.ὢὡὪὪὯὩὧὫὬὧὢ[i]*material.craftCount,out items)&&items.All(x=>x.KeepFlag==0),"Cooking materials missing/protected");}
    ui.OnClickUI(button.objButtonRoot);
   }else throw new InvalidOperationException("Unsupported trade command");
  }
 }
}
