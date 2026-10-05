using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace BD2Daily.Live {
 internal static class StoryNative {
  static object Field(object u,string name){return u.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(u);}
  internal static string PopupContext(UIBase u){
   var p=u as StoryPopupUI;if(p==null)return "";
   return typeof(StoryPopupUI).GetField("ὤὭὦὫὬὩὨὢὦὨὦ",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(p).ToString()=="PurposeOfSkip"?"story_skip":"story_replay";
  }
  internal static Surface[] Observe(){
   return Bridge.Find(typeof(StorySkipUI)).Cast<StorySkipUI>().Where(u=>u.gameObject.activeInHierarchy&&u.ὢὪὯὧὠὥὬὪὪὮὤ).Select(u=>{
    var button=(GameObject)Field(u,"_objSkipButton");var canvas=(Canvas)Field(u,"_canvas");
    bool ready=!(bool)Field(u,"ὦὢὠὪὮὮὣὩὡὤὮ")&&(bool)Field(u,"ὫὯὨὢὯὬὪὥὨὨὧ");
    // BalloonScriptUI ignores skip while its entrance coroutine is still busy.
    var balloon=Bridge.Find(typeof(BalloonScriptUI)).Cast<BalloonScriptUI>().FirstOrDefault(b=>b.gameObject.activeInHierarchy&&object.ReferenceEquals(Field(b,"_skipUI"),u));
    if(balloon!=null&&(bool)Field(balloon,"ὧὥὧὫὭὢὠὥὦὫὧ"))ready=false;
    return LiveProtocol.StoryToolbar(u.GetInstanceID(),Bridge.PathOf(u.transform),canvas.sortingOrder,ready,
     new[]{new Target{Id=button.GetInstanceID(),Field="_objSkipButton",Enabled=ready&&button.activeInHierarchy}});
   }).ToArray();
  }
  internal static Surface[] ObserveScripts(){
   return Bridge.Find(typeof(ScriptUI)).Cast<ScriptUI>().Where(u=>u.gameObject.activeInHierarchy).Select(u=>{
    var start=(GameObject)Field(Field(u,"_type1"),"_buttonStart");var touch=(GameObject)Field(u,"_objButtonTouch");
    bool ready=!u.ὧὡὥὢὣὫὪὮὮὦὢ&&((u.ὠὦὡὢὬὭὥὧὧὩὡ.IsActive&&u.ὠὦὡὢὬὭὥὧὧὩὡ.IsTouch)||(u.ὤὭὡὦὧὯὣὯὯὯὫ.IsActive&&u.ὤὭὡὦὧὯὣὯὯὯὫ.IsTouch));
    var targets=new[]{new Target{Id=start.GetInstanceID(),Field="_buttonStart",Enabled=start.activeInHierarchy},new Target{Id=touch.GetInstanceID(),Field="_objButtonTouch",Enabled=ready&&touch.activeInHierarchy}};
    return new Surface{Id=u.GetInstanceID(),Type="ScriptUI",NativeContext="story_dialogue",Path=Bridge.PathOf(u.transform),Popup=false,Order=850,InputReady=targets.Any(t=>t.Enabled),Targets=targets,
     Text=u.GetComponentsInChildren<TMPro.TMP_Text>().Where(t=>t!=null&&t.isActiveAndEnabled).Select(t=>t.text).Take(12).ToArray()};
   }).ToArray();
  }
  internal static void Advance(Command c){
   var s=ObserveScripts().Single(x=>x.Id==c.SurfaceId);var target=s.Targets.FirstOrDefault(t=>t.Enabled);
   if(!s.InputReady||target==null)throw new InvalidOperationException("Story dialogue not ready");
   var u=Bridge.Find(typeof(ScriptUI)).Cast<ScriptUI>().Single(x=>x.GetInstanceID()==c.SurfaceId);
   var button=target.Field=="_buttonStart"?(GameObject)Field(Field(u,"_type1"),"_buttonStart"):(GameObject)Field(u,"_objButtonTouch");
   u.OnClickUI(button);
  }
  internal static void Skip(Command c){
   var s=Observe().Single(x=>x.Id==c.SurfaceId);
   if(!s.InputReady||!s.Targets.Any(t=>t.Field=="_objSkipButton"&&t.Enabled))throw new InvalidOperationException("Story skip not ready");
   Bridge.Find(typeof(StorySkipUI)).Cast<StorySkipUI>().Single(x=>x.GetInstanceID()==c.SurfaceId).OnClickStorySkip();
  }
 }
}
