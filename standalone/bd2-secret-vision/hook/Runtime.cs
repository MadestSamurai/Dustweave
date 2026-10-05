using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using HarmonyLib;
using BD2SecretVision.Rules;
namespace BD2SecretVision.Runtime {
public static class Loader {
 static object engine;static bool resolver;static BD2.LocalIpc.Handoff handoff;static BD2.LocalIpc.MainThread frame;
 static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2SecretVisionAssistant");
 public static void Load(){lock(typeof(Loader)){
  if(handoff==null)handoff=new BD2.LocalIpc.Handoff(typeof(Loader).Assembly.FullName,"secret-vision","secret-vision",false,Start,Pause,Busy,Stop,Status);
  if(!handoff.IsActive&&!handoff.Pending)BD2.LocalIpc.RuntimeFiles.Start(Root,BD2.LocalIpc.Build.Fingerprint,"state.json|error.json|control.json|command.json|runtime.json|stop|receipt-*");
  handoff.Request(DateTime.UtcNow);
  if(frame==null)frame=new BD2.LocalIpc.MainThread(()=>{BD2.LocalIpc.LegacyPilots.Discover();handoff.Tick(DateTime.UtcNow);return handoff.Pending;},handoff.Fail);frame.Schedule();
 }}
 static void Start(){BD2.LocalIpc.RuntimeFiles.Start(Root,BD2.LocalIpc.Build.Fingerprint,"state.json|error.json|control.json|command.json|runtime.json|stop|receipt-*");if(!resolver){AppDomain.CurrentDomain.AssemblyResolve+=Resolve;resolver=true;}engine=typeof(Loader).Assembly.GetType("BD2SecretVision.Runtime.Engine",true).GetMethod("Create",BindingFlags.Static|BindingFlags.Public).Invoke(null,null);}
 static object Invoke(string name){return engine.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.Public).Invoke(engine,null);}
 static void Pause(){BD2.LocalIpc.RuntimeFiles.Revoke();if(engine!=null)Invoke("PrepareHandoff");}
 static string Busy(){return engine==null?"":(string)Invoke("HandoffBusy");}
 static void Stop(){if(engine!=null)Invoke("StopHook");engine=null;if(resolver){AppDomain.CurrentDomain.AssemblyResolve-=Resolve;resolver=false;}}
 public static void Unload(){BD2.LocalIpc.MainThread.Drain(()=>{if(handoff!=null)handoff.Unload();},Status);}
 public class StatusFrame { public string AtUtc {get;set;} public string State {get;set;} public string Error {get;set;} }
 static void Status(string state,string error){if(state=="active"&&handoff!=null&&handoff.IsActive)BD2.LocalIpc.RuntimeFiles.Activate();var value=new StatusFrame{AtUtc=DateTime.UtcNow.ToString("O"),State=state,Error=error};if(state=="error")WriteStatus("error.json",value);WriteStatus("runtime.json",value);}
 static void WriteStatus(string name,object value){using(var buffer=new MemoryStream()){new System.Runtime.Serialization.Json.DataContractJsonSerializer(value.GetType()).WriteObject(buffer,value);BD2.LocalIpc.RuntimeFiles.Write(Path.Combine(Root,name),buffer.ToArray());}}
 static Assembly Resolve(object s,ResolveEventArgs e){if(new AssemblyName(e.Name).Name!="0Harmony")return null;using(var a=typeof(Loader).Assembly.GetManifestResourceStream("SecretVision.Harmony.dll"))using(var b=new MemoryStream()){a.CopyTo(b);return Assembly.Load(b.ToArray());}}
}
[DefaultExecutionOrder(-20000)]
public sealed class Engine:MonoBehaviour {
 internal static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2SecretVisionAssistant");
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static Engine current;bool retired;bool handingOff;bool settlementPending;
 public static object Create(){var go=new GameObject("BD2 Secret Vision");UnityEngine.Object.DontDestroyOnLoad(go);return go.AddComponent<Engine>();}
 public void PrepareHandoff(){if(handingOff)return;handingOff=true;controlled=false;Stop("component-handoff",running&&Playing);}
 public string HandoffBusy(){return settlementPending?"等待小游戏结算回读":"";}
 public void StopHook(){PrepareHandoff();retired=true;enabled=false;new Harmony("bd2.secretvision.public4").UnpatchAll("bd2.secretvision.public4");if(current==this)current=null;UnityEngine.Object.Destroy(gameObject);}

