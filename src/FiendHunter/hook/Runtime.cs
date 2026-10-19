using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using HarmonyLib;
using Proto.Net;
using Proto.Design.common;
using BD2FiendHunter.Shared;
using gamfs.ActionGame;

namespace BD2FiendHunterRuntimePublic1 {
public static class Loader {
 static object engine;static GameObject host;static bool resolver;static BD2.LocalIpc.Handoff flow;static BD2.LocalIpc.MainThread frame;
 public static string PreviousReply="",PreviousRound="";
 static string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2FiendHunter");
 const string Names="runtime.json|state.json|error.json|control.json|command.json|stop|receipt-*";
 public static void Load(){if(flow==null)flow=new BD2.LocalIpc.Handoff(typeof(Loader).Assembly.FullName,"fiend-hunter","fiend-hunter",false,Start,Pause,Busy,Stop,Status);if(!flow.IsActive&&!flow.Pending)BD2.LocalIpc.RuntimeFiles.Start(Root,typeof(Loader).Module.ModuleVersionId.ToString("N"),Names);flow.Request(DateTime.UtcNow);if(frame==null)frame=new BD2.LocalIpc.MainThread(()=>{BD2.LocalIpc.LegacyPilots.Discover();flow.Tick(DateTime.UtcNow);return flow.Pending;},flow.Fail);frame.Schedule();}
 public static void Unload(){BD2.LocalIpc.MainThread.Drain(()=>flow.Unload(),Status);}
 static void Status(string state,string error){if(state=="active")BD2.LocalIpc.RuntimeFiles.Activate();BD2.LocalIpc.RuntimeFiles.Write(Path.Combine(Root,"runtime.json"),System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(new{State=state,Error=error,AtUtc=DateTime.UtcNow.ToString("O")})));}
 static void Start(){BD2.LocalIpc.RuntimeFiles.Start(Root,typeof(Loader).Module.ModuleVersionId.ToString("N"),Names);if(!resolver){AppDomain.CurrentDomain.AssemblyResolve+=Resolve;resolver=true;}host=new GameObject("BD2 FiendHunter Assistant");UnityEngine.Object.DontDestroyOnLoad(host);engine=host.AddComponent(typeof(Loader).Assembly.GetType("BD2FiendHunterRuntimePublic1.Engine",true));}
 static object Call(string method){return engine.GetType().GetMethod(method).Invoke(engine,null);}
 static void Pause(){BD2.LocalIpc.RuntimeFiles.Revoke();if(engine!=null)Call("PrepareHandoff");}
 static string Busy(){return engine==null?"":(string)Call("HandoffBusy");}
 static void Stop(){if(host!=null)UnityEngine.Object.DestroyImmediate(host);engine=null;host=null;if(resolver){AppDomain.CurrentDomain.AssemblyResolve-=Resolve;resolver=false;}}
 static Assembly Resolve(object s,ResolveEventArgs e){if(new AssemblyName(e.Name).Name!="0Harmony")return null;using(var a=typeof(Loader).Assembly.GetManifestResourceStream("FiendHunter.Harmony.dll"))using(var b=new MemoryStream()){a.CopyTo(b);return Assembly.Load(b.ToArray());}}
}
[DefaultExecutionOrder(19900)]
public sealed class Engine:MonoBehaviour {
 public static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2FiendHunter");
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 readonly string session=Guid.NewGuid().ToString("N");readonly Publisher publisher=new Publisher();
 Control control=new Control();bool handingOff,latchedStop,ownsMove;float nextRead,nextWrite,nextObserve,nextAction,nextDodge,nextHeal,waitingSince;string reason="只读观察",lastError="";
 ActionCharPlayer player;ActionCharMonster boss;UIBase[] uis=new UIBase[0];ActionColliderData[] bossBoxes=new ActionColliderData[0],playerBoxes=new ActionColliderData[0];object engagement;int attackRequests;Dictionary<string,float> bossDashes=new Dictionary<string,float>();string lastProtection="";
 readonly Dictionary<string,List<float>> observedImpacts=new Dictionary<string,List<float>>();double maxBossMultiplier,observedDamage;object healSafety;int hazardSkill;string[] activeHazards=new string[0];bool holdingAttack,chargeEntered;float heldSince,lastHold,nextCharge;int specials,charges;int attacks,dodges,heals,guards;float nextDefense,matchingUntil;int defenseSkill;float defenseImpact;float previousBossTime;long oldHp=-1,oldBossHp=-1;float started;int observedBoss;string oldBossState="";static Engine current;
 readonly Harmony harmony=new Harmony("bd2.fiend-hunter.public1");static readonly Dictionary<MethodBase,string> replies=new Dictionary<MethodBase,string>();int endReplies,networkErrors,networkLost;double lastPacketTicks;bool resultSent;float resultSince;string lastReply="",outcome="";
 ActionGameManager Manager=>ὦὣὫὣὮὫὪὧὢὩὮ.ὠὪὯὢὪὨὯὢὡὤὫ;
 bool Live=>!handingOff&&!latchedStop&&control.Enabled&&control.Valid(session,DateTime.UtcNow.Ticks);
 public void PrepareHandoff(){handingOff=true;control.Enabled=false;Release();}
 public string HandoffBusy(){return "";}
 void Awake(){current=this;NetworkGuard.Install((kind,data)=>Log(kind,data));
  var net=typeof(ὢὭὣὪὩὡὪὯὦὫὦ);
  foreach(var t in net.GetNestedTypes(All).Concat(new[]{net}))foreach(var m in t.GetMethods(All|BindingFlags.DeclaredOnly)){
   var a=m.GetParameters();if(a.Length!=3||a[0].ParameterType!=typeof(byte[])||a[1].ParameterType!=typeof(int)||a[2].ParameterType!=typeof(int)||m.GetMethodBody()==null)continue;
   var kinds=PatchProcessor.GetOriginalInstructions(m).Select(i=>i.operand as MethodInfo).Where(x=>x!=null&&x.Name=="get_Parser").Select(x=>x.DeclaringType.Name).Where(x=>x=="MiniGameActionEndResponse"||x=="MiniGameActionStartResponse").Distinct().ToArray();
   if(kinds.Length==1){replies[m]=kinds[0];harmony.Patch(m,prefix:new HarmonyMethod(typeof(Engine),nameof(Reply)));}
  }
  harmony.Patch(AccessTools.Method(typeof(ActionNetworkManager),"TCPReceivedPacketCallback"),prefix:new HarmonyMethod(typeof(Engine),nameof(TcpReply)));
  harmony.Patch(AccessTools.Method(typeof(BDNetwork.NetworkTCPManager),"OnNetworkLost"),prefix:new HarmonyMethod(typeof(Engine),nameof(NetworkLost)));
  Log("connected",new{Observers=replies.Values.ToArray()});
 }
 public static void TcpReply(Network.TCP.ENetMsg __0,Google.Protobuf.IMessage __1){var e=current;if(e==null)return;e.lastPacketTicks=DateTime.UtcNow.Ticks;string kind=__0.ToString();if(kind.Contains("GameEnd")||kind.Contains("Reconnect")||kind.Contains("GameExit")||kind.Contains("GameStart")){var details=JObject.Parse(__1.ToString());e.Log("relay-reply",new{Kind=kind,StartTime=details["startTime"],EndTime=details["endTime"],State=details["state"]});}}
 public static void NetworkLost(){var e=current;if(e==null)return;e.networkLost++;e.Log("network-lost",new{LastPacketAge=(DateTime.UtcNow.Ticks-e.lastPacketTicks)/TimeSpan.TicksPerSecond,Status=NetworkConnectivityMonitor.ὥὪὡὦὦὫὢὠὬὮὡ.ὬὨὠὣὡὮὧὩὯὭὭ.ToString(),Stack=Environment.StackTrace});}
 public static void Reply(MethodBase __originalMethod,byte[] __0,int __1,int __2){var e=current;if(e==null)return;string kind=replies[__originalMethod];e.lastReply=kind+":"+__2;e.Log("network-reply",new{Kind=kind,Code=__2,Bytes=__0==null?0:__0.Length});if(__2!=0){e.networkErrors++;e.latchedStop=true;e.Release();}else if(kind=="MiniGameActionEndResponse"){e.endReplies++;try{e.Log("settlement",new{Data=typeof(MiniGameActionEndResponse).GetProperty("Parser").GetValue(null,null).GetType().GetMethod("ParseFrom",new[]{typeof(byte[])}).Invoke(MiniGameActionEndResponse.Parser,new object[]{__0}).ToString()});}catch(Exception ex){e.Log("settlement-parse-error",new{Error=ex.Message});}}}
 void Write(string name,object value){publisher.Write(Path.Combine(Root,name),JsonConvert.SerializeObject(value));}
 internal void Log(string kind,object data){try{Directory.CreateDirectory(ToolStorage.Diagnostics);var path=Path.Combine(ToolStorage.Diagnostics,"events.jsonl");if(File.Exists(path)&&new FileInfo(path).Length>8*1024*1024){var prior=Path.Combine(ToolStorage.Diagnostics,"events-previous.jsonl");if(File.Exists(prior))File.Delete(prior);File.Move(path,prior);}File.AppendAllText(path,JsonConvert.SerializeObject(new{AtUtc=DateTime.UtcNow.ToString("O"),Session=session,Kind=kind,Data=data})+"\n");}catch(IOException){}catch(UnauthorizedAccessException){}}
 void Update(){try{
  float now=Time.realtimeSinceStartup;
  if(now>=nextRead){nextRead=now+.1f;var b=BD2.LocalIpc.RuntimeFiles.Read(Path.Combine(Root,"control.json"));if(b!=null){var c=JsonConvert.DeserializeObject<Control>(System.Text.Encoding.UTF8.GetString(b));c.Validate();control=c;}Command();}
  if(now>=nextObserve){nextObserve=now+.5f;Observe();}
  NetworkGuard.Pump(!handingOff&&((now<matchingUntil&&endReplies==0)||(Manager!=null&&Manager.ὣὢὠὤὤὣὦὧὪὨὬ.ToString()=="EcsPlaying"&&boss!=null&&Hp(boss)>0)));
  if(Live)Play();else {Release();waitingSince=0;if(!latchedStop)reason=control.Enabled?"控制端心跳过期，已暂停":"已暂停";}
  if(now>=nextWrite){nextWrite=now+.5f;Snapshot();publisher.Flush();}
 }catch(Exception e){latchedStop=true;Release();reason=e.GetBaseException().Message;if(lastError!=e.ToString()){lastError=e.ToString();Log("error",new{Error=lastError});}try{Snapshot();}catch{}}}
 void Observe(){uis=UnityEngine.Object.FindObjectsOfType<UIBase>().Where(x=>x.gameObject.activeInHierarchy).ToArray();var m=Manager;
  var p=m==null||m.ὮὧὮὡὦὫὪὣὡὥὭ==null?null:m.ὮὧὮὡὦὫὪὣὡὥὭ.OfType<ActionCharPlayer>().FirstOrDefault(x=>x.ὡὦὨὡὫὮὥὮὨὦὡ);if(p!=player){Release();player=p;playerBoxes=player==null?new ActionColliderData[0]:player.GetComponentsInChildren<ActionColliderData>(true);}
  boss=m==null||m.ὮὧὮὡὦὫὪὣὡὥὭ==null?null:m.ὮὧὮὡὦὫὪὣὡὥὭ.OfType<ActionCharMonster>().FirstOrDefault();
  if(boss!=null&&boss.GetInstanceID()!=observedBoss){observedBoss=boss.GetInstanceID();endReplies=0;attacks=dodges=heals=0;lastReply="";resultSent=false;resultSince=0;outcome="";started=Time.realtimeSinceStartup;bossBoxes=boss.GetComponentsInChildren<ActionColliderData>(true);oldHp=-1;oldBossHp=-1;Log("round-observed",new{Player=Unit(player),Boss=Unit(boss)});nextDefense=0;defenseSkill=0;previousBossTime=0;guards=0;specials=charges=attackRequests=0;engagement=null;observedDamage=0;healSafety=null;maxBossMultiplier=BossDamageMultiplier(boss);bossDashes=DashProfile(boss);lastProtection="";Log("motion-profile",new{Player=DashProfile(player),Boss=bossDashes});}
 }
 static long Hp(ActionCharBase c)=>c==null||c.ὭὢὬὫὫὢὡὩὮὬὢ==null?0:c.ὭὢὬὫὫὢὡὩὮὬὢ.ὧὣὮὮὠὦὡὫὨὪὯ.ὤὡὩὦὣὠὪὫὬὯὤ.ὮὭὡὤὢὫὡὥὩὩὭ;
 static object Vec(Vector3 v)=>new{x=v.x,y=v.y,z=v.z};
 static double BossDamageMultiplier(ActionCharMonster unit){
  return ὯὣὩὤὢὫὫὬὠὤὬ.ὭὤὢὥὭὭὤὧὬὬὮ(unit.ὮὤὪὤὧὤὢὦὧὡὥ).Where(x=>x.AttackId>0).Select(x=>ὯὣὩὤὢὫὫὬὠὤὬ.ὯὢὬὬὢὩὭὤὬὬὯ(x.AttackId)).Where(x=>x!=null).Select(x=>x.SkillValue).DefaultIfEmpty(0).Max();
 }
 double IncomingDamage(){
  var s=boss.ὭὢὬὫὫὢὡὩὮὬὢ.ὧὣὮὮὠὦὡὫὨὪὯ;
  double damage=s.ὨὮὯὡὨὪὠὮὧὦὯ.ὮὭὡὤὢὫὡὥὩὩὭ*maxBossMultiplier;
  if(s.ὥὥὩὨὠὡὭὯὬὣὡ.ὮὭὡὤὢὫὡὥὩὩὭ>0)damage*=1+Math.Max(0,s.ὤὯὮὠὪὦὠὤὤὬὧ.ὮὭὡὤὢὫὡὥὩὩὭ);
  // Ignore temporary damage reduction when reserving health for the next attack.
  var stats=player.ὭὢὬὫὫὢὡὩὮὬὢ;
  foreach(var type in new[]{ὦὠὦὣὯὡὮὥὨὪὧ.ὠὧὫὫὠὦὯὯὦὬὥ.Wound,ὦὠὦὣὯὡὮὥὨὪὧ.ὠὧὫὫὠὦὯὯὦὬὥ.IncreaseDamage}){
   var buff=stats.ὭὫὪὡὪὮὥὮὧὪὠ<ὦὠὦὣὯὡὮὥὨὪὧ>(type);if(buff!=null)damage*=1+Math.Max(0,buff.ὬὯὯὥὪὮὣὡὭὢὧ());
  }
  return Math.Max(Math.Ceiling(damage),observedDamage);
 }
 static Dictionary<string,float> DashProfile(ActionCharBase unit){var controller=unit==null?null:unit.GetComponent<CombatController>();var assets=controller==null?null:AccessTools.Field(typeof(CombatController),"_combatActionAssets").GetValue(controller) as IEnumerable<CombatActionBase>;return assets==null?new Dictionary<string,float>():assets.OfType<CombatActionDash>().GroupBy(x=>(string)AccessTools.Field(typeof(CombatActionBase),"_animationName").GetValue(x)).ToDictionary(x=>x.Key,x=>x.Max(y=>Math.Abs(y.Distance)));}
 static object Unit(ActionCharBase c){if(c==null||c.ὭὢὬὫὫὢὡὩὮὬὢ==null)return null;var s=c.ὭὢὬὫὫὢὡὩὮὬὢ.ὧὣὮὮὠὦὡὫὨὪὯ;var a=c.ὨὠὭὧὦὧὩὭὯὥὧ.GetAnimatorStateInfo();return new{Id=c.ὮὤὪὤὧὤὢὦὧὡὥ,Hp=Hp(c),MaxHp=s.ὤὡὩὦὣὠὪὫὬὯὤ.ὮὨὭὯὡὬὫὨὢὭὣ,Stamina=s.ὪὥὥὮὢὤὯὩὡὪὩ.ὮὭὡὤὢὫὡὥὩὩὭ,Heals=s.ὡὣὭὨὭὡὧὠὣὨὧ.ὮὭὡὤὢὫὡὥὩὩὭ,Position=Vec(c.transform.position),Forward=Vec(c.ὥὮὨὩὥὬὣὭὤὠὪ.ὢὮὠὮὭὨὣὢὠὤὧ.forward),Skill=c.ὦὣὧὫὮὯὥὩὠὨὫ?.ὠὭὠὣὯὪὡὡὭὢὣ,SkillName=c.ὦὣὧὫὮὯὥὩὠὨὫ?.ὭὬὫὡὬὢὫὫὧὣὠ,Invincible=c.ὭὢὬὫὫὢὡὩὮὬὢ.ὯὥὡὩὠὩὥὯὦὡὩ(ὦὠὦὣὯὡὮὥὨὪὧ.ὠὧὫὫὠὦὯὯὦὬὥ.Invincible),Animation=a.shortNameHash,Time=a.normalizedTime,Length=a.length,Speed=a.speed};}

