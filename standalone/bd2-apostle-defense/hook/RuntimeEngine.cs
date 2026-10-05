using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using Proto.Net;
using Proto.Design.common;
using B=BD2ApostleDefense.Runtime.Bindings;
namespace BD2ApostleDefense.Runtime
{
 internal sealed class RuntimeEngine
 {
  private sealed class UiNotReadyException:Exception {internal UiNotReadyException(string message):base(message){}}
  private static RuntimeEngine current;
  private readonly SettlementTracker settlement=new SettlementTracker();
  private readonly Harmony patch=new Harmony("bd2.apostle-defense.inputs");
  private Timer timer; private int ioBusy; private bool stopped;
  private readonly int pid=System.Diagnostics.Process.GetCurrentProcess().Id;
  private readonly long processStart=System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
  private volatile Control control=new Control(); private volatile Snapshot latest=new Snapshot();
  private UIBase[] surfaces=new UIBase[0]; private long lastTick,lastScan,seq,handled,ack,pendingAt;
  private string owner="",ackResult="",ackMessage="",account="",lastStage="",fault="";
  private Catalog catalog; private object manager,field; private DefenseHUD hud;
  private Control pending; private Snapshot before,observed; private int actionPhase;
  private volatile ActionEvidence evidence;private ActionEvidence savedEvidence;
  private readonly ConcurrentQueue<FlowEvidence> flow=new ConcurrentQueue<FlowEvidence>();private string flowKey="";
  private bool achievementsKnown,achievementRequest; private int clearProgress,rareProgress,clearGroup,rareGroup,clearTarget,rareTarget;
  internal void Start()
  {
   if(timer!=null)return; B.ValidateCompiledClient();B.Validate();current=this;
   try{NetworkGuard.Install();var pump=typeof(GameCameraManager).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance);if(pump==null)throw new MissingMethodException("GameCameraManager.LateUpdate");patch.Patch(pump,postfix:new HarmonyMethod(typeof(RuntimeEngine),nameof(Frame)));timer=new Timer(_=>IO(),null,0,100);Loader.Status("active","");}catch{Stop();throw;}
  }
  internal void StartProbe(){B.ValidateCompiledClient();Storage.Write("probe.json",new Snapshot{State="compatible",Message=B.Validate()+" interfaces",At=DateTime.UtcNow.Ticks});}
  internal void PrepareHandoff(){control=new Control();CancelSelection();}
  internal string HandoffBusy()=>ioBusy!=0?"snapshot writer":latest.Exiting?"native exit awaiting reply":"";
  internal void Stop(){CancelSelection();stopped=true;NetworkGuard.Remove();timer?.Dispose();timer=null;patch.Unpatch(typeof(GameCameraManager).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance),HarmonyPatchType.All,patch.Id);current=null;}
  private static void Frame(){current?.Tick();}
  private void IO()
  {
   if(Interlocked.Exchange(ref ioBusy,1)!=0)return;
   try
   {
    if(stopped)return;
    // A failed evidence/log write must not suppress commands, snapshots or the heartbeat.
    IoDiagnostics.Attempt(Identity.Root,"control-read","control.json",()=>{var c=Storage.Read<Control>("control.json");if(c!=null)control=c;});
    var snapshot=latest;
    IoDiagnostics.Attempt(Identity.Root,"snapshot-write","snapshot.json",()=>Storage.Write("snapshot.json",snapshot));
    var e=evidence;if(e!=null&&e!=savedEvidence&&IoDiagnostics.Attempt(Identity.Root,"action-write","last-action.json",()=>Storage.Write("last-action.json",e)))savedEvidence=e;
    IoDiagnostics.Attempt(Identity.Root,"flow-append","flow-current.jsonl",()=>{FlowEvidence entry;while(flow.TryPeek(out entry)){Storage.AppendFlow(entry);flow.TryDequeue(out entry);}});
    IoDiagnostics.Attempt(Identity.Root,"network-append","network-current.jsonl",()=>NetworkGuard.Flush());
    IoDiagnostics.Attempt(Identity.Root,"heartbeat-write","runtime.json",()=>Loader.Status("active",""));
   }
   finally{Interlocked.Exchange(ref ioBusy,0);}
  }
  private static int N(string role,object o)=>Convert.ToInt32(B.Read(role,o)??0);
  private static bool Flag(string role,object o)=>B.Read(role,o) is bool value&&value;
  private static IEnumerable<object> Items(object o)=>o is IEnumerable values?values.Cast<object>():Enumerable.Empty<object>();
  private static IEnumerable<object> Values(object o)=>o is IDictionary d?d.Values.Cast<object>():Enumerable.Empty<object>();
  private static Point Point(Component c,int id){var p=c.transform.position;return new Point{Id=id,X=p.x,Y=p.y,Z=p.z};}
  private T Surface<T>() where T:UIBase=>surfaces.OfType<T>().FirstOrDefault(B.Active);
  private void Tick()
  {
   long now=DateTime.UtcNow.Ticks;if(stopped||now-lastTick<TimeSpan.FromMilliseconds(100).Ticks)return;long elapsed=lastTick==0?0:now-lastTick;lastTick=now;
   var s=new Snapshot{ProcessId=pid,ProcessStart=processStart,At=now,Sequence=++seq};
   try
   {
    if(now-lastScan>TimeSpan.FromMilliseconds(400).Ticks){surfaces=UnityEngine.Object.FindObjectsOfType<UIBase>().Where(B.Active).ToArray();lastScan=now;}
    Read(s);settlement.Observe(s);NetworkGuard.Pump(s);NetworkGuard.Apply(s);observed=s;
    if(s.Account!=account){account=s.Account;achievementsKnown=false;achievementRequest=false;clearProgress=rareProgress=0;catalog=null;fault="";}
    // A complete native result must be settled before refreshing the server-backed counters.
    if(s.Stage=="lobby"&&lastStage!="lobby")achievementsKnown=false;
    lastStage=s.Stage;s.AchievementsKnown=achievementsKnown;s.ClearProgress=clearProgress;s.RareProgress=rareProgress;s.ClearTarget=clearTarget;s.RareTarget=rareTarget;
    var c=control;
    if(c==null||!c.Enabled||c.Expires<now||c.ProcessId!=pid||c.ProcessStart!=processStart){if(pending!=null)Finish(pending,"rejected","操作租约中断，恢复后按实际盘面重新决策");s.State="idle";s.Message="自动操作已暂停";Publish(s);return;}
    if(c.Owner!=owner){CancelSelection();pending=null;owner=c.Owner;handled=ack=0;fault="";ackMessage=ackResult="";}
    s.Owner=owner;
    if(fault.Length>0){s.State="error";s.Message=fault;Publish(s);return;}
    // Cancellation is an acknowledged barrier, including during a real network outage.
    if(c.Command>ack&&c.Command<=c.CancelThrough)
    {handled=Math.Max(handled,c.CancelThrough);Finish(pending??c,"retry","旧操作已取消，按当前界面重新决策");s.State="ready";Publish(s);return;}
    // Scene evidence can complete an old action even before battle networking recovers.
    if(pending!=null&&(GameFlow.Superseded(pending.Action.Kind,before,s)||GameFlow.Receipt(pending.Action.Kind,before,s)))
    {Continue(s,now);Publish(s);return;}
    if(GameFlow.NetworkBlocked(s)){if(pending!=null)pendingAt+=elapsed;s.State="network-wait";s.Message=s.NetworkMessage;Publish(s);return;}
    if(pending!=null){Continue(s,now);Publish(s);return;}
    if(c.Command>handled&&c.Action.Kind!="wait")
    {
     handled=c.Command;string reject=Guards.Reject(c,s,now);
     if(reject.Length>0){Finish(c,"rejected",reject);}
     else{pending=c;before=s;pendingAt=now;actionPhase=0;Execute(s);}
    }
    s.State=pending!=null?"pending":"ready";s.Message=pending!=null?"等待原生操作回读："+pending.Action.Reason:"盘面已同步";
    if(pending==null&&s.Blocker.Length>0){s.State="waiting-popup";s.Message="等待关闭游戏弹窗："+s.Blocker;}
   }
   catch(UiNotReadyException e){if(pending!=null)Finish(pending,"retry",e.Message);s.State="ready";s.Message=e.Message;}
   catch(Exception e){fault=e.GetBaseException().Message;s.State="error";s.Message=fault;CancelSelection();if(pending!=null)Finish(pending,"error",fault);}
   Publish(s);
  }
  private void Publish(Snapshot s)
  {
   s.AcceptedCommand=pending==null?0:pending.Command;s.Owner=owner;s.Ack=ack;s.AckResult=ackResult;s.AckMessage=ackMessage;latest=s;
   var key=s.Stage+"|"+s.State+"|"+s.Blocker+"|"+s.Win+"|"+s.Dead+"|"+s.RoomEnded+"|"+s.Exiting+"|"+(pending==null?0:pending.Command)+"|"+ack+"|"+s.NetworkHold+"|"+s.SettlementPending+"|"+s.SettlementRoomEnded;
   if(key!=flowKey){flowKey=key;flow.Enqueue(new FlowEvidence{At=s.At,ProcessId=pid,Runtime=Identity.Runtime,Stage=s.Stage,State=s.State,Blocker=s.Blocker,Win=s.Win,Dead=s.Dead,RoomEnded=s.RoomEnded,Exiting=s.Exiting,Wave=s.Wave,Command=pending==null?0:pending.Command,Action=pending==null?"":pending.Action.Kind,Ack=ack,AcceptedCommand=s.AcceptedCommand,NetworkHold=s.NetworkHold,SettlementPending=s.SettlementPending,SettlementRoomEnded=s.SettlementRoomEnded,AckResult=ackResult,Message=s.Message});FlowEvidence drop;while(flow.Count>256)flow.TryDequeue(out drop);}
  }
  private void CancelSelection(){if(pending!=null&&(pending.Action.Kind=="sell"||pending.Action.Kind=="move")&&manager!=null){try{B.InvokeOn("Manager.ClearSelection",manager);}catch{}}}
  private void Finish(Control c,string result,string message){CancelSelection();ack=c.Command;ackResult=result;ackMessage=message;evidence=new ActionEvidence{At=DateTime.UtcNow.Ticks,Result=result,Message=message,Command=c,Before=pending==c?before:observed,After=observed};pending=null;actionPhase=0;}
  private void Read(Snapshot s)
  {
   var user=B.Read("Account.User",null) as UserDBInfo;if(user==null||user.OwnerIndex<=0){s.Stage="login";return;}
   s.Account=Identity.Hash(user.OwnerIndex.ToString());s.Name=user.UserId;
   manager=B.Read("Manager.Instance",null);field=B.Read("Field.Instance",null);hud=Surface<DefenseHUD>();
   if(Surface<DefenseGiveUpConfirmPopupUI>()!=null)s.Stage="confirm-exit";
   else if(Surface<DefenseResultPopupUI>()!=null)s.Stage="result";
   else if(Surface<DefenseMatchingFailPopupUI>()!=null)s.Stage="match-failed";
   else if(Surface<DefenseQuickMatchingPopupUI>()!=null){s.Stage="matching";var matching=Surface<DefenseQuickMatchingPopupUI>();s.CanStartMatch=Flag("Matching.Ready",matching)&&!Flag("Matching.Leaving",matching)&&B.Active(B.Read("Matching.StartButton",matching) as GameObject);}
   else if(hud!=null&&manager!=null)s.Stage=Flag("Manager.Playing",manager)?"playing":"waiting-round";
   else if(Surface<DefenseMainUI>()!=null)s.Stage="lobby";
   var embeddedNotice=hud==null?null:B.Read("Hud.Notice",hud);
   s.Surfaces=surfaces.Where(v=>v!=null).Select(v=>new SurfaceInfo{
    Type=v.GetType().Name,RegisteredName=Convert.ToString(B.Read("Ui.RegisteredName",v))??"",
    ObjectName=v.gameObject.name,Parent=v.transform.parent==null?"":v.transform.parent.name,
    Active=B.Active(v),Popup=Flag("Ui.Popup",v),GameHud=(bool)B.Invoke("Ui.IsHud",v),
    EmbeddedDefenseNotice=v is NoticeUI&&ReferenceEquals(v,embeddedNotice),
    NativeFlow=v is DefenseQuickMatchingPopupUI||v is DefenseResultPopupUI||v is DefenseMatchingFailPopupUI||v is DefenseGiveUpConfirmPopupUI
   }).ToArray();
   var blocker=s.Surfaces.FirstOrDefault(PopupPolicy.IsBlocking);
   s.Blocker=blocker==null?"":blocker.Type;
   var eventPopup=Surface<EventPopupUI>();s.EventPopupOpen=eventPopup!=null;
   // Only the native dismissible event overlay over the defense lobby is automated.
   // Purchases, network errors and live-match menus are never closed by this path.
   s.CanDismissEvent=s.Stage=="lobby"&&eventPopup!=null&&s.Surfaces.Where(PopupPolicy.IsBlocking).All(v=>v.Type==typeof(EventPopupUI).Name)
    &&(bool)B.InvokeOn("Ui.CanClose",eventPopup);
   if(s.Stage=="waiting"||s.Stage=="login")return;
   if(catalog==null)catalog=ReadCatalog();s.Catalog=catalog;
   if(manager==null)return;
   s.Gold=N("Manager.Currency",manager);s.Wave=N("Manager.Wave",manager);s.EnemyCount=N("Manager.Count",manager);
   s.Win=Flag("Manager.Win",manager);s.Dead=Flag("Manager.Dead",manager);s.RoomEnded=B.Read("Manager.RoomState",manager).ToString()=="ErsEnd";s.Exiting=Flag("Manager.Exit",manager);
   s.MyView=N("Manager.View",manager)==0;
   if(hud!=null&&GameFlow.RoundFinished(s)&&(s.Stage=="waiting-round"||s.Stage=="playing"))s.Stage="ended";
   if(s.Exiting&&s.Stage!="lobby")s.Stage="settling";
   var network=B.Read("Services.Network",null);if(network!=null){s.Room=Convert.ToString(B.Read("Network.Room",network));s.RoundRare=N("Network.Rare",network);}
   // Connection policy is applied after terminal evidence has been retained in Tick.
   // Native death clears my objects and can switch to another player's view. Settlement must not
   // depend on that board, its camera, upgrade controls, or enemies still being available.
   if(hud==null||field==null||s.Stage!="playing"||GameFlow.RoundFinished(s)||s.Exiting)return;
   var levels=B.Read("Manager.Levels",manager) as IDictionary;
   if(levels!=null)foreach(DictionaryEntry x in levels){int k=Convert.ToInt32(x.Key);if(k>=0&&k<5)s.Levels[k]=Convert.ToInt32(x.Value);}
   var raw=(RawDataManager)B.Singleton(typeof(RawDataManager));
   foreach(var button in Items(B.Get(hud,"_elementButtons")))
   {
    int element=(int)B.Num(button,"_elementType");if(element<0||element>=5)throw new InvalidOperationException("未知元素编号");
    s.CanUpgrade[element]=B.Active(B.Get(button,"_goUpgradeEnable") as GameObject)&&!B.Active(B.Get(button,"_goUpgradeMax") as GameObject);
    var row=raw.GetMGDUpgradeTable(Math.Min(catalog.MaxUpgrade,s.Levels[element]+1));
    if(row!=null)s.UpgradeCosts[element]=new[]{row.WaterupgradeCost,row.FireupgradeCost,row.WindupgradeCost,row.LightupgradeCost,row.DarkupgradeCost}[element];
   }
   var camera=B.Read("Manager.Camera",manager) as Camera;
   s.Boards=Items(B.Read("Field.Boards",field)).Cast<Component>().Select(b=>{var p=b.transform.position;var screen=camera==null?Vector3.zero:camera.WorldToScreenPoint(p);return new Board{Id=N("Field.Id",b),Element=N("Board.Element",b),X=p.x,Y=p.y,Z=p.z,ScreenX=screen.x,ScreenY=screen.y};}).ToArray();
   s.Path=Items(B.Read("Field.Path",field)).Cast<Component>().Select(b=>Point(b,N("Field.Id",b))).ToArray();
   s.Units=Values(B.Read("Manager.Units",manager)).Select(u=>{var target=B.Read("Unit.Target",u) as Component;return new Unit{Index=N("Field.Id",u),Id=N("Unit.Table",u),Grid=N("Unit.Grid",u),Ready=Flag("Unit.Ready",u),TargetEnemy=target==null?-1:N("Field.Id",target),AttackElapsed=Convert.ToDouble(B.Read("Unit.AttackElapsed",u)),AttackState=N("Unit.AttackState",u)-1};}).ToArray();
   s.Enemies=Values(B.Read("Manager.Enemies",manager)).Cast<Component>().Where(e=>!Flag("Enemy.Dead",e)).Select(e=>{var pos=e.transform.position;int id=N("Enemy.Table",e),next=N("Enemy.NextPoint",e);var row=raw.GetMGDEnemyTable(id);return new Enemy{Id=N("Field.Id",e),Table=id,X=pos.x,Y=pos.y,Z=pos.z,Element=row.Element,Speed=row.MoveSpeed,NextPoint=next>=0&&next<s.Path.Length?s.Path[next].Id:-1,Hp=Convert.ToDouble(B.Read("Enemy.Hp",e))};}).ToArray();
   var clock=B.Read("Services.Clock",null);var gameNow=(DateTime)B.Call(clock,"Now");s.SecondsLeft=Math.Max(0,((DateTime)B.Read("Manager.Deadline",manager)-gameNow).TotalSeconds);
   s.CanSummon=B.Active(B.Get(hud,"_goEnableRecall") as GameObject);
   s.Ready=s.MyView&&s.Boards.Length>0&&s.Path.Length>1&&s.Catalog.Units.Length>0&&s.Units.All(u=>u.Ready&&s.Catalog.Units.Any(d=>d.Id==u.Id))&&s.Units.Select(u=>u.Grid).Distinct().Count()==s.Units.Length;
   s.BoardKey=Guards.BoardKey(s);
  }
  private Catalog ReadCatalog()
  {
   var raw=(RawDataManager)B.Singleton(typeof(RawDataManager));var d=raw.GetMGDDefaultTable();
   var c=new Catalog{SummonCost=d.SummonCost,GameOverCount=d.GameOverValue,Advantage=d.ElementAdvantage,Penalty=d.ElementPenalty,MaxUpgrade=raw.GetLastMGDUpgradeTable().Id};
   c.Units=raw.GetMGDCharTableListByGroupId(1).Select(x=>new UnitDef{Id=x.Id,Name=Convert.ToString(B.Invoke("Text.Local",x.CharNameTextId)),Element=x.Element,Grade=x.Grade,Weight=x.SummonRatio,Attack=x.AttackValue,UpAttack=x.UpAttackValue,Interval=x.AttackSpeed,Range=x.AttackRange,SplashRange=x.Splash>0?x.SplashRange:0,Sell=x.SellCost}).ToArray();
   c.MaxGrade=raw.GetAllMGDCharTableList().Max(x=>x.Grade);
   var waves=new List<WaveDef>();for(int i=1;i<=raw.GetLastMGDWaveTable().Id;i++){var w=raw.GetMGDWaveTable(1,i);if(w==null)throw new InvalidOperationException("波次表不完整");var e=raw.GetMGDEnemyTable(w.SpawnMonsterId);waves.Add(new WaveDef{Id=i,EnemyTable=w.SpawnMonsterId,Element=e.Element,Hp=e.Health,Count=w.SpawnValue,Gold=e.DropGold,Speed=e.MoveSpeed,Duration=w.WaitingTime/1000.0,Boss=w.WaveType!=0,SpawnInterval=w.SpawnTime/1000.0});}c.Waves=waves.ToArray();
   var clear=raw.GetAchievementTablesByConditionType(123).Single(x=>x.ContentsGroup==1&&x.ConditionSubType==50);
   var rare=raw.GetAchievementTablesByConditionType(151).Single(x=>x.ContentsGroup==1);
   clearGroup=clear.GroupId;rareGroup=rare.GroupId;clearTarget=Convert.ToInt32(clear.ConditionValue);rareTarget=Convert.ToInt32(rare.ConditionValue);
   if(c.Units.Any(x=>x.Interval<=0||x.Element<0||x.Element>4)||c.SummonCost<=0||clearTarget<=0||rareTarget<=0)throw new InvalidOperationException("小游戏规则校验失败");
   c.Key=Identity.Hash(string.Join(";",c.Units.Select(x=>x.Id+":"+x.Grade+":"+x.Attack+":"+x.Weight)));return c;
  }
  private void Click(UIBase ui,string fieldName)
  {if(ui==null||!B.Active(ui))throw new UiNotReadyException("原生界面已变化");var go=B.Get(ui,fieldName) as GameObject;if(!B.Active(go))throw new UiNotReadyException("原生按钮未就绪："+fieldName);ui.OnClickUI(go);}
  private void Pointer(int grid,bool down)
  {var board=Items(B.Read("Field.Boards",field)).Cast<Component>().Single(b=>N("Field.Id",b)==grid);var camera=B.Read("Manager.Camera",manager) as Camera;if(camera==null)throw new InvalidOperationException("缺少盘面相机");var p=camera.WorldToScreenPoint(board.transform.position);if(p.z<=0)throw new InvalidOperationException("盘面不在相机视野内");B.InvokeOn("Manager.Pointer",manager,down,p);}
  private void Execute(Snapshot s)
  {
   var a=pending.Action;
   switch(a.Kind)
   {
    case "close-event":
     if(!s.CanDismissEvent||s.Stage!="lobby"||s.Exiting)throw new UiNotReadyException("等待可关闭的大厅活动弹窗");
     B.InvokeOn("Ui.Back",Surface<EventPopupUI>());break;
    case "refresh":
     if(s.Stage!="lobby")throw new UiNotReadyException("等待大厅读取成就");
     if(achievementRequest)break; // Reattach to an outstanding read; never duplicate it.
     achievementRequest=true;achievementsKnown=false;string requestedAccount=s.Account;
     B.Invoke("Achievement.Refresh",false,(Action)(()=>{achievementRequest=false;if(account!=requestedAccount)return;var c=B.Invoke("Achievement.Read",clearGroup,1) as AchievementDBInfo;var r=B.Invoke("Achievement.Read",rareGroup,1) as AchievementDBInfo;clearProgress=c==null?0:(int)Math.Max(c.Value,c.MaxClearId>=1001?clearTarget:0);rareProgress=r==null?0:(int)Math.Max(r.Value,r.MaxClearId>=1001?rareTarget:0);achievementsKnown=true;}));break;
    case "start":if(s.Stage!="lobby"||!s.AchievementsKnown)throw new InvalidOperationException("尚未核对成就");Click(Surface<DefenseMainUI>(),"_goQuickStartButton");break;
    case "retry":if(s.Stage!="match-failed")throw new InvalidOperationException("匹配状态已变化");Click(Surface<DefenseMatchingFailPopupUI>(),"_goOKButton");break;
    case "match-start":if(s.Stage!="matching"||!s.CanStartMatch)throw new InvalidOperationException("等待原生匹配开始按钮");Click(Surface<DefenseQuickMatchingPopupUI>(),"_goGameStartButton");break;
    case "open-result":if(s.Stage!="ended"||!GameFlow.CanSettle(s))throw new InvalidOperationException("对局尚未结束");Click(hud,"_goExitButton");break;
    case "settle":if(s.Stage!="result"||!GameFlow.CanSettle(s))throw new InvalidOperationException("请先完成当前对局");Click(Surface<DefenseResultPopupUI>(),"_goExitButton");break;
    case "confirm-exit":if(s.Stage!="confirm-exit"||!GameFlow.CanSettle(s))throw new InvalidOperationException("不自动放弃进行中的对局");Click(Surface<DefenseGiveUpConfirmPopupUI>(),"_goOkButton");break;
    case "summon":Click(hud,"_goRecallButton");break;
    case "upgrade":
     var button=Items(B.Get(hud,"_elementButtons")).Single(b=>(int)B.Num(b,"_elementType")==a.Element);
     if(!B.Active(B.Get(button,"_goUpgradeEnable") as GameObject))throw new InvalidOperationException("升级按钮不可用");hud.OnClickUI((GameObject)B.Get(button,"_goUpgradeButton"));break;
    case "sell":case "move":
     B.InvokeOn("Manager.ClearSelection",manager);Pointer(a.Grid,true);
     var selected=B.Read("Manager.Selected",manager);
     if(selected==null||N("Field.Id",selected)!=a.UnitIndex||N("Unit.Table",selected)!=a.UnitId||N("Unit.Grid",selected)!=a.Grid)
     {Finish(pending,"retry","原生点击暂未选中目标，重新读取盘面后继续");break;}
     Pointer(a.Kind=="move"?a.TargetGrid:a.Grid,false);actionPhase=1;break;
    default:throw new InvalidOperationException("未知操作："+a.Kind);
   }
  }
  private void Continue(Snapshot s,long now)
  {
   var c=control;if(!c.Enabled||c.Owner!=pending.Owner||c.Account!=s.Account||c.Expires<now){Finish(pending,"rejected","控制上下文已变化，按当前界面重新决策");return;}
   var a=pending.Action;bool done=false;
   if(GameFlow.Superseded(a.Kind,before,s)){Finish(pending,"superseded","对局或匹配状态已变化，按当前界面继续");s.State="ready";s.Message=ackMessage;return;}
   if(a.Kind=="sell"&&actionPhase==1)
   {
    string reject=Guards.Reject(pending,s,now);if(reject.Length>0){Finish(pending,"rejected",reject);return;}
    var selected=B.Read("Manager.Selected",manager);
    if(selected==null||N("Field.Id",selected)!=a.UnitIndex||N("Unit.Table",selected)!=a.UnitId||N("Unit.Grid",selected)!=a.Grid){Finish(pending,"rejected","选中目标未确认，保留使徒");return;}
    Click(hud,"_goSellButton");actionPhase=2;return;
   }
   switch(a.Kind)
   {
    case "summon":done=s.Room==before.Room&&s.Units.Any(u=>!before.Units.Any(v=>v.Index==u.Index)&&u.Ready);break;
    case "sell":done=s.Room==before.Room&&!s.Units.Any(u=>u.Index==a.UnitIndex);break;
    case "move":done=s.Room==before.Room&&s.Units.Any(u=>u.Index==a.UnitIndex&&u.Grid==a.TargetGrid)&&(a.TargetIndex<0||s.Units.Any(u=>u.Index==a.TargetIndex&&u.Grid==a.Grid));break;
    case "upgrade":done=s.Room==before.Room&&s.Levels[a.Element]==before.Levels[a.Element]+1;break;
    case "refresh":done=achievementsKnown;break;
    default:done=GameFlow.Receipt(a.Kind,before,s);break;
   }
   if(done){Finish(pending,"ok",a.Reason);s.State="ready";s.Message="已回读："+a.Reason;return;}
   s.State="pending";s.Message="等待回读："+a.Reason;
   int timeout=GameFlow.TimeoutSeconds(a.Kind);
   if(now-pendingAt>TimeSpan.FromSeconds(timeout).Ticks)
   {
    string message="原生操作"+timeout+"秒未确认："+a.Kind+"，阶段="+s.Stage+"，弹窗="+s.Blocker+"；自动化保持开启，按实际状态恢复。";
    Finish(pending,"timeout",message);s.State="ready";s.Message=message;
   }
  }
 }
}