 readonly Dictionary<string,FieldInfo> fields=new Dictionary<string,FieldInfo>();
 readonly Dictionary<int,Component> uiTargets=new Dictionary<int,Component>();
 HopscotchManager manager;HopscotchGridMap grid;HopscotchPlayerController player;HopscotchEnemyController enemy;
 sealed class Body {public Collider Collider;public Vector2 Previous,Velocity;public float At;}
 readonly Dictionary<int,Body> bodies=new Dictionary<int,Body>();float nextBodies;float lastControl;
 readonly List<Vector2> wallOrigins=new List<Vector2>();int wallGridId;string lastCarve="";
 List<Node> route=new List<Node>();int cursor;int boardId;bool running;bool guard=true;bool escaping;bool hadDrawing;
 DateTime nextRead,nextWrite,deadline,lastProgress,nextGate,nextTransit,gateSince;string gateReason="";List<string> cachedRows;int cachedGridId;float cachedPercent=-1;Vector2Int lastVertex;float startedTime;string reason="observing";string session=Guid.NewGuid().ToString("N"); string owner="",round=""; int stage; bool controlled; int lastManager; readonly long processStart=System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
 public class Node {public int X;public int Y;public bool Claim;public float WaitBeforeS;}
 [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]static extern bool MoveFileEx(string existing,string replacement,int flags);
 public static void Write(string name,object value){var path=Path.Combine(Root,name);if(BD2.LocalIpc.RuntimeFiles.Write(path,System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value))))return;Directory.CreateDirectory(Root);var temp=path+".tmp";File.WriteAllText(temp,JsonConvert.SerializeObject(value));if(!MoveFileEx(temp,path,1|8))throw new IOException("Snapshot publication failed: "+System.Runtime.InteropServices.Marshal.GetLastWin32Error());}
 void Event(string kind,object data){try{var ep=Path.Combine(Root,"events.jsonl");if(File.Exists(ep)&&new FileInfo(ep).Length>4000000){var old=ep+".previous";if(File.Exists(old))File.Delete(old);File.Move(ep,old);}File.AppendAllText(Path.Combine(Root,"events.jsonl"),JsonConvert.SerializeObject(new{AtUtc=DateTime.UtcNow.ToString("O"),Session=session,Frame=Time.frameCount,Kind=kind,Data=data})+"\n");}catch(IOException){}}
 object Field(object o,string name){var k=o.GetType().FullName+name;FieldInfo f;if(!fields.TryGetValue(k,out f)){for(var t=o.GetType();t!=null&&f==null;t=t.BaseType)f=t.GetField(name,All|BindingFlags.DeclaredOnly);if(f==null)throw new MissingFieldException(o.GetType().Name,name);fields[k]=f;}return f.GetValue(o);}
 T Read<T>(object o,string name){return (T)Field(o,name);}
 void Awake(){current=this;Directory.CreateDirectory(Root);var h=new Harmony("bd2.secretvision.public4");h.Patch(typeof(HopscotchPlayerController).GetMethod("ὧὭὦὫὪὬὭὭὮὥὧ",All),postfix:new HarmonyMethod(typeof(Engine),nameof(Arrived)));
 // Observe the game's successful stage-record update, without altering the request or response.
 var type=typeof(HopscotchManager).Assembly.GetType("ὥὯὯὤὫὠὢὯὠὤὤ");
 var records=type.GetMethods(All).Where(m=>m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType.FullName=="Proto.Net.MiniGameHopscotchStageDBInfo").ToArray();
 foreach(var m in records)h.Patch(m,postfix:new HarmonyMethod(typeof(Engine),nameof(StageRecord)));
 h.Patch(typeof(HopscotchPlayerController).GetMethod("FailTrail",All),prefix:new HarmonyMethod(typeof(Engine),nameof(Failing)));
 h.Patch(typeof(HopscotchManager).GetMethod("RequestGameEnd",All),prefix:new HarmonyMethod(typeof(Engine),nameof(Ending)));
 Event("connected",new{Pid=System.Diagnostics.Process.GetCurrentProcess().Id,StageObservers=records.Select(x=>x.Name).ToArray()});}
 public static void Failing(object[] __args){try{if(current!=null)current.Event("trail-failed",new{Args=__args,Stack=Environment.StackTrace,Cursor=current.cursor,Player=XY(current.Position),PlayerRadius=current.Radius(current.player),Projectiles=current.Projectiles()});}catch{}}
 public static void Ending(object[] __args){try{if(current!=null){current.settlementPending=true;var data=new{AtUtc=DateTime.UtcNow.ToString("O"),Session=current.session,Round=current.round,Stage=current.stage,Percent=(int)__args[0],GridPercent=current.grid.GetClaimedPercentExcludeWall()*100,Remaining=current.Field(current.manager,"ὩὪὭὬὢὭὫὢὮὢὮ"),Continued=current.Field(current.manager,"ὩὡὭὤὡὭὪὣὯὩὣ")};Write("end.json",data);current.Event("game-end",data);}}catch{}}
 public static void StageRecord(object[] __args){try{if(current!=null)current.settlementPending=false;Write("server-stage.json",new{AtUtc=DateTime.UtcNow.ToString("O"),Session=current==null?"":current.session,Round=current==null?"":current.round,Data=__args[0].ToString()});}catch{}}
 public static void Arrived(HopscotchPlayerController __instance){if(current!=null&&current.running&&current.player==__instance){try{current.Drive(true);}catch(Exception e){current.Stop("arrival-error",true);Write("error.json",new{Error=e.ToString()});}}}
 bool Playing {get{return manager!=null&&!manager.ὩὩὮὣὤὦὮὩὢὣὠ;}}
 Vector2Int Vertex {get{return player.ὭὧὢὢὡὠὨὫὧὨὬ;}}
 Vector2 Position {get{return player.ὡὢὥὧὮὪὪὪὪὧὠ;}}
 void Acquire(){manager=HopscotchManager.ὪὫὢὨὯὭὦὪὦὨὣ;if(manager==null){grid=null;player=null;enemy=null;return;}if(lastManager!=manager.GetInstanceID()){lastManager=manager.GetInstanceID();bodies.Clear();lastControl=0;wallOrigins.Clear();wallGridId=0;lastCarve="";}grid=manager.ὦὢὬὢὬὣὭὪὠὦὭ;player=manager.ὠὣὡὬὯὠὣὠὭὯὪ;enemy=manager.ὦὠὪὩὥὡὯὩὣὨὩ;}
 void Update(){if(retired)return;try{
 if(DateTime.UtcNow>=nextRead){nextRead=DateTime.UtcNow.AddMilliseconds(100);Acquire();if(!handingOff){ReadControl();ReadCommand();}}
 if(Playing&&Time.time>=nextBodies){nextBodies=Time.time+.1f;TrackBodies();var control=ControlRemaining;if((control>0)!=(lastControl>0)||control>lastControl+.2f)Event("control-state",new{Remaining=control,TimeStop=TimeStopRemaining,Power=PowerRemaining,State=Field(enemy,"ὭὥὨὤὠὥὡὪὩὯὨ").ToString()});lastControl=control;var carve=CarveState+":"+CarveReadyInterrupted;if(carve!=lastCarve){lastCarve=carve;Event("attack-opportunity",new{EnemyState=EnemyState,CarveState=CarveState,CarveReadyInterrupted=CarveReadyInterrupted,CarveBlockedRemaining=CarveBlockedRemaining,ControlRemaining=control});}}
 if(running)Drive(false);
 else if(controlled&&Playing&&player!=null&&!Read<bool>(player,"ὤὫὥὩὣὦὮὫὡὪὮ")&&!Read<bool>(player,"ὣὭὥὠὨὬὧὣὮὢὦ")&&DateTime.UtcNow>=nextTransit&&Vector2.Distance(Position,grid.VertexToUIPos(Vertex))<.02f){
  nextTransit=DateTime.UtcNow.AddMilliseconds(60);if(BorderThreat(.9f))BeginEscape();
 }
 if(DateTime.UtcNow>=nextWrite){nextWrite=DateTime.UtcNow.AddMilliseconds(grid!=null?150:800);try{Snapshot();}catch(IOException){}}
 }catch(Exception e){running=false;reason=e.GetBaseException().Message;try{if(Playing)manager.RequestPaused();Write("error.json",new{AtUtc=DateTime.UtcNow.ToString("O"),Error=e.ToString()});}catch{}}}
 void Stop(string why,bool pause){running=false;reason=why;if(player!=null){player.OnTouchDirection(Vector2.zero,true);player.OnTouchPadOK(false);}if(pause&&Playing)manager.RequestPaused();Event("stopped",new{Reason=why,Cursor=cursor,Count=route.Count});}
 void Drive(bool arrival){
 // Ownership is checked before gameplay state, including manual pauses.
 if(!controlled||DateTime.UtcNow>deadline||BD2.LocalIpc.RuntimeFiles.Read(Path.Combine(Root,"stop"))!=null){Stop("lease/stop",true);return;}
 if(manager==null||manager.GetInstanceID()!=boardId){Stop("board changed",false);return;}
 if(!Playing){Stop("game-"+Field(manager,"ὢὠὤὪὧὨὫὦὯὨὯ").ToString(),false);return;}
 var v=Vertex;
 if(v!=lastVertex){lastProgress=DateTime.UtcNow;lastVertex=v;}
 // Do not overwrite a pending closure until its LateUpdate flood fill has completed.
 if(Read<bool>(player,"ὣὭὥὠὨὬὧὣὮὢὦ"))return;
 bool drawingNow=Read<bool>(player,"ὤὫὥὩὣὦὮὫὡὪὮ");
 if(drawingNow)hadDrawing=true;
 else if(hadDrawing){hadDrawing=false;var done=escaping?"escape-completed":"route-completed";escaping=false;Stop(done,false);return;}
 while(cursor<route.Count&&route[cursor].X==v.x&&route[cursor].Y==v.y){cursor++;}
 if(cursor>=route.Count){var result=escaping?"escape-completed":"route-completed";escaping=false;Stop(result,false);return;}
 // Forecast safe-border transfers too: an escape must not immediately walk back into the pursuer.
 if(!drawingNow&&!escaping&&Vector2.Distance(Position,grid.VertexToUIPos(v))<.02f&&DateTime.UtcNow>=nextTransit){
 nextTransit=DateTime.UtcNow.AddMilliseconds(60);
 if(BorderThreat(.9f)||(!route[cursor].Claim&&(BorderTransitRisk(.9f)||BodyTransitRisk(.9f)))){if(!BeginEscape())Stop("no-safe-border-escape",true);return;}
 }
 if(cursor<route.Count&&route[cursor].WaitBeforeS>0&&!Read<bool>(player,"ὤὫὥὩὣὦὮὫὡὪὮ")){
 lastProgress=DateTime.UtcNow;
 if(DateTime.UtcNow<nextGate){player.OnTouchDirection(Vector2.zero,true);player.OnTouchPadOK(false);return;}
 nextGate=DateTime.UtcNow.AddMilliseconds(80);
 if(!SafeDeparture()){if(gateSince==default(DateTime))gateSince=DateTime.UtcNow;if(DateTime.UtcNow-gateSince>TimeSpan.FromSeconds(.35)){Event("departure-blocked",new{Gate=gateReason,EnemyState=EnemyState,CarveState=CarveState,CarveReadyInterrupted=CarveReadyInterrupted,CarveBlockedRemaining=CarveBlockedRemaining,ControlRemaining=ControlRemaining});Stop("replan-window",false);gateSince=default(DateTime);return;}player.OnTouchDirection(Vector2.zero,true);player.OnTouchPadOK(false);reason="waiting-"+gateReason;if(BorderThreat(.9f)||BodyTransitRisk(.9f,true)){if(!BeginEscape())Stop("no-safe-border-escape",true);}return;}
 Event("departure",new{Cursor=cursor,Gate=gateReason,EnemyState=EnemyState,CarveState=CarveState,CarveReadyInterrupted=CarveReadyInterrupted,CarveBlockedRemaining=CarveBlockedRemaining,ControlRemaining=ControlRemaining,Speed=Field(player,"ὮὬὤὪὫὤὢὥὠὦὪ"),Radius=Radius(player),Bodies=Bodies(),Projectiles=Projectiles()});route[cursor].WaitBeforeS=0;reason="executing";
 }
 if(DateTime.UtcNow-lastProgress>TimeSpan.FromSeconds(2)){Stop("no-progress",true);return;}
 var n=route[cursor];var delta=new Vector2(n.X-v.x,n.Y-v.y);
 if(!drawingNow&&Vector2.Distance(Position,grid.VertexToUIPos(v))<.02f){var step=v+new Vector2Int(Math.Sign(delta.x),Math.Sign(delta.y));if(!n.Claim&&!grid.IsSafeBorderEdgeHV(v,step)||n.Claim&&!grid.IsSafeBorderEdgeHV(v,step)&&!grid.IsDrawableEdgeHV(v,step)){Stop("replan-topology",false);return;}}
 if(delta.x!=0&&delta.y!=0){Stop("off-route",true);return;}
 // Local danger guard is conservative; stop via normal pause for this supervised experiment.
 if(guard&&enemy!=null&&(!grid.IsBorderVertex(v)||BoundaryBodyThreat)&&Time.frameCount%3==0){var ep=manager.ὨὢὧὭὡὠὭὠὡὬὩ.WorldToMapUI(enemy.transform.position);if(Vector2.Distance(ep,Position)<grid.ὤὠὡὤὪὠὬὡὥὢὠ*4){Stop("enemy-near",true);return;}}
 player.OnTouchPadOK(n.Claim);player.OnTouchDirection(delta.normalized,false);
 }
 static IEnumerable<FieldInfo> Fields(Type t){for(;t!=null&&t!=typeof(MonoBehaviour);t=t.BaseType)foreach(var f in t.GetFields(All|BindingFlags.DeclaredOnly).Where(x=>!x.IsStatic))yield return f;}
 static GameObject GO(object x){if(x!=null&&x.GetType().Name=="ButtonInfo"){var prop=x.GetType().GetProperty("GoRoot",All);if(prop!=null)return prop.GetValue(x,null) as GameObject;}return x is GameObject?(GameObject)x:x is Component?((Component)x).gameObject:null;}
 object UIs(){uiTargets.Clear();var result=new List<object>();foreach(var u in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()){
 var name=u.GetType().Name;if(!(name.StartsWith("Hopscotch")&&name.EndsWith("UI"))&&name!="MiniGameHubScrollItem")continue;if(!u.gameObject.activeInHierarchy)continue;
 var links=new List<object>();uiTargets[u.GetInstanceID()]=u;
 foreach(var f in Fields(u.GetType())){object val;try{val=f.GetValue(u);}catch{continue;}var go=GO(val);if(go!=null&&go.activeInHierarchy&&go.transform.IsChildOf(u.transform))links.Add(new{Field=f.Name,Name=go.name,Id=go.GetInstanceID()});}
 var texts=u.GetComponentsInChildren<TMPro.TMP_Text>().Where(x=>x.gameObject.activeInHierarchy).Select(x=>x.text).ToArray();result.Add(new{Id=u.GetInstanceID(),Type=name,Links=links,Texts=texts});}return result;}
 float Number(object o,string name){return Convert.ToSingle(Field(o,name));}
 float ColliderRadius(Collider c,Vector2 origin){
 var bridge=manager.ὨὢὧὭὡὠὭὠὡὬὩ;
 if(c is SphereCollider){var sphere=(SphereCollider)c;var center=sphere.transform.TransformPoint(sphere.center);var scale=sphere.transform.lossyScale;var r=sphere.radius*Math.Max(Math.Abs(scale.x),Math.Max(Math.Abs(scale.y),Math.Abs(scale.z)));var p=bridge.WorldToMapUI(center);var dx=bridge.WorldToMapUI(center+Vector3.right)-p;var dz=bridge.WorldToMapUI(center+Vector3.forward)-p;return Vector2.Distance(origin,p)+(float)ProjectionBound.Maximum(dx.x,dx.y,dz.x,dz.y)*r;}
 var b=c.bounds;float result=0;foreach(var dx in new[]{-1,1})foreach(var dz in new[]{-1,1})result=Math.Max(result,Vector2.Distance(origin,bridge.WorldToMapUI(b.center+new Vector3(b.extents.x*dx,0,b.extents.z*dz))));return result;
 }
 float Radius(Component component){float result=0;var origin=manager.ὨὢὧὭὡὠὭὠὡὬὩ.WorldToMapUI(component.transform.position);foreach(var c in component.GetComponentsInChildren<Collider>()){if(c.enabled&&c.gameObject.activeInHierarchy)result=Math.Max(result,ColliderRadius(c,origin));}return result;}
 float ProjectileSpeed(HopscotchProjectileBase shot){var speed=Number(shot,"ὮὬὤὪὫὤὢὥὠὦὪ");if(!(shot is HopscotchProjectileSplit))return speed;var bridge=manager.ὨὢὧὭὡὠὭὠὡὬὩ;var origin=shot.transform.position;var p=bridge.WorldToMapUI(origin);var dx=bridge.WorldToMapUI(origin+new Vector3(speed,0,0))-p;var dy=bridge.WorldToMapUI(origin+new Vector3(0,0,speed))-p;return (float)ProjectionBound.Maximum(dx.x,dx.y,dy.x,dy.y);}
 object[] Projectiles(){return manager.ὩὣὭὠὮὭὢὢὡὧὫ==null?new object[0]:manager.ὩὣὭὠὮὭὢὢὡὧὫ.GetComponentsInChildren<HopscotchProjectileBase>().Where(x=>x.isActiveAndEnabled).Select(x=>(object)new{Type=x.GetType().Name,Radius=Radius(x),Position=XY(manager.ὨὢὧὭὡὠὭὠὡὬὩ.WorldToMapUI(x.transform.position)),BorderMode=x is HopscotchProjectileBorder?Field(x,"ὡὧὢὡὫὢὣὬὩὯὠ").ToString():null,Direction=XY(Read<Vector2>(x,"ὡὮὤὩὧὦὡὣὥὭὩ")),Speed=ProjectileSpeed(x),FrozenRemaining=ProjectileFrozenRemaining(x),SourceSpeed=Number(x,"ὮὬὤὪὫὤὢὥὠὦὪ"),Remaining=Number(x,"ὧὬὪὭὦὬὤὨὥὠὩ")-Number(x,"ὩὠὣὮὬὧὬὯὢὡὩ"),Attached=x.ὮὥὠὡὨὮὣὩὩὡὥ}).ToArray();}
 static char EdgeCode(ὦὪὤὦὣὭὩὭὠὮὮ edge){return edge==ὦὪὤὦὣὭὩὭὠὮὮ.ClaimedBorder?'C':edge==ὦὪὤὦὣὭὩὭὠὮὮ.Wall?'#':edge==ὦὪὤὦὣὭὩὭὠὮὮ.Trail?'T':'.';}
 static string[] EdgeRows(ὦὪὤὦὣὭὩὭὠὮὮ[,] edges){var rows=new string[edges.GetLength(1)];for(int y=0;y<rows.Length;y++){var row=new char[edges.GetLength(0)];for(int x=0;x<row.Length;x++)row[x]=EdgeCode(edges[x,y]);rows[y]=new string(row);}return rows;}
 static string TopologyKey(IEnumerable<string> cells,IEnumerable<string> horizontal,IEnumerable<string> vertical){ulong hash=14695981039346656037UL;unchecked{foreach(var rows in new[]{cells,horizontal,vertical})foreach(var row in rows){foreach(char c in row){hash^=c;hash*=1099511628211UL;}hash^=255;hash*=1099511628211UL;}}return hash.ToString("X16");}
 void Snapshot(){
 object board=null;
 if(grid!=null&&grid.Cells!=null&&player!=null){
 var percent=grid.GetClaimedPercentExcludeWall()*100;
 {cachedRows=new List<string>();for(int y=0;y<grid.Cells.GetLength(1);y++){var chars=new char[grid.Cells.GetLength(0)];for(int x=0;x<chars.Length;x++){var c=grid.Cells[x,y];chars[x]=c==ὤὪὣὯὠὭὢὨὡὬὪ.Claimed?'C':c==ὤὪὣὯὠὭὢὨὡὬὪ.Wall?'#':'.';}cachedRows.Add(new string(chars));}cachedGridId=grid.GetInstanceID();cachedPercent=percent;}
 var horizontal=EdgeRows(Read<ὦὪὤὦὣὭὩὭὠὮὮ[,]>(grid,"ὯὥὤὠὮὥὫὬὭὦὩ"));var vertical=EdgeRows(Read<ὦὪὤὦὣὭὩὭὠὮὮ[,]>(grid,"ὭὣὮὢὠὨὤὩὫὫὢ"));
 var items=UnityEngine.Object.FindObjectsOfType<HopscotchItemBase>().Where(x=>x.isActiveAndEnabled).Select(x=>new{Id=x.GetInstanceID(),Type=x.GetType().Name,Position=XY(manager.ὨὢὧὭὡὠὭὠὡὬὩ.WorldToMapUI(x.transform.position)),Remaining=Number(x,"ὧὬὪὭὦὬὤὨὥὠὩ")-Number(x,"ὩὠὣὮὬὧὬὯὢὡὩ"),Collected=Read<bool>(x,"ὨὤὣὯὨὠὧὠὫὠὡ")}).ToArray();
 board=new{Id=manager.GetInstanceID(),State=Field(manager,"ὢὠὤὪὧὨὫὦὯὨὯ").ToString(),Percent=percent,Remaining=Field(manager,"ὩὪὭὬὢὭὫὢὮὢὮ"),Continued=Field(manager,"ὩὡὭὤὡὭὪὣὯὩὣ"),CellSize=grid.ὤὠὡὤὪὠὬὡὥὢὠ,Width=grid.Cells.GetLength(0),Height=grid.Cells.GetLength(1),Rows=cachedRows,HorizontalEdges=horizontal,VerticalEdges=vertical,TopologyKey=TopologyKey(cachedRows,horizontal,vertical),
 Player=new{Radius=Radius(player),X=Vertex.x,Y=Vertex.y,U=Position.x,V=Position.y,Speed=Field(player,"ὮὬὤὪὫὤὢὥὠὦὪ"),SpeedStacks=Field(player,"ὡὦὤὪὯὬὨὩὪὦὬ"),SlowStacks=Field(player,"ὨὩὤὪὥὢὭὧὮὡὩ"),SuperBonus=Field(player,"ὩὢὯὦὬὦὬὫὫὣὯ"),SuperRemaining=Math.Max(0,Number(player,"ὥὫὬὣὦὡὥὫὮὨὥ")-Number(player,"ὬὭὥὯὨὬὪὫὧὣὯ")),Drawing=Field(player,"ὤὫὥὩὣὦὮὫὡὪὮ"),Closing=Field(player,"ὣὭὥὠὨὬὧὣὮὢὦ")},
 Enemy=enemy==null?null:new{Monster=Field(enemy,"ὣὭὪὮὡὪὧὬὣὫὯ").ToString(),NormalPattern=Field(enemy,"ὫὨὧὭὩὢὢὭὨὢὦ"),SpecialPattern=Field(enemy,"ὭὬὭὢὯὣὯὪὧὫὪ"),Position=XY(manager.ὨὢὧὭὡὠὭὠὡὬὩ.WorldToMapUI(enemy.transform.position)),State=Field(enemy,"ὭὥὨὤὠὥὡὪὩὯὨ").ToString(),NormalElapsed=Field(enemy,"ὤὧὩὢὪὡὧὪὣὠὮ"),NormalInterval=Field(enemy,"ὥὮὯὥὯὨὭὫὧὬὠ"),SpecialElapsed=Field(enemy,"ὠὭὫὬὡὤὣὧὠὪὥ"),CarveState=CarveState,CarveReadyInterrupted=CarveReadyInterrupted,CarveBlockedRemaining=CarveBlockedRemaining,ControlRemaining=ControlRemaining,TimeStopRemaining=TimeStopRemaining,PowerRemaining=PowerRemaining,SpecialInterval=Field(enemy,"ὦὤὥὭὪὬὤὨὨὦὨ")},Items=items,Bodies=Bodies(),Projectiles=Projectiles()};}
 Write("state.json",new{AtUtc=DateTime.UtcNow.ToString("O"),Protocol=1,Pid=System.Diagnostics.Process.GetCurrentProcess().Id,ProcessStart=processStart,Session=session,Round=round,Stage=stage,Frame=Time.frameCount,Running=running,Reason=reason,Cursor=cursor,Count=route.Count,Board=board,UIs=grid!=null?new object[0]:UIs()});}
 float Travel(float distance){var speed=Number(player,"ὮὬὤὪὫὤὢὥὠὦὪ");var remain=Math.Max(0,Number(player,"ὥὫὬὣὦὡὥὫὮὨὥ")-Number(player,"ὬὭὥὯὨὬὪὫὧὣὯ"));var baseSpeed=speed-Number(player,"ὩὢὯὦὬὦὬὫὫὣὯ")*(1-Number(player,"ὨὩὤὪὥὢὭὧὮὡὩ")*.1f);return distance<=speed*remain?distance/Math.Max(1,speed):remain+(distance-speed*remain)/Math.Max(1,baseSpeed);}
 Vector2 BodyCenter(Collider c){return manager.ὨὢὧὭὡὠὭὠὡὬὩ.WorldToMapUI(c.bounds.center);}
 float BodyRadius(Collider c){return ColliderRadius(c,BodyCenter(c));}
 void TrackBodies(){if(enemy==null)return;var colliders=enemy.GetComponentsInChildren<Collider>().Concat(UnityEngine.Object.FindObjectsOfType<HopscotchTrailSegment>().SelectMany(x=>x.GetComponentsInChildren<Collider>())).Distinct();foreach(var c in colliders){if(!c.enabled||!c.gameObject.activeInHierarchy)continue;var pos=BodyCenter(c);Body b;if(!bodies.TryGetValue(c.GetInstanceID(),out b)){b=new Body{Collider=c,Previous=pos,At=Time.time};bodies[c.GetInstanceID()]=b;}var dt=Time.time-b.At;if(dt>.01f){b.Velocity=(pos-b.Previous)/dt;b.At=Time.time;b.Previous=pos;}}}
 Vector2 AtDistance(List<Vector2> points,float distance){for(int i=1;i<points.Count;i++){var length=Vector2.Distance(points[i-1],points[i]);if(distance<=length)return Vector2.MoveTowards(points[i-1],points[i],distance);distance-=length;}return points[points.Count-1];}
 Vector2 HeadVelocity(){var d=Read<Vector2>(enemy,"ὡὮὤὩὧὦὡὣὥὭὩ");var speed=Number(enemy,"ὮὬὤὪὫὤὢὥὠὦὪ");var p=enemy.transform.position;var bridge=manager.ὨὢὧὭὡὠὭὠὡὬὩ;return bridge.WorldToMapUI(p+new Vector3(d.x,0,d.y)*speed)-bridge.WorldToMapUI(p);}
 float HeadHorizon(){
 if(enemy==null||ControlRemaining>0||Field(enemy,"ὭὥὨὤὠὥὡὪὩὯὨ").ToString()!="Move"||Field(enemy,"_carveState").ToString()!="None")return 0;
 var horizon=Math.Min(5f,5f-Number(enemy,"ὠὠὦὧὦὬὣὨὡὬὤ"));
 horizon=Math.Min(horizon,Number(enemy,"ὥὮὯὥὯὨὭὫὧὬὠ")-Number(enemy,"ὤὧὩὢὪὡὧὪὣὠὮ"));
 horizon=Math.Min(horizon,Number(enemy,"ὦὤὥὭὪὬὤὨὨὦὨ")-Number(enemy,"ὠὭὫὬὡὤὣὧὠὪὥ"));horizon=Math.Max(0,horizon-.08f);
 var velocity=HeadVelocity();var step=Math.Min(.02f,grid.ὤὠὡὤὪὠὬὡὥὢὠ*.4f/Math.Max(1,velocity.magnitude));var origin=manager.ὨὢὧὭὡὠὭὠὡὬὩ.WorldToMapUI(enemy.transform.position);var previous=grid.UIPosToCell(origin);
 for(float t=step;t<=horizon+step;t+=step){var cell=grid.UIPosToCell(origin+velocity*Math.Min(t,horizon));ὦὪὤὦὣὭὩὭὠὮὮ edge;if(!grid.InBounds(cell)||grid.IsSolidCell(cell)||(grid.TryGetEdgeBetweenCells(previous,cell,out edge)&&(edge==ὦὪὤὦὣὭὩὭὠὮὮ.ClaimedBorder||edge==ὦὪὤὦὣὭὩὭὠὮὮ.Trail)))return Math.Max(0,t-step-.04f);previous=cell;}
 return horizon;
 }
 bool IsHead(Collider c){return c.GetComponentInParent<HopscotchTrailSegment>()==null&&c.GetComponentInParent<HopscotchEnemyController>()==enemy;}
 object[] Bodies(){return bodies.Values.Where(x=>x.Collider!=null&&x.Collider.enabled&&x.Collider.gameObject.activeInHierarchy).Select(x=>(object)new{FrozenRemaining=BodyFrozenRemaining(x.Collider),MotionHorizon=IsHead(x.Collider)?HeadHorizon():0,MotionVelocity=XY(IsHead(x.Collider)?HeadVelocity():Vector2.zero),Collider=x.Collider.GetType().Name,Position=XY(BodyCenter(x.Collider)),Velocity=XY(x.Velocity),SpeedBound=Math.Max(BodySpeedBound(),x.Velocity.magnitude),Radius=BodyRadius(x.Collider)}).ToArray();}
 float TimeStopRemaining {get{return manager!=null&&Read<bool>(manager,"ὯὤὩὦὥὥὬὬὦὩὩ")?Math.Max(0,Number(manager,"ὮὤὣὮὩὫὭὩὨὠὫ")):0;}}
 float PowerRemaining {get{return enemy!=null&&Read<bool>(enemy,"ὧὫὦὠὣὫὭὡὮὭὯ")?Math.Max(0,Number(enemy,"ὠὪὤὥὮὮὩὩὥὨὮ")-Number(enemy,"ὣὯὡὪὢὢὥὨὪὣὥ")):0;}}
 float ControlRemaining {get{if(enemy==null||Field(enemy,"ὭὥὨὤὠὥὡὪὩὯὨ").ToString()=="Respawn")return 0;var power=PowerRemaining;if(power>0)return power;return Field(enemy,"ὭὥὨὤὠὥὡὪὩὯὨ").ToString()=="Stop"?TimeStopRemaining:0;}}
 string EnemyState {get{return enemy==null?"":Field(enemy,"ὭὥὨὤὠὥὡὪὩὯὨ").ToString();}}
 string CarveState {get{return enemy==null?"None":Field(enemy,"_carveState").ToString();}}
 bool CarveReadyInterrupted {get{return enemy!=null&&CarveState=="Ready"&&Field(enemy,"ὭὣὮὮὣὯὭὪὭὮὮ")==null;}}
 float WallDistance(){
  if(grid==null||grid.Cells==null)return 0;
  if(wallGridId!=grid.GetInstanceID()){
   wallOrigins.Clear();wallGridId=grid.GetInstanceID();
   for(int y=0;y<grid.Cells.GetLength(1);y++)for(int x=0;x<grid.Cells.GetLength(0);x++)if(grid.Cells[x,y]==ὤὪὣὯὠὭὢὨὡὬὪ.Wall)wallOrigins.Add(grid.VertexToUIPos(new Vector2Int(x,y)));
  }
  var p=manager.ὨὢὧὭὡὠὭὠὡὬὩ.WorldToMapUI(enemy.transform.position);double distance=double.MaxValue;
  foreach(var wall in wallOrigins)distance=Math.Min(distance,AttackOpportunity.DistanceToWall(p.x,p.y,wall.x,wall.y,grid.ὤὠὡὤὪὠὬὡὥὢὠ));
  return wallOrigins.Count==0?0:(float)Math.Max(0,distance-grid.ὤὠὡὤὪὠὬὡὥὢὠ*2);
 }
 float CarveBlockedRemaining {get{
  var carve=CarveState;if(carve!="Ready"&&carve!="Progress")return 0;
  return (float)AttackOpportunity.CarveSeconds(EnemyState,carve,CarveReadyInterrupted,WallDistance(),BodySpeedBound(),ControlRemaining,TimeStopRemaining);
 }}
 float BodyFrozenRemaining(Collider body){return IsHead(body)?(float)AttackOpportunity.StationarySeconds(EnemyState,CarveState,CarveReadyInterrupted,ControlRemaining,TimeStopRemaining):ControlRemaining;}
 float ProjectileFrozenRemaining(HopscotchProjectileBase shot){return shot is HopscotchProjectileSplit&&Read<bool>(shot,"ὯὤὩὦὥὥὬὬὦὩὩ")?TimeStopRemaining:0;}
 float BodySpeedBound(){var speed=Number(enemy,"ὮὬὤὪὫὤὢὥὠὦὪ");var bridge=manager.ὨὢὧὭὡὠὭὠὡὬὩ;var origin=enemy.transform.position;var p=bridge.WorldToMapUI(origin);var dx=bridge.WorldToMapUI(origin+new Vector3(speed,0,0))-p;var dy=bridge.WorldToMapUI(origin+new Vector3(0,0,speed))-p;return (float)ProjectionBound.Maximum(dx.x,dx.y,dy.x,dy.y);}
 bool BodyRisk(List<Vector2> points,float need){var bound=BodySpeedBound();var speed=Number(player,"ὮὬὤὪὫὤὢὥὠὦὪ");var bonus=Number(player,"ὩὢὯὦὬὦὬὫὫὣὯ")*(1-Number(player,"ὨὩὤὪὥὢὭὧὮὡὩ")*.1f);var remain=Math.Max(0,Number(player,"ὥὫὬὣὦὡὥὫὮὨὥ")-Number(player,"ὬὭὥὯὨὬὪὫὧὣὯ"));foreach(var body in bodies.Values){if(body.Collider==null||!body.Collider.enabled||!body.Collider.gameObject.activeInHierarchy)continue;var center=BodyCenter(body.Collider);var horizon=IsHead(body.Collider)?HeadHorizon():0;var velocity=horizon>0?HeadVelocity():Vector2.zero;var radius=BodyRadius(body.Collider)+Radius(player)+8;var frozen=BodyFrozenRemaining(body.Collider);for(float t=0;t<=need;t+=.05f){var distance=speed*Math.Min(t,remain)+Math.Max(0,t-remain)*(speed-bonus);var pos=AtDistance(points,distance);var moving=Math.Max(0,t-frozen);var known=Math.Min(moving,horizon);var predicted=center+velocity*known;var uncertainty=Math.Max(bound,body.Velocity.magnitude)*Math.Max(0,moving-known);if(Vector2.Distance(pos,predicted)<radius+uncertainty)return true;}}return false;}
 float BorderClearance(HopscotchProjectileBase shot,Vector2 point,float from,float to){var previous=PredictBorder(shot,from)-point;float best=previous.magnitude;for(float t=from+.02f;t<=to+.02f;t+=.02f){var next=PredictBorder(shot,Math.Min(t,to))-point;if(previous.magnitude<50000&&next.magnitude<50000)best=Math.Min(best,(float)BorderEscapeRules.SweptDistance(previous.x,previous.y,next.x,next.y));else best=Math.Min(best,next.magnitude);previous=next;}return best;}
 bool BorderThreat(float horizon){foreach(var shot in manager.ὩὣὭὠὮὭὢὢὡὧὫ.GetComponentsInChildren<HopscotchProjectileBorder>())if(shot.isActiveAndEnabled&&BorderClearance(shot,Position,0,horizon)<14)return true;return false;}
 bool BoundaryBodyThreat {get{return enemy!=null&&Field(enemy,"_carveState").ToString()=="Progress";}}
 float BodyClearance(Vector2 point,float t){if(!BoundaryBodyThreat)return 10000;float result=10000;foreach(var body in bodies.Values){if(body.Collider==null||!body.Collider.enabled||!body.Collider.gameObject.activeInHierarchy)continue;var center=BodyCenter(body.Collider)+body.Velocity*Math.Max(0,t-BodyFrozenRemaining(body.Collider));var radius=BodyRadius(body.Collider)+Radius(player)+12+35*t;result=Math.Min(result,Vector2.Distance(point,center)-radius);}return result;}
 bool BodyPointThreat(Vector2 point,float t){return BodyClearance(point,t)<0;}
 bool BodyTransitRisk(float horizon,bool waiting=false){if(!BoundaryBodyThreat)return false;var points=new List<Vector2>{Position};if(!waiting)for(int i=cursor;i<route.Count&&!route[i].Claim;i++)points.Add(grid.VertexToUIPos(new Vector2Int(route[i].X,route[i].Y)));var speed=Number(player,"ὮὬὤὪὫὤὢὥὠὦὪ");for(float t=.1f;t<=horizon;t+=.1f)if(BodyPointThreat(AtDistance(points,speed*t),t))return true;return false;}
 bool BorderTransitRisk(float horizon){
 var points=new List<Vector2>{Position};for(int i=cursor;i<route.Count&&!route[i].Claim;i++)points.Add(grid.VertexToUIPos(new Vector2Int(route[i].X,route[i].Y)));
 var speed=Number(player,"ὮὬὤὪὫὤὢὥὠὦὪ");
 foreach(var shot in manager.ὩὣὭὠὮὭὢὢὡὧὫ.GetComponentsInChildren<HopscotchProjectileBorder>()){
 if(!shot.isActiveAndEnabled)continue;
 var previous=PredictBorder(shot,0)-AtDistance(points,0);
 for(float t=.02f;t<=horizon+.02f;t+=.02f){float at=Math.Min(t,horizon);var next=PredictBorder(shot,at)-AtDistance(points,speed*at);if(previous.magnitude<50000&&next.magnitude<50000&&BorderEscapeRules.SweptDistance(previous.x,previous.y,next.x,next.y)<14)return true;previous=next;}
 }return false;
 }
 void ApplyEscapeInput(){
 // Arrival runs inside the native movement loop. Replace input before its next edge,
 // otherwise the old direction survives until the next Update and leaves this route.
 var n=route[cursor];var v=Vertex;player.OnTouchPadOK(n.Claim);player.OnTouchDirection(new Vector2(n.X-v.x,n.Y-v.y).normalized,false);
 }
 bool BeginEscape(){
 var shots=manager.ὩὣὭὠὮὭὢὢὡὧὫ.GetComponentsInChildren<HopscotchProjectileBorder>().Where(p=>p.isActiveAndEnabled).ToArray();
 if(shots.Length>0&&BeginDrawEscape())return true;
 var future=shots.Select(p=>Enumerable.Range(0,41).Select(i=>PredictBorder(p,i*.05f)).ToArray()).ToArray();
 var paths=new Queue<List<Vector2Int>>();paths.Enqueue(new List<Vector2Int>{Vertex});List<Vector2Int> best=null;float bestScore=-1;int expanded=0;
 var speed=Math.Max(1,Number(player,"ὮὬὤὪὫὤὢὥὠὦὪ"));int count=Math.Max(4,(int)(speed/grid.ὤὠὡὤὪὠὬὡὥὢὠ));
 while(paths.Count>0&&expanded++<1500){var path=paths.Dequeue();var v=path[path.Count-1];float t=(path.Count-1)*grid.ὤὠὡὤὪὠὬὡὥὢὠ/speed;float separation=10000;foreach(var list in future){for(int j=-1;j<=1;j++)separation=Math.Min(separation,Vector2.Distance(grid.VertexToUIPos(v),list[Mathf.Clamp((int)(t/.05f)+j,0,40)]));}if(separation<8&&path.Count>2)continue;
 if(path.Count>=8){float score=t*1000+Math.Min(200,separation)+2*Math.Min(200,BodyClearance(grid.VertexToUIPos(v),t));if(score>bestScore){best=path;bestScore=score;}}if(path.Count>=count)continue;
 foreach(var d in new[]{Vector2Int.right,Vector2Int.left,Vector2Int.up,Vector2Int.down}){var n=v+d;if(n.x<1||n.y<1||n.x>=grid.Cells.GetLength(0)||n.y>=grid.Cells.GetLength(1)||!grid.IsSafeBorderEdgeHV(v,n)||path.Contains(n))continue;var p=new List<Vector2Int>(path);p.Add(n);paths.Enqueue(p);}}
 if(best==null)return BeginDrawEscape();
 route=best.Skip(1).Select(p=>new Node{X=p.x,Y=p.y,Claim=false}).ToList();cursor=0;escaping=true;hadDrawing=false;running=true;boardId=manager.GetInstanceID();reason="evading-border";nextTransit=DateTime.UtcNow.AddMilliseconds(250);lastProgress=DateTime.UtcNow;ApplyEscapeInput();Event("border-escape",new{Route=route,Score=bestScore});return true;
 }
 bool BeginDrawEscape(){
 var original=route;var oldCursor=cursor;var start=Vertex;bool imminent=BorderThreat(.9f);
 foreach(var points in BorderEscapeRules.DrawingRoutes(new BorderEscapeRules.Vertex(start.x,start.y),grid.Cells.GetLength(0),grid.Cells.GetLength(1),
 (a,b)=>grid.IsDrawableEdgeHV(new Vector2Int(a.X,a.Y),new Vector2Int(b.X,b.Y)),
 (a,b)=>grid.IsSafeBorderEdgeHV(new Vector2Int(a.X,a.Y),new Vector2Int(b.X,b.Y)),
 v=>grid.IsBorderVertex(new Vector2Int(v.X,v.Y)))){
 var nodes=points.Select(p=>new Node{X=p.X,Y=p.Y,Claim=true}).ToList();
 route=nodes;cursor=0;bool safe=SafeDeparture(imminent);route=original;cursor=oldCursor;
 if(!safe)continue;route=nodes;cursor=0;escaping=true;hadDrawing=false;running=true;boardId=manager.GetInstanceID();reason="evading-by-closure";lastProgress=DateTime.UtcNow;ApplyEscapeInput();Event("draw-escape",new{Route=route});return true;
 }return false;
 }
 Vector2 PredictBorder(HopscotchProjectileBase shot,float seconds){
 var phase=Field(shot,"ὡὧὢὡὫὢὣὬὩὯὠ").ToString();var p=Read<Vector2>(shot,"ὮὩὣὬὣὦὤὠὭὭὤ");
 var next=Read<Vector2Int>(shot,"ὠὧὦὭὣὨὦὮὣὦὠ");var direction=Read<Vector2Int>(shot,"ὨὠὢὯὯὮὢὧὯὠὮ");
 if(phase=="ApproachingBorder"){
 var from=Read<Vector3>(shot,"ὣὧὠὯὭὫὯὥὥὥὬ");var target=Read<Vector3>(shot,"ὮὪὪὭὢὭὭὣὭὡὬ");
 var approach=Vector3.Distance(from,target)/Math.Max(1,Number(shot,"ὯὪὤὡὠὩὩὭὩὥὡ"));
 if(seconds<approach)return new Vector2(100000,100000);seconds-=approach;
 }
 var life=Number(shot,"ὧὬὪὭὦὬὤὨὥὠὩ")-Number(shot,"ὩὠὣὮὬὧὬὯὢὡὩ");if(seconds>life)return new Vector2(100000,100000);
 var budget=Number(shot,"ὮὬὤὪὫὤὢὥὠὦὪ")*seconds;
 var choose=shot.GetType().GetMethod("ὭὣὮὡὣὯὮὫὠὧὨ",All);
 for(int i=0;i<1000&&budget>0;i++){var target=grid.VertexToUIPos(next);float length=Vector2.Distance(p,target);if(length>budget)return Vector2.MoveTowards(p,target,budget);budget-=length;p=target;object[] args={next,direction,Vector2Int.zero};if(!(bool)choose.Invoke(shot,args))return p;direction=(Vector2Int)args[1];next=(Vector2Int)args[2];}
 return p;
 }
 static float PathDistance(List<Vector2> points,Vector2 point){float result=Vector2.Distance(points[0],point);for(int i=1;i<points.Count;i++){var d=points[i]-points[i-1];var u=Mathf.Clamp01(Vector2.Dot(point-points[i-1],d)/Math.Max(.001f,d.sqrMagnitude));result=Math.Min(result,Vector2.Distance(point,points[i-1]+d*u));}return result;}
 bool SafeDeparture(bool imminentBorder=false){
 var points=new List<Vector2>{Position};float distance=0;var previous=Position;
 for(int i=cursor;i<route.Count&&route[i].Claim;i++){var p=grid.VertexToUIPos(new Vector2Int(route[i].X,route[i].Y));distance+=Vector2.Distance(previous,p);points.Add(p);previous=p;}
 float need=Travel(distance)+.2f;
 var normal=Number(enemy,"ὥὮὯὥὯὨὭὫὧὬὠ")-Number(enemy,"ὤὧὩὢὪὡὧὪὣὠὮ");var special=Number(enemy,"ὦὤὥὭὪὬὤὨὨὦὨ")-Number(enemy,"ὠὭὫὬὡὤὣὧὠὪὥ");
 var control=ControlRemaining;var carveWindow=CarveBlockedRemaining;var available=AttackOpportunity.Available(EnemyState,normal,special,control,carveWindow);gateReason="attack-window";if(!BorderEscapeRules.TimingAllows(available,need+.25f,imminentBorder))return false;
 if(BodyRisk(points,need)){gateReason="body-forecast";return false;}
 var active=manager.ὩὣὭὠὮὭὢὢὡὧὫ.GetComponentsInChildren<HopscotchProjectileBase>();
 foreach(var shot in active){if(!shot.isActiveAndEnabled)continue;var type=shot.GetType().Name;var p=manager.ὨὢὧὭὡὠὭὠὡὬὩ.WorldToMapUI(shot.transform.position);float life=Number(shot,"ὧὬὪὭὦὬὤὨὥὠὩ")-Number(shot,"ὩὠὣὮὬὧὬὯὢὡὩ");if(life<=0)continue;
 if(type=="HopscotchProjectileBorder"){
 // Border crawlers kill on the safe boundary, but not while the player is drawing.
 // Predict their native right/straight/left/back edge traversal up to the return time.
 if(BorderClearance(shot,points[points.Count-1],Math.Max(0,need-.28f),need+.2f)<14){gateReason="border-return-intersection";return false;}continue;
 }
 if(type!="HopscotchProjectileLongRange"){if(!shot.ὮὥὠὡὨὮὣὩὩὡὥ){var speed=ProjectileSpeed(shot);if(PathDistance(points,p)<speed*Math.Max(0,need-ProjectileFrozenRemaining(shot))+Radius(player)+Radius(shot)){gateReason="tracking-projectile";return false;}}continue;}
 float clearance=Radius(player)+Radius(shot)+grid.ὤὠὡὤὪὠὬὡὥὢὠ*2;var velocity=Read<Vector2>(shot,"ὡὮὤὩὧὦὡὣὥὭὩ")*Number(shot,"ὮὬὤὪὫὤὢὥὠὦὪ");
 for(float t=0;t<Math.Min(need,life);t+=.02f){var q=p+velocity*t;float prefix=0;for(int i=1;i<points.Count;i++){var a=points[i-1];var d=points[i]-a;var length=d.magnitude;var fraction=Mathf.Clamp01(Vector2.Dot(q-a,d)/Math.Max(.01f,d.sqrMagnitude));var nearest=a+d*fraction;if((q-nearest).sqrMagnitude<clearance*clearance&&Travel(prefix+length*fraction)<=t+.1f){gateReason="projectile-intersection";return false;}prefix+=length;}}
 }
 gateReason=(carveWindow>0?"safe-carve:":control>0?"safe-control:":"safe:")+need.ToString("F2")+"s/window:"+available.ToString("F2")+"/control:"+control.ToString("F2");return true;
 }
 static float[] XY(Vector2 v){return new[]{v.x,v.y};}
 static JObject ReadJson(string path){using(var f=new MemoryStream(BD2.LocalIpc.RuntimeFiles.Read(path)??new byte[0]))using(var r=new StreamReader(f))return JObject.Parse(r.ReadToEnd());}
 void ReadControl(){bool valid=false;try{var c=ReadJson(Path.Combine(Root,"control.json"));valid=(bool?)c["Enabled"]==true&&(string)c["Session"]==session&&(int?)c["Pid"]==System.Diagnostics.Process.GetCurrentProcess().Id&&(long?)c["ProcessStart"]==processStart&&DateTime.Parse((string)c["ExpiresUtc"]).ToUniversalTime()>DateTime.UtcNow;if(valid){owner=(string)c["Owner"];deadline=DateTime.Parse((string)c["ExpiresUtc"]).ToUniversalTime();}}catch(IOException){if(DateTime.UtcNow<deadline&&controlled)return;}catch{}if(controlled&&!valid){Stop("lease/stop",true);}controlled=valid;}
 void ReadCommand(){var bytes=BD2.LocalIpc.RuntimeFiles.Take(Path.Combine(Root,"command.json"));if(bytes==null)return;var q=JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));
 var id=(string)q["Id"];
 try{
 if((string)q["Session"]!=session||DateTime.Parse((string)q["ExpiresUtc"]).ToUniversalTime()<DateTime.UtcNow)throw new Exception("stale command");
 var kind=(string)q["Kind"];if(kind!="stop"&&(!controlled||(string)q["Owner"]!=owner))throw new Exception("inactive control lease");
 if(kind=="stop")Stop("requested",true);
 else if(kind=="click"){
 Snapshot();Component ui;if(!uiTargets.TryGetValue((int)q["Ui"],out ui))throw new Exception("UI changed");var go=GO(Fields(ui.GetType()).Single(f=>f.Name==(string)q["Field"]).GetValue(ui));if(go==null||!go.activeInHierarchy)throw new Exception("inactive button");var method=ui.GetType().GetMethod("OnClickUI",All)??ui.GetType().GetMethod("OnClick",All);if(method==null)throw new Exception("No native click");method.Invoke(ui,new object[]{go});
 }
 else if(kind=="exit"){Stop("leaving-round",false);manager.RequestGameExit();}
 else if(kind=="retry"){manager.RequestGameRetry();}
 else if(kind=="start"){
 if(Playing)throw new Exception("already playing");stage=((int?)q["StageIndex"]??0)+1;if(stage<1||stage>5)throw new Exception("unsupported stage");round=id;var ui=UnityEngine.Object.FindObjectsOfType<HopscotchMainUI>().Single(x=>x.gameObject.activeInHierarchy);ui.OnClickPlay(true,(int?)q["StageIndex"]??0);
 }
 else if(kind=="route"){
 if(escaping&&running){Write("receipt-"+id+".json",new{AtUtc=DateTime.UtcNow.ToString("O"),Status="deferred",Id=id});return;}
 if(player==null||manager.GetInstanceID()!=(int)q["BoardId"])throw new Exception("board changed");
 var state=Field(manager,"ὢὠὤὪὧὨὫὦὯὨὯ").ToString();if(state!="Playing")throw new Exception("not ready for route: "+state);
 route=q["Route"].ToObject<List<Node>>();if(route.Count<1||route.Count>5000)throw new Exception("invalid route");
 var prev=Vertex;foreach(var n in route){if(n.X<1||n.Y<1||n.X>grid.Cells.GetLength(0)-1||n.Y>grid.Cells.GetLength(1)-1)throw new Exception("outside grid");if(n.X!=prev.x&&n.Y!=prev.y)throw new Exception("non-cardinal segment");prev=new Vector2Int(n.X,n.Y);}
 gateSince=default(DateTime);escaping=false;hadDrawing=false;cursor=0;boardId=manager.GetInstanceID();guard=(bool?)q["Guard"]??true;running=true;reason="executing";lastProgress=DateTime.UtcNow;lastVertex=Vertex;startedTime=Time.time;
 Event("route",q);Drive(false);
 }
 else throw new Exception("unknown command");
 Write("receipt-"+id+".json",new{AtUtc=DateTime.UtcNow.ToString("O"),Status="dispatched",Id=id});
 }catch(Exception e){Write("receipt-"+id+".json",new{AtUtc=DateTime.UtcNow.ToString("O"),Status="failed",Error=e.GetBaseException().Message,Id=id});}
 }
}
}
