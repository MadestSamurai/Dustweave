using System;
using UnityEngine;
namespace BD2Territory.Runtime
{
 internal sealed partial class RuntimeEngine
 {
  private readonly GatheringStandMemory gatheringStands=new GatheringStandMemory();
  private Vector3 gatheringStand;
  private bool CanGatherAt(Component target,Vector3 point)
  {
   if(!InInteractionRange(target,point))return false;
   if(!(target is LifeGatheringObject resource))return true;
   if(!gatheringStands.Allows(target.GetInstanceID(),Point(point),DateTime.UtcNow.Ticks))return false;
   if(Kind(resource)!=1)return true;
   // The detector is wider than the axe hit volume. Tree colliders can be offset from the root.
   // Approach the actual trunk, leaving capsule clearance; never enlarge the game's hit volume.
   var trunk=target.GetComponent<Collider>();if(trunk==null||!trunk.enabled)return false;
   var surface=trunk.ClosestPoint(point+Vector3.up*.4f);
   return FlatDistance(point,surface)<=BodyRadius+.15f;
  }
  private bool GatherStandReached(Component target,Vector3 point,bool destinationReached,bool detected)
  {
   // A recovery route must actually reach its new stand, even if the old detector still includes us.
   return (destinationReached||detected&&!gatheringReposition)&&CanGatherAt(target,point);
  }
 }
}
