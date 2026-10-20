using System;
using System.Collections.Generic;
using System.Linq;
using Proto.Design.common;
using B=BD2Territory.Runtime.TerritoryBindings;
namespace BD2Territory.Runtime
{
 internal sealed partial class RuntimeEngine
 {
  private long lastCatalog;private RecipeOption[] recipes=new RecipeOption[0],seedOptions=new RecipeOption[0];
  private void ReadCatalog(TerritorySnapshot s,TerritoryControl c,long now)
  {
   if(!s.Ready)return;
   if(now-lastCatalog>TimeSpan.FromSeconds(3).Ticks)
   {
    lastCatalog=now;var result=new List<RecipeOption>();
    foreach(var id in (IEnumerable<int>)B.Invoke("Tables.CookIds"))
    {
     var r=(LifeCookTable)B.Invoke("Tables.Cook",id);if(r==null)continue;
     var item=(LifeItemTable)B.Invoke("Tables.ItemById",r.ResultItemId);
     var option=new RecipeOption{Id=id,Name=item==null?"料理 "+id:(string)B.Invoke("Text.Name",item.NameTextId)};
     try{if(item==null)throw new InvalidOperationException("料理成品数据缺失");if(!(bool)B.Invoke("Layout.Unlocked",item.UnlockContentType,item.UnlockContentLevel))throw new InvalidOperationException("料理尚未解锁");var stocks=ReadCrops(id);option.Available=true;option.Ratio=string.Join(" : ",stocks.Select(v=>v.Name+" "+v.Required).ToArray());}
     catch(Exception e){option.Reason=e.GetBaseException().Message;}
     result.Add(option);
    }
    recipes=result.ToArray();var seedResult=new List<RecipeOption>();
    foreach(var id in (IEnumerable<int>)B.Invoke("Tables.CropIds"))
    {
     var seed=(LifeCropSeedTable)B.Invoke("Tables.Crop",id);if(seed==null)continue;
     var item=(LifeItemTable)B.Invoke("Tables.Item",seed.ItemGroupId);
     var option=new RecipeOption{Id=id,Name=item==null?"作物 "+id:(string)B.Invoke("Text.Name",item.NameTextId)};
     try{ReadCrop(seed,1);option.Available=true;}catch(Exception e){option.Reason=e.GetBaseException().Message;}
     seedResult.Add(option);
    }
    seedOptions=seedResult.ToArray();
   }
   s.Recipes=recipes;s.Seeds=seedOptions;
   if(!c.Valid(now,pid)){s.ActiveRecipeId=c.RecipeId;s.ActiveFixedSeedId=c.FixedCrop?c.FixedSeedId:0;try{s.Crops=c.FixedCrop&&c.FixedSeedId<=0?new CropStock[0]:ReadPlantingCrops(c.RecipeId,s.ActiveFixedSeedId);}catch{s.Crops=new CropStock[0];}}
  }
 }
}
