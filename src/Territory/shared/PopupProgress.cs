using System;
namespace BD2Territory
{
 public sealed class PopupProgress
 {
  public bool Pending{get;private set;}private long lastSeen,lastClick;
  public void Seen(long now){Pending=true;lastSeen=now;}
  public bool CanClick(long now,bool nativeCanClose)=>nativeCanClose&&(lastClick==0||now-lastClick>=TimeSpan.FromMilliseconds(500).Ticks);
  public void Clicked(long now){lastClick=now;}
  public bool WaitForSettle(long now){if(!Pending)return false;if(now-lastSeen<TimeSpan.FromMilliseconds(800).Ticks)return true;Pending=false;return false;}
 }
}
