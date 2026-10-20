using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace BD2Territory.Runtime
{
 internal sealed partial class RuntimeEngine
 {
  private static readonly FieldInfo controllerField=typeof(MoveController).GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Single(f=>f.FieldType==typeof(CharacterController));
  internal static CharacterController MovementBody(PlayerMoveController movement)=>movement==null?null:controllerField.GetValue(movement) as CharacterController;
  private CharacterController Body=>MovementBody(move);
  private static readonly FieldInfo normalStepField=typeof(PlayerMoveController).GetField(TerritoryClient.NormalStepFieldName,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
  private int stepBodyId;private float normalStep;
  private float StepHeight
  {
   get{var body=Body;if(body==null)return 0;if(stepBodyId!=body.GetInstanceID()){stepBodyId=body.GetInstanceID();normalStep=0;}
    // PlayerMoveController temporarily sets stepOffset=0 after a side collision. Do not
    // fossilize that transient recovery frame as a map-wide ban on all steps.
    normalStep=Math.Max(body.stepOffset,Convert.ToSingle(normalStepField.GetValue(move)));return Math.Max(0,normalStep*Math.Abs(body.transform.lossyScale.y));}
  }
  private float RootLift
  {
   get{var body=Body;if(body==null)return .18f;float half=Math.Max(BodyRadius,body.height*Math.Abs(body.transform.lossyScale.y)*.5f);return half+Skin-(body.transform.position-player.transform.position).y-body.transform.TransformVector(body.center).y;}
  }
  private void BodyCapsule(Vector3 root,out Vector3 low,out Vector3 high,out float radius)
  {
   var body=Body;radius=BodyRadius;
   var center=body==null?root+Vector3.up*.4f:root+(body.transform.TransformPoint(body.center)-player.transform.position);
   float half=body==null?.4f:Math.Max(radius,body.height*Math.Abs(body.transform.lossyScale.y)*.5f);
   low=center-Vector3.up*(half-radius);high=center+Vector3.up*(half-radius);
  }
  private float BodyRadius
  {get{var body=Body;return body==null?.24f:Math.Max(.01f,body.radius*Math.Max(Math.Abs(body.transform.lossyScale.x),Math.Abs(body.transform.lossyScale.z)));}}
  private float SlopeLimit=>Body==null?45:Body.slopeLimit;
  private float Skin=>Body==null?0:Math.Max(0,Body.skinWidth*Math.Abs(Body.transform.lossyScale.y));
  // Use the same collision pair/layer rules for floor rays and body sweeps. Decorative/selection
  // colliders ignored by the player must neither block movement nor become fictitious floors.
  private bool MovementCollider(Collider collider)
  {
   if(collider==null||!collider.enabled||!collider.gameObject.activeInHierarchy||collider.isTrigger||collider.transform.IsChildOf(player.transform))return false;
   var own=Body;int layer=own==null?player.gameObject.layer:own.gameObject.layer;
   return !Physics.GetIgnoreLayerCollision(layer,collider.gameObject.layer)&&(own==null||!Physics.GetIgnoreCollision(own,collider));
  }
  private bool Blocks(Collider collider,Vector3 root)
  {
   if(!MovementCollider(collider))return false;
   Vector3 low,high;float radius;BodyCapsule(root,out low,out high,out radius);double margin=TraversalRules.ContactMargin(Skin);
   return collider.bounds.max.y>low.y-radius+margin&&collider.bounds.min.y<high.y+radius-margin;
  }
  private bool StandClear(Vector3 p)
  {Vector3 low,high;float radius;BodyCapsule(p,out low,out high,out radius);return !Physics.OverlapCapsule(low,high,radius,~0,QueryTriggerInteraction.Ignore).Any(c=>Blocks(c,p));}
  private bool TraversalClear(Vector3 p)
  {
   // A walking capsule contacts the next riser before its centre reaches that tread.
   // Intermediate route points may use native step clearance; interaction stands may not.
   Vector3 low,high;float radius;BodyCapsule(p,out low,out high,out radius);
   var contacts=Physics.OverlapCapsule(low,high,radius,~0,QueryTriggerInteraction.Ignore).Where(c=>Blocks(c,p)).ToArray();
   if(contacts.Length==0)return true;
   if(contacts.Any(c=>c is CharacterController||c.GetComponentInParent<LifeGatheringObject>()!=null))return false;
   int steps=Math.Max(1,(int)Math.Ceiling(StepHeight/.05f));
   for(int i=1;i<=steps;i++)if(StandClear(p+Vector3.up*(StepHeight*i/steps)))return true;
   return false;
  }
  private bool GroundPoint(Vector3 desired,out Vector3 point,bool clearance=true)
  {
   point=desired;bool bridge;if(!SurfaceAllowed(desired,out bridge))return false;float floor=desired.y-RootLift,height=Math.Max(.55f,StepHeight+.1f);
   foreach(var hit in Physics.RaycastAll(new Vector3(desired.x,floor+height,desired.z),Vector3.down,height+.75f,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
   {
    var collider=hit.collider;if(!(MovementCollider(collider)||bridge&&BridgeGround(collider))||!TraversalRules.WalkableNormal(hit.normal.y,SlopeLimit)||Math.Abs(hit.point.y-floor)>Math.Max(.45f,StepHeight+.02f))continue;
    if(collider is CharacterController||collider.GetComponentInParent<LifeGatheringObject>()!=null)continue;
    point=hit.point+Vector3.up*RootLift;return !clearance||StandClear(point);
   }
   return false;
  }
  private bool StepCandidate(RaycastHit hit,Vector3 from,Vector3 to)
  {
   var collider=hit.collider;
   if(collider is CharacterController||collider.GetComponentInParent<LifeGatheringObject>()!=null)return false;
   Vector3 low,high;float radius;BodyCapsule(from,out low,out high,out radius);float foot=low.y-radius;
   Vector3 landing;
   if(!GroundPoint(to,out landing,false))return false;
   float top=landing.y-RootLift;
   if(!TraversalRules.CanStep(top,foot,StepHeight,Skin)&&!TraversalRules.WalkableNormal(hit.normal.y,SlopeLimit))return false;
   if(TraversalRules.WalkableNormal(hit.normal.y,SlopeLimit))return true;
   // Sweep at successive legal step heights, including before the centre crosses the
   // riser. Entire-mesh bounds cannot describe a staircase's local tread height.
   float baseHeight=Math.Max(from.y,landing.y),limit=from.y+StepHeight;
   int levels=Math.Max(1,(int)Math.Ceiling(Math.Max(0,limit-baseHeight)/.05f));
   for(int i=0;i<=levels;i++)
   {
    float y=baseHeight+Math.Max(0,limit-baseHeight)*i/levels;
    var raisedFrom=new Vector3(from.x,y,from.z);var raisedTo=new Vector3(landing.x,y,landing.z);
    if(!StandClear(raisedFrom)||!StandClear(raisedTo))continue;
    Vector3 raisedLow,raisedHigh;float raisedRadius;BodyCapsule(raisedFrom,out raisedLow,out raisedHigh,out raisedRadius);
    var crossing=raisedTo-raisedFrom;
    if(!Physics.CapsuleCastAll(raisedLow,raisedHigh,raisedRadius,crossing.normalized,crossing.magnitude,~0,QueryTriggerInteraction.Ignore)
     .Any(h=>MovementCollider(h.collider)&&!TraversalRules.WalkableNormal(h.normal.y,SlopeLimit)))return true;
   }
   return false;
  }
  private Collider Obstacle(Vector3 from,Vector3 to)
  {
   var delta=to-from;delta.y=0;if(delta.sqrMagnitude<.0001f)return null;
   Vector3 low,high;float radius;BodyCapsule(from,out low,out high,out radius);
   foreach(var hit in Physics.CapsuleCastAll(low,high,radius,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
    if(Blocks(hit.collider,from)&&!StepCandidate(hit,from,to))return hit.collider;
   return null;
  }
 }
}
