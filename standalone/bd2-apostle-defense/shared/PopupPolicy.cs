using System.Runtime.Serialization;
namespace BD2ApostleDefense
{
 [DataContract] public class SurfaceInfo
 {
  [DataMember] public string Type="",RegisteredName="",ObjectName="",Parent="";
  [DataMember] public bool Active,Popup,GameHud,EmbeddedDefenseNotice,NativeFlow;
  public bool Blocks {get{return PopupPolicy.IsBlocking(this);}}
 }
 public static class PopupPolicy
 {
  public static bool IsBlocking(SurfaceInfo surface)
  {
   // DefenseHUD initializes its embedded NoticeUI itself. It is a persistent notification
   // strip, even when its UI registry name is absent and the game's name-based HUD test fails.
   // EmbeddedDefenseNotice is set only for the exact DefenseHUD._noticeUI instance.
   return surface.Active&&surface.Popup&&!surface.GameHud&&!surface.EmbeddedDefenseNotice&&!surface.NativeFlow;
  }
 }
}