 void Move(Vector3 d){if(player==null)return;d.y=0;d.Normalize();var target=Manager.GetCameraLookAt();if(target!=null&&d!=Vector3.zero){var forward=target.position-player.transform.position;forward.y=0;if(forward.sqrMagnitude>.0001f)d=Quaternion.Inverse(Quaternion.LookRotation(forward,Vector3.up))*d;}player.OnMove(new Vector3(d.x,d.z,0));ownsMove=d!=Vector3.zero;}
 void Tap(int key){player.OnKeyDown((ὧὧὥὫὯὦὭὩὪὪὦ)key);player.OnKeyUp((ὧὧὥὫὯὦὭὩὪὪὦ)key);}
 void Play(){
  if(endReplies>0){reason="本局已完成服务器结算";control.Enabled=false;Release();return;}
  var errorUi=uis.FirstOrDefault(x=>x.GetType().Name=="ErrorMessagePopupUI"||x.GetType().Name=="MessagePopupUI");
  if(errorUi!=null){var messages=errorUi.GetComponentsInChildren<TMPro.TMP_Text>().Select(x=>x.text).ToArray();if(messages.Any(x=>x.Contains("網路")||x.Contains("网络")||x.IndexOf("network",StringComparison.OrdinalIgnoreCase)>=0)){reason="游戏网络中断，请恢复连接后继续";Log("network-ui",messages);latchedStop=true;Release();return;}}
  var message=uis.OfType<ActionMessagePopupUI>().FirstOrDefault();if(message!=null){reason="等待游戏提示确认："+string.Join(" ",message.GetComponentsInChildren<TMPro.TMP_Text>().Select(x=>x.text).ToArray());latchedStop=true;Release();return;}
  var m=Manager;if(m==null||player==null||boss==null){reason="等待恶魔猎人单人对局";Release();if(waitingSince==0)waitingSince=Time.realtimeSinceStartup;else if(Time.realtimeSinceStartup-waitingSince>45){latchedStop=true;reason="45秒内未进入对局，已暂停；请检查游戏加载或网络提示";}return;}if(m.ὦὮὦὢὡὪὤὭὪὯὡ){reason="首版只操作单人对局";Release();return;}
  if(m.ὫὣὡὣὤὩὪὢὥὢὦ||m.ὪὥὣὯὧὦὤὡὠὫὠ){Release();reason=m.ὫὣὡὣὤὩὪὢὥὢὦ?"胜利，等待结算":"本局结束，等待结算";var popup=UnityEngine.Object.FindObjectsOfType<ActionEndPopup>().FirstOrDefault(x=>x.gameObject.activeInHierarchy);if(popup!=null&&!resultSent){if(resultSince==0)resultSince=Time.realtimeSinceStartup;if(Time.realtimeSinceStartup-resultSince>=2){var button=AccessTools.Field(typeof(ActionEndPopup),"_objButtonExit").GetValue(popup) as GameObject;if(button!=null&&button.activeInHierarchy){resultSent=true;outcome=m.ὫὣὡὣὤὩὪὢὥὢὦ?"victory":"defeat";Log("result-exit",new{Outcome=outcome,Player=Unit(player),Boss=Unit(boss),NetworkLost=networkLost});popup.OnClickUI(button);}}}return;}
  if(m.ὪὥὣὯὧὦὤὡὠὫὠ||Hp(player)<=0||Hp(boss)<=0){reason="等待原生胜败结算";Release();return;}
  if(m.ὣὢὠὤὤὣὦὧὪὨὬ.ToString()!="EcsPlaying"){reason="等待战斗开始："+m.ὣὢὠὤὤὣὦὧὪὨὬ;Release();return;}
  if(NetworkGuard.Hold){Release();reason="正在自动战斗";return;}
  if(!control.Matches(player.ὮὤὪὤὧὤὢὦὧὡὥ,boss.ὮὤὪὤὧὤὢὦὧὡὥ)){latchedStop=true;reason="unsupported";Release();return;}
  waitingSince=0;float now=Time.realtimeSinceStartup;var bs=boss.ὥὯὬὢὥὤὧὡὣὡὣ.ὮὭὡὤὢὫὡὥὩὩὭ.ToString();var ps=player.ὥὯὬὢὥὤὧὡὣὡὣ.ὮὭὡὤὢὫὡὥὩὩὭ.ToString();
  string protection=string.Join("|",playerBoxes.Where(x=>x!=null&&x.Collider!=null&&x.Collider.enabled&&x.gameObject.activeInHierarchy&&(x.Type==ActionColliderData.ὯὨὩὬὢὡὮὯὭὨὧ.JustDash||x.Type==ActionColliderData.ὯὨὩὬὢὡὮὯὭὨὧ.JustGuard)).Select(x=>x.Type.ToString()));if(protection!=lastProtection){lastProtection=protection;Log("protection-window",new{Protection=protection,Player=Unit(player)});}
  if(oldHp>Hp(player))observedDamage=Math.Max(observedDamage,oldHp-Hp(player));
  if(bs!=oldBossState||Hp(player)!=oldHp||Hp(boss)!=oldBossHp){if(bs!=oldBossState||Hp(player)<oldHp)Log("combat",new{State=bs,Player=Unit(player),Boss=Unit(boss),Reason=reason});oldBossState=bs;oldHp=Hp(player);oldBossHp=Hp(boss);}
  var stat=player.ὭὢὬὫὫὢὡὩὮὬὢ.ὧὣὮὮὠὦὡὫὨὪὯ;double stamina=stat.ὪὥὥὮὢὤὯὩὡὪὩ.ὮὭὡὤὢὫὡὥὩὩὭ;var delta=boss.transform.position-player.transform.position;delta.y=0;float distance=delta.magnitude;var dir=delta.normalized;float melee=distance;
  // Approach is planar. Including the hurtbox's height creates an unreachable
  // minimum distance even when the player's body is already against the boss.
  // Native weapon hitboxes still decide whether an issued attack actually hits.
  float legacyDistance=distance;string nearest="root";
  foreach(var box in bossBoxes){if(box==null||box.Type!=ActionColliderData.ὯὨὩὬὢὡὮὯὭὨὧ.Hurt||box.Collider==null||!box.Collider.enabled||!box.gameObject.activeInHierarchy)continue;var edge=box.Collider.ClosestPoint(player.transform.position+Vector3.up*.8f)-player.transform.position;legacyDistance=Math.Min(legacyDistance,edge.magnitude);edge.y=0;if(edge.magnitude<melee){melee=edge.magnitude;nearest=box.name;}}
  float reach=control.AttackRange;
  engagement=new{Distance=melee,LegacyDistance=legacyDistance,Reach=reach,Nearest=nearest,RootDistance=distance,Facing=Vector3.Dot(player.ὥὮὨὩὥὬὣὭὤὠὪ.ὢὮὠὮὭὨὣὢὠὤὧ.forward,dir),InputReady=player.IsInputEnable()};
  var info=boss.ὨὠὭὧὦὧὩὭὯὥὧ.GetAnimatorStateInfo();var skill=boss.ὦὣὧὫὮὯὥὩὠὨὫ;
  string animation=skill?.ὭὬὫὡὬὢὫὫὧὣὠ??"";int skillId=skill?.ὠὭὠὣὯὪὡὡὭὢὣ??0;
  string timingKey=control.BossGroup+":"+info.shortNameHash+":"+animation;
  var activeHitboxes=bossBoxes.Where(x=>x!=null&&x.Collider!=null&&x.Collider.enabled&&x.gameObject.activeInHierarchy&&x.Type==ActionColliderData.ὯὨὩὬὢὡὮὯὭὨὧ.Hit).Select(x=>x.name).ToArray();
  if(skillId!=hazardSkill){hazardSkill=skillId;activeHazards=new string[0];}
  if(bs=="Attack"&&skillId>0&&info.normalizedTime>0&&info.normalizedTime<1&&activeHitboxes.Except(activeHazards).Any()){
   List<float> points;if(!observedImpacts.TryGetValue(timingKey,out points))observedImpacts[timingKey]=points=new List<float>();
   float point=Math.Max(0,info.normalizedTime-Time.deltaTime/Math.Max(.1f,info.length));
   if(!points.Any(x=>Math.Abs(x-point)<.04f)){points.Add(point);points.Sort();Log("observed-impact",new{Skill=skillId,Animation=animation,Phase=point});}
  }
  activeHazards=activeHitboxes;
  bool unblockable=skill is ὪὦὢὨὣὠὨὨὩὮὩ hit&&hit.ὤὪὥὮὦὬὢὫὥὥὭ;
  float impact=AttackTiming.NextImpact(control.BossGroup,animation,info.normalizedTime);List<float> knownImpacts;if(observedImpacts.TryGetValue(timingKey,out knownImpacts)){var upcoming=knownImpacts.Where(x=>x>=info.normalizedTime-.1f/Math.Max(.1f,info.length)).ToArray();if(upcoming.Length>0)impact=upcoming[0];}float eta=(impact-info.normalizedTime)*info.length;
  float dashReach;bossDashes.TryGetValue(animation,out dashReach);bool canReach=distance<12+dashReach;
  bool threat=bs=="Attack"&&impact>0&&canReach&&eta<.16f&&eta>-.10f;
  bool invincible=player.ὭὢὬὫὫὢὡὩὮὬὢ.ὯὥὡὩὠὩὥὯὦὡὩ(ὦὠὦὣὯὡὮὥὨὪὧ.ὠὧὫὫὠὦὯὯὦὬὥ.Invincible);
  if(info.normalizedTime<previousBossTime-.3f)defenseSkill=0;previousBossTime=info.normalizedTime;
  if(control.Defend&&threat&&!invincible&&now>=nextDefense&&(defenseSkill!=skillId||Math.Abs(defenseImpact-impact)>.01f)&&stamina>=(unblockable?20:25)){
   Move(unblockable?Vector3.Cross(Vector3.up,dir):Vector3.zero);int key=unblockable?3:2;var before=player.ὦὣὧὫὮὯὥὩὠὨὫ;Tap(key);var after=player.ὦὣὧὫὮὯὥὩὠὨὫ;
   if(!ReferenceEquals(before,after)){EndHold();defenseSkill=skillId;defenseImpact=impact;nextDefense=now+.2f;nextAction=now+.25f;if(unblockable)dodges++;else guards++;Log("defense",new{BossSkill=skillId,Animation=animation,Eta=eta,Key=key,Player=Unit(player),Boss=Unit(boss)});}
   reason=unblockable?"迎击时机闪避":"格挡反击";return;
  }
  double incomingDamage=IncomingDamage();long maxHp=stat.ὤὡὩὦὣὠὪὫὬὯὤ.ὮὨὭὯὡὬὫὨὢὭὣ;int remainingHeals=stat.ὡὣὭὨὭὡὧὠὣὨὧ.ὮὭὡὤὢὫὡὥὩὩὭ;
  bool needsHeal=CombatPolicy.NeedsHeal(Hp(player),maxHp,incomingDamage,remainingHeals,control);
  healSafety=new{IncomingDamage=incomingDamage,ObservedDamage=observedDamage,Threshold=CombatPolicy.HealThreshold(maxHp,incomingDamage,control),NeedsHeal=needsHeal};
  if(holdingAttack&&needsHeal&&Hp(player)<=incomingDamage){EndHold();nextCharge=now+2;}
  if(holdingAttack){
   var charge=player.ὦὣὧὫὮὯὥὩὠὨὫ as ὨὮὣὯὭὤὥὢὨὭὧ;
   if(charge!=null){chargeEntered=true;if(charge.ὤὪὧὨὤὪὮὦὬὩὤ()>=3||(bs=="Attack"&&eta>0&&eta<.55f)||melee>control.AttackRange+1||now-heldSince>4.5f){EndHold();charges++;nextCharge=now+1;nextAction=now+.15f;reason="释放蓄力攻击";}else reason="蓄力攻击";return;}
   if(chargeEntered||now-heldSince>1.4f){EndHold();nextCharge=now+2;}
   else {if(now-heldSince>=.3f&&now>=lastHold){player.OnKeyHold((ὧὧὥὫὯὦὭὩὪὪὦ)0);lastHold=now+.15f;}reason="准备蓄力";return;}
  }
  if(now<nextAction)return;
  if(bs=="Attack"&&impact>0&&eta>.12f&&eta<.5f&&canReach){Move(dir);reason="准备迎击";return;}
  bool attackBuff=player.ὭὢὬὫὫὢὡὩὮὬὢ.ὯὥὡὩὠὩὥὯὦὡὩ(ὦὠὦὣὯὡὮὥὨὪὧ.ὠὧὫὫὠὦὯὯὦὬὥ.IncreaseAtk);
  bool chargeBuff=player.ὭὢὬὫὫὢὡὩὮὬὢ.ὯὥὡὩὠὩὥὯὦὡὩ(ὦὠὦὣὯὡὮὥὨὪὧ.ὠὧὫὫὠὦὯὯὦὬὥ.DecreaseChargeTime);
  double safeSeconds=bs=="Groggy"?Math.Max(0,(1-info.normalizedTime)*info.length):bs=="Attack"&&impact>0&&eta>0?eta:0;
  bool inReach=(ps=="Idle"||ps=="Move")&&!threat&&melee<reach;
  bool gauge=stat.ὭὭὬὤὯὧὫὩὤὦὪ.ὮὨὭὯὡὬὫὨὢὭὣ>0&&stat.ὭὭὬὤὯὧὫὩὤὦὪ.ὮὭὡὤὢὫὡὥὩὩὭ>=stat.ὭὭὬὤὯὧὫὩὤὦὪ.ὮὨὭὯὡὬὫὨὢὭὣ;
  if(inReach&&!needsHeal&&CombatPolicy.Special(control.Character,stamina,gauge,attackBuff,control.Character==1||safeSeconds>1.8||invincible)){
   Move(Vector3.zero);var before=player.ὦὣὧὫὮὯὥὩὠὨὫ;Tap(1);if(!ReferenceEquals(before,player.ὦὣὧὫὮὯὥὩὠὨὫ))specials++;nextAction=now+.3f;reason="释放特殊技能";return;
  }
  if(inReach&&!needsHeal&&now>=nextCharge&&CombatPolicy.Charge(control.Character,stamina,safeSeconds,chargeBuff)){
   Move(Vector3.zero);holdingAttack=true;chargeEntered=false;heldSince=now;lastHold=now;player.OnKeyDown((ὧὧὥὫὯὦὭὩὪὪὦ)0);reason="准备蓄力";return;
  }
  var decision=CombatPolicy.Choose(new CombatFacts{IncomingDamage=incomingDamage,Hp=Hp(player),MaxHp=stat.ὤὡὩὦὣὠὪὫὬὯὤ.ὮὨὭὯὡὬὫὨὢὭὣ,Stamina=stamina,Heals=stat.ὡὣὭὨὭὡὧὠὣὨὧ.ὮὭὡὤὢὫὡὥὩὩὭ,Threat=threat&&!invincible&&(defenseSkill!=skillId||Math.Abs(defenseImpact-impact)>.01f),DodgeReady=now>=nextDodge,HealReady=now>=nextHeal,Idle=ps=="Idle"||ps=="Move"||ps=="Attack",Distance=melee+control.AttackRange-reach,Facing=Vector3.Dot(player.ὥὮὨὩὥὬὣὭὤὠὪ.ὢὮὠὮὭὨὣὢὠὤὧ.forward,dir)},control);
  switch(decision){
   case CombatChoice.Dodge:Move(-dir+Vector3.Cross(Vector3.up,dir)*.8f);var dodgeBefore=player.ὦὣὧὫὮὯὥὩὠὨὫ;Tap(3);if(!ReferenceEquals(dodgeBefore,player.ὦὣὧὫὮὯὥὩὠὨὫ)){dodges++;nextDodge=now+control.DodgeInterval;nextAction=now+.35f;reason="侧向闪避";}else{nextAction=now+.12f;reason="等待当前动作结束";}break;
   case CombatChoice.Wait:reason="等待当前动作结束";break;
   case CombatChoice.Heal:var healsBefore=stat.ὡὣὭὨὭὡὧὠὣὨὧ.ὮὭὡὤὢὫὡὥὩὩὭ;Move(Vector3.zero);Tap(4);if(stat.ὡὣὭὨὭὡὧὠὣὨὧ.ὮὭὡὤὢὫὡὥὩὩὭ<healsBefore){heals++;nextHeal=now+3;nextAction=now+.8f;reason="安全窗口治疗";}else{nextAction=now+.12f;reason="等待当前动作结束";}break;
   case CombatChoice.Retreat:Move(-dir+Vector3.Cross(Vector3.up,dir));reason="避开攻击范围";break;
   case CombatChoice.Recover:Move(-dir);reason="恢复体力";break;
   case CombatChoice.Approach:Move(dir);reason="接近Boss";break;
   case CombatChoice.Face:Move(dir);reason="调整攻击朝向";break;
   case CombatChoice.Attack:if(ps!="Attack")Move(Vector3.zero);var attackBefore=player.ὦὣὧὫὮὯὥὩὠὨὫ;Tap(0);attackRequests++;if(!ReferenceEquals(attackBefore,player.ὦὣὧὫὮὯὥὩὠὨὫ))attacks++;nextAction=now+.12f;reason="普通攻击";break;
  }
 }
 void EndHold(){if(!holdingAttack)return;holdingAttack=false;chargeEntered=false;if(player!=null)player.OnKeyUp((ὧὧὥὫὯὦὭὩὪὪὦ)0);}
 void Release(){try{EndHold();}catch{}if(player!=null&&ownsMove){try{player.OnMove(Vector3.zero);}catch{}}ownsMove=false;}
 void Command(){if(handingOff)return;var b=BD2.LocalIpc.RuntimeFiles.Take(Path.Combine(Root,"command.json"));if(b==null)return;var q=JObject.Parse(System.Text.Encoding.UTF8.GetString(b));string id=(string)q["Id"];try{
  if(!BD2.LocalIpc.RuntimeFiles.TryClaim(id,(long)q["ExpiresUtcTicks"])||(string)q["Session"]!=session||(long)q["ExpiresUtcTicks"]<DateTime.UtcNow.Ticks)throw new Exception("Expired or duplicate command");string kind=(string)q["Kind"];
  if(kind=="stop"){control.Enabled=false;latchedStop=true;Release();}else if(kind=="reset"){latchedStop=false;waitingSince=0;lastError="";endReplies=0;outcome="";}else if(kind=="prepare"){
   var ui=uis.OfType<ActionSelectUI>().Single();int character=(int?)q["Character"]??3,bossGroup=(int?)q["Boss"]??1,difficulty=(int?)q["Difficulty"]??1;
   if(character<1||character>3||bossGroup<1||bossGroup>2||difficulty<1||difficulty>3)throw new Exception("Invalid stage selection");
   var items=ui.GetComponentsInChildren<ActionCharSelectItem>(true);
   ui.OnClickCharSelectItem(items.Single(x=>x.ὡὮὥὪὪὩὩὭὣὬὠ==ὯὣὩὤὢὫὫὬὠὤὬ.ὫὤὨὦὣὨὥὦὮὬὨ.Playable&&x.ὫὣὣὪὪὤὤὨὩὣὢ==character));
   ui.OnClickTabItem(ὯὣὩὤὢὫὫὬὠὤὬ.ὫὤὨὦὣὨὥὦὮὬὨ.Monster);
   ui.OnClickMonsterSelectItem(items.Single(x=>x.ὡὮὥὪὪὩὩὭὣὬὠ==ὯὣὩὤὢὫὫὬὠὤὬ.ὫὤὨὦὣὨὥὦὮὬὨ.Monster&&x.ὩὥὤὧὪὨὨὨὠὨὭ==bossGroup));ui.OnClickMonsterDifficult(difficulty);
  }else if(kind=="open-menu"){
   var field=uis.OfType<GameFieldDefaultUI>().Single();var button=AccessTools.Field(typeof(GameFieldDefaultUI),"_buttonMenu").GetValue(field) as UnityEngine.UI.Button;
   if(button==null||!button.isActiveAndEnabled||!button.IsInteractable())throw new Exception("Menu is not ready");
   field.OnClickUI(button.gameObject);
  }else if(kind=="open-fiend"){
   var hub=uis.OfType<MiniGameHubUI>().Single();bool found=false;
   for(int k=0;k<100;k++){var ev=hub.GetScrollMiniGameData(k);if(ev==null)break;var t=ὫὡὭὬὢὯὭὨὧὭὨ.ὫὣὯὨὥὥὤὩὠὢὮ(ev.EventId);if(t==null)continue;string title=ὧὨὨὨὤὢὨὮὢὫὧ.ὢὪὪὭὤὡὢὫὩὤὠ(t.EventNameLocalTextId);Log("hub-entry",new{ev.Id,ev.EventId,Title=title});if(title.Contains("惡魔")||title.Contains("恶魔")||title.Contains("Fiend Hunter")){hub.ClickPlayMiniGame(ev.Id,ev.EventId);found=true;break;}}
   if(!found)throw new Exception("Fiend Hunter entry not found");
  }else if(kind=="close"){var ui=uis.Single(x=>x.GetType().Name==(string)q["Ui"]);if(!(ui is ActionSelectUI||ui is AchievementUI||ui is ActionMessagePopupUI||ui is ActionSimpleGuideBookPopupUI))throw new Exception("Close not allowed");ui.CloseUI();}else if(kind=="click"){
   var ui=uis.Single(x=>x.GetType().Name==(string)q["Ui"]);string field=(string)q["Field"];
   if(!(ui is ActionMessagePopupUI||ui is ActionMainUI||ui is ActionSelectUI||ui is MiniGameHubUI||ui is MenuUI))throw new Exception("UI not allowed");
   if(ui is ActionSelectUI&&field=="_goPlaySingle"&&!ὦὣὫὣὮὫὪὧὢὩὮ.ὤὥὣὬὬὠὧὮὦὪὨ.IsConnect(ὡὬὬὦὮὠὬὥὯὣὢ.Chat))throw new Exception("游戏匹配服务尚未连接，请等待网络恢复");var f=AccessTools.Field(ui.GetType(),field);var go=f.GetValue(ui) as GameObject;if(go==null||!go.activeInHierarchy)throw new Exception("Button not active");if(ui is ActionSelectUI&&field=="_goPlaySingle"){endReplies=0;matchingUntil=Time.realtimeSinceStartup+45;}ui.OnClickUI(go);
  }else throw new Exception("Unknown command: "+kind);
  Write("receipt-"+id+".json",new{Status="dispatched"});Log("command",q);
 }catch(Exception ex){Write("receipt-"+id+".json",new{Status="error",Error=ex.GetBaseException().Message});}}
 object Ui(UIBase ui)=>new{Type=ui.GetType().Name,Fields=ui.GetType().GetFields(All).Where(x=>x.FieldType==typeof(GameObject)).Select(x=>new{Name=x.Name,Active=(x.GetValue(ui) as GameObject)?.activeInHierarchy}).ToArray(),Texts=ui.GetComponentsInChildren<TMPro.TMP_Text>().Select(x=>x.text).Take(15).ToArray()};
 void Snapshot(){Write("state.json",new{AtUtc=DateTime.UtcNow.ToString("O"),Runtime="PublicRuntime1",Session=session,Enabled=Live,LatchedStop=latchedStop,Reason=reason,Error=lastError,TimeScale=Time.timeScale,NetworkProtection=NetworkGuard.State,NetworkLost=networkLost,NetworkStatus=NetworkConnectivityMonitor.ὥὪὡὦὦὫὢὠὬὮὡ.ὬὨὠὣὡὮὧὩὯὭὭ.ToString(),ActionConnected=ὦὣὫὣὮὫὪὧὢὩὮ.ὩὧὭὣὯὣὨὮὭὯὯ.IsConnect(ὡὬὬὦὮὠὬὥὯὣὢ.Action),GameTime=Time.time,ChatConnected=ὦὣὫὣὮὫὪὧὢὩὮ.ὤὥὣὬὬὠὧὮὦὪὨ.IsConnect(ὡὬὬὦὮὠὬὥὯὣὢ.Chat),ManagerState=Manager?.ὣὢὠὤὤὣὦὧὪὨὬ.ToString(),Player=Unit(player),Boss=Unit(boss),PlayerState=player?.ὥὯὬὢὥὤὧὡὣὡὣ?.ὮὭὡὤὢὫὡὥὩὩὭ.ToString(),BossState=boss?.ὥὯὬὢὥὤὧὡὣὡὣ?.ὮὭὡὤὢὫὡὥὩὩὭ.ToString(),Character=control.Character,Difficulty=control.Difficulty,Specials=specials,Charges=charges,ChargeLevel=(player?.ὦὣὧὫὮὯὥὩὠὨὫ as ὨὮὣὯὭὤὥὢὨὭὧ)?.ὤὪὧὨὤὪὮὦὬὩὤ(),Engagement=engagement,HealSafety=healSafety,AttackRequests=attackRequests,Attacks=attacks,Guards=guards,Dodges=dodges,Heals=heals,Hurtboxes=bossBoxes.Where(x=>x!=null&&x.Type==ActionColliderData.ὯὨὩὬὢὡὮὯὭὨὧ.Hurt).Select(x=>new{x.name,Position=Vec(x.transform.position),Active=x.gameObject.activeInHierarchy&&x.Collider.enabled}).ToArray(),EndReplies=endReplies,Outcome=outcome,NetworkErrors=networkErrors,LastReply=lastReply,UIs=uis.Select(Ui).ToArray()});}
 void OnDestroy(){Release();NetworkGuard.Remove();harmony.UnpatchAll(harmony.Id);if(current==this)current=null;}
}
}
