using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
namespace BD2Daily.Live {
 internal static class NoticeNative {
  private static object Field(object obj,string name){
   for(var t=obj.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);if(f!=null)return f.GetValue(obj);}return null;
  }
  private static Component Owner(UIBase ui){
   if(ui.GetType().Name=="PackagePopupUI"){
    var offers=ui.GetComponentsInChildren<PackageUIPrefabBase>().Where(c=>c!=null&&c.gameObject.activeInHierarchy).ToArray();
    return offers.Length==1?offers[0]:null;
   }
   if(ui.GetType().Name!="NewsPopupEventUI")return ui;
   var children=ui.GetComponentsInChildren<MonoBehaviour>().Where(c=>c!=null&&c.gameObject.activeInHierarchy&&c.GetType().Name=="NewsPopupEventUIPrefab").ToArray();
   return children.Length==1?children[0]:null;
  }
  internal static string Observe(UIBase ui){
   if(!LivePolicy.SuppressibleNotice(ui.GetType().Name))return "none";
   var owner=Owner(ui);if(owner==null)return "loading";
   var button=Field(owner,"_goBtnSkip") as GameObject;
   var on=Field(owner,"_goSkipEnable") as GameObject;var off=Field(owner,"_goSkipDisable") as GameObject;
   var label=Field(owner,"_textSkip") as TMPro.TMP_Text;
   if(button==null||on==null||off==null||label==null)return owner is PackageUIPrefabBase?"hidden":"loading";
   if(!button.activeInHierarchy)return "hidden";
   if(!Regex.IsMatch(label.text??"",@"(?<![0-9])7(?![0-9])"))return "other_period";
   if(on.activeSelf==off.activeSelf)return "loading";
   return on.activeSelf?"checked":"unchecked";
  }
  internal static void Select(UIBase ui){
   string state=Observe(ui);
   if(state=="checked")return; // Idempotent: never toggle a checked box off.
   if(state!="unchecked")throw new InvalidOperationException("Seven-day notice checkbox is not ready");
   var owner=Owner(ui);var button=(GameObject)Field(owner,"_goBtnSkip");
   var click=owner.GetType().GetMethod("OnClickUI",BindingFlags.Instance|BindingFlags.Public,null,new[]{typeof(GameObject)},null);
   if(click==null)throw new InvalidOperationException("Native notice click unavailable");
   click.Invoke(owner,new object[]{button});
   if(Observe(ui)!="checked")throw new InvalidOperationException("Native notice checkbox did not become checked");
  }
 }
}
