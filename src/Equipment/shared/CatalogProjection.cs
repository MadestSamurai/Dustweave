using System;
using System.Collections.Generic;
using System.Reflection;
namespace BD2Equipment.Live {
 // Ship only strings and reward rows reachable from equipment. Story/UI text and
 // unrelated rewards can exceed the IPC frame budget by themselves.
 public sealed class CatalogProjection {
  readonly HashSet<int> names=new HashSet<int>(),texts=new HashSet<int>(),boxes=new HashSet<int>(),rewards=new HashSet<int>();
  static int Number(object row,string property){var p=row.GetType().GetProperty(property);return p==null?0:Convert.ToInt32(p.GetValue(row,null));}
  public bool Include(string table,object row){int id=Number(row,"Id");return table=="NameTextTable"?names.Contains(id):table=="LocalTextTable"?texts.Contains(id):table=="RandomBoxTable"?boxes.Contains(id):table=="RewardGroupTable"?rewards.Contains(id):true;}
  public void Remember(string table,object row){
   if(table=="EquipmentTable"||table=="ResourceTable")names.Add(Number(row,"ItemNameTextId"));
   if(table=="CharTable"){names.Add(Number(row,"CharNameTextId"));names.Add(Number(row,"NameTextId"));}
   if(table=="EquipmentMakingTable"){texts.Add(Number(row,"ItemNameLocalTextId"));if(Number(row,"ResultItemType")==9)boxes.Add(Number(row,"ResultItemId"));}
   if(table=="RandomBoxTable")rewards.Add(Number(row,"RewardGroupId"));
  }
 }
}
