using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Proto.Design.common;
using Proto.Net;
using Proto.Local;
using gamfs;
namespace BD2Daily.Live {
 // Pre-dispatch only. No global patch or persistent setting affects manual play/tools.
 internal static class TalentPotionNative {
  const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
  static object Get(object o,string n){var t=o.GetType();while(t!=null){var f=t.GetField(n,Flags);if(f!=null)return f.GetValue(o);var p=t.GetProperty(n,Flags);if(p!=null)return p.GetValue(o,null);t=t.BaseType;}throw new MissingFieldException(n);}
  static TalentSkillTable Skill(CharDBInfo ch,int level){return ὯὤὮὦὮὬὨὢὩὪὦ.ὩὫὩὤὤὤὢὣὥὤὨ(ὯὤὮὦὮὬὨὢὩὪὦ.ὮὠὯὦὥὤὣὯὣὥὭ(ὢὭὤὣὠὦὥὠὢὣὩ.ὣὢὨὠὦὥὯὢὥὠὪ(ch.Id).TalentId).TalentSkillGroupId,level);}
  static long DispatchCost(TalentSkillTable skill,IEnumerable<int> ids){
   long now=ὫὨὪὠὢὨὮὤὩὤὮ.ὠὥὬὪὮὬὢὨὬὮὫ.UnixTimeStamp();
   var working=ὣὡὧὡὦὣὣὬὨὪὫ.ὨὬὯὢὪὥὮὬὠὢὠ.Where(x=>x.EndTime>now).Select(x=>x.Id).ToArray();
   return checked((long)skill.CatalystValue*ids.Where(x=>x>0&&!working.Contains(x)).Distinct().Count());
  }
  static long GlobalDispatchCost(){
   if(!ὬὦὬὪὢὥὣὤὧὬὮ.ὥὪὧὣὬὢὫὩὩὪὮ)return 0;
   long cost=0;
   foreach(var ch in ὣὡὧὡὦὣὣὬὨὪὫ.ὨὯὯὪὢὯὡὪὡὫὯ){
    var talent=ὯὤὮὦὮὬὨὢὩὪὦ.ὮὠὯὦὥὤὣὯὣὥὭ(ὢὭὤὣὠὦὥὠὢὣὩ.ὣὢὨὠὦὥὯὢὥὠὪ(ch.Id).TalentId);
    if(talent==null)continue;
    var skill=Skill(ch,ch.TalentLevel);
    if(skill!=null&&skill.ClassType==18){
     cost=checked(cost+DispatchCost(skill,ὫὧὧὦὬὦὠὩὯὦὦ.ὥὪὪὬὮὢὠὮὢὨὧ(skill.GroupId,skill.Id)));
    }
   }
   return cost;
  }
  internal static string Gate(Command c,Frame frame,UIBase ui){
   if(!c.GuardTalentPotions)return "";
   try{
    long cost=0;
    var surface=frame.Surfaces.SingleOrDefault(x=>x.Id==c.SurfaceId);
    var target=surface==null?null:surface.Targets.FirstOrDefault(x=>x.Id==c.TargetId);
    string field=target==null?"":target.Field;
    bool press=c.Kind=="click"||c.Kind=="pointer"||c.Kind=="native_click";
    if(c.Kind=="mainline_talent"||c.Kind=="weekly_steal_talent"){
     var row=Bridge.Find(typeof(QuickMenuTalentSkillLoopScrollItem)).Cast<QuickMenuTalentSkillLoopScrollItem>().Single(x=>x.GetInstanceID()==c.Value);
     cost=((TalentSkillTable)Get(row,"ὣὡὪὭὤὨὭὣὪὧὩ")).CatalystValue;
    }else if(c.Kind=="weekly_steal_confirm"||(press&&ui is DiscountPopupUI&&field=="_objButtonOk")){
     cost=((TalentSkillTable)Get(ui,"ὣὡὪὭὤὨὭὣὪὧὩ")).CatalystValue;
    }else if(c.Kind=="trade_cook_confirm"){
     var model=(ὡὮὨὥὤὥὠὮὨὦὮ)Get(ui,"ὫὫὮὪὠὥὧὤὮὠὬ");
     cost=checked((long)Skill((CharDBInfo)Get(ui,"ὪὤὣὨὯὩὩὠὦὦὢ"),model.ὭὥὫὤὦὦὬὦὪὣὣ.ὭὭὭὥὣὣὦὬὮὢὥ).CatalystValue*((RenewalCraftMaterial)Get(ui,"_craftMaterial")).craftCount);
    }else if(press&&ui is EquipmentMakingUI&&field=="$pointer/UIRoot/Mask/EquipInfoParent/Button - Make"){
     var recipe=Get(Get(ui,"ὤὭὠὥὤὠὡὣὣὠὣ"),"ὫὮὩὬὤὫὮὨὢὯὬ");
     cost=checked((long)Skill((CharDBInfo)Get(ui,"ὪὤὣὨὯὩὩὠὦὦὢ"),Convert.ToInt32(Get(recipe,"TalentLevel"))).CatalystValue*((RenewalCraftMaterial)Get(ui,"_craftMaterial")).craftCount);
    }else if(c.Kind=="dispatch_collect_all")cost=GlobalDispatchCost();
    else if(press&&ui is DispatchSelectUI&&field=="_autoDispatchExcuteBtn")cost=DispatchCost((TalentSkillTable)Get((DispatchSelectUI)ui,"ὦὢὧὧὥὥὫὡὧὣὪ"),(IEnumerable<int>)Get((DispatchSelectUI)ui,"ὬὩὠὡὬὯὨὪὡὩὮ"));
    if(cost<0||c.TalentPotionReserve<0)return "potion_policy_invalid";
    if(cost==0)return "";
    long balance=ὣὡὧὡὦὣὣὬὨὪὫ.ὭὨὣὥὧὨὮὪὯὯὠ(ὥὯὯὠὣὪὦὢὫὠὮ.Catalyst);
    return balance<c.TalentPotionReserve||cost>balance-c.TalentPotionReserve?"potion_limit:"+balance+":"+cost+":"+c.TalentPotionReserve:"";
   }catch(Exception ex){return "potion_cost_unavailable:"+ex.GetType().Name;}
  }
 }
}
