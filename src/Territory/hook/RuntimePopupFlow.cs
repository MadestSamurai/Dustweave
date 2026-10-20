using System;
using B=BD2Territory.Runtime.TerritoryBindings;
namespace BD2Territory.Runtime
{
 internal sealed partial class RuntimeEngine
 {
  private readonly PopupProgress popupProgress=new PopupProgress();
  private void ConfirmTerritoryPopup(UIBase popup,TerritorySnapshot s,long now)
  {
   StopMove();popupProgress.Seen(now);s.Reason="确认领地升级／解锁";
   // CloseUI alone skips AvatarLifeLevelUpPopupUI.OnClickUI: unlocks and HUD restoration live there.
   // The native back-button route retains animation/click guards and the subclass continuation.
   if(popupProgress.CanClick(now,popup.CanCloseUI())&&now-lastInput>=TimeSpan.FromMilliseconds(500).Ticks)
   {B.InvokeOn("Ui.Back",popup);popupProgress.Clicked(now);lastInput=now;}
  }
 }
}
