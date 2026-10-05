namespace BD2ApostleDefense;
public sealed class Controller
{
 private readonly object sync=new(),sendSync=new();private long revision,stopVersion;private readonly IClientPort port;private readonly Planner planner=new();private Control? active;private Settings settings=new();private long command,nextAction,watchAt,lastPoll;
 private bool refillPending;private string refillRoom="";
 private readonly RecoveryBackoff recovery=new();
 public bool Running {get{lock(sync)return active!=null;}}
 public Decision? PendingDecision {get{lock(sync)return active?.Command>0?JsonFiles.Clone(active.Action):null;}}
 public string Message {get;private set;}="请连接游戏后进入使徒运气防守大厅";
 public string Focus=>planner.Focus;
 public event Action<string>? Diagnostic;
 public Controller(IClientPort port){this.port=port;}
 public long StopVersion {get{lock(sync)return stopVersion;}}
 public void Start(Snapshot s,Settings p,long now,long? expectedStopVersion=null)
 {
  var game=port.Find();lock(sync){if(expectedStopVersion.HasValue&&expectedStopVersion!=stopVersion)throw new OperationCanceledException();StartLocked(s,p,now,game);revision++;}
  try{Publish();}catch{RequestStop();throw;}
 }
 private void StartLocked(Snapshot s,Settings p,long now,GameProcess? game)
 {
  if(game==null||!Guards.Fresh(s,game.Id,game.Start,now)||s.Account.Length!=64)throw new InvalidOperationException("请先连接游戏并等待账号和盘面同步");
  if(p.Validate()!="")throw new ArgumentException(p.Validate());
  planner.ResetTactics();recovery.Reset();settings=JsonFiles.Clone(p);active=new(){Enabled=true,Owner=Guid.NewGuid().ToString("N"),Account=s.Account,ProcessId=game.Id,ProcessStart=game.Start,Expires=now+TimeSpan.FromSeconds(10).Ticks};command=0;nextAction=0;watchAt=lastPoll=now;refillPending=false;refillRoom="";Message="已开启，准备读取游戏状态";
 }
 public void Update(Settings p){lock(sync){if(p.Validate()!="")throw new ArgumentException(p.Validate());settings=JsonFiles.Clone(p);}}
 // The state lock never spans IPC. Stopping stays immediate even during a blocked write.
 public void RequestStop(string reason="已暂停；游戏仍会继续运行"){lock(sync)StopLocked(reason);}
 private void StopLocked(string reason){active=null;refillPending=false;stopVersion++;revision++;Message=reason;Diagnostic?.Invoke(reason);}
 public void Stop(string reason="已暂停；游戏仍会继续运行"){RequestStop(reason);Publish();}
 public void Flush()=>Publish();
 private void Publish()
 {
  lock(sendSync)while(true)
  {
   Control value;long sent;lock(sync){sent=revision;value=active==null?new Control():JsonFiles.Clone(active);}
   try{port.Write(value);}catch(BD2.LocalIpc.LeaseRevokedException){RequestStop("组件控制权已转移，请重新连接");throw;}
   lock(sync)if(sent==revision)return;
   // A concurrent stop/update must be the last command, never the older enabled renewal.
  }
 }
 public void Poll(Snapshot? s,long now)
 {
  if(!Running)return;var game=port.Find();
  lock(sync){if(active==null)return;PollLocked(s,now,game);revision++;}
  Publish();
 }
 private void PollLocked(Snapshot? s,long now,GameProcess? game)
 {
  if(active==null)return;
  if(game==null||game.Id!=active.ProcessId||game.Start!=active.ProcessStart){StopLocked("游戏进程已退出或更换，请重新连接");return;}
  // Keep the GUI enabled through transient loading / file contention. Do not renew a stale action.
  if(s==null||!Guards.Fresh(s,game.Id,game.Start,now)){Message="等待组件心跳；暂停发出操作，恢复后继续";return;}
  if(s.Account.Length==64&&s.Account!=active.Account){StopLocked("账号已切换，请核对目标后重新开启");return;}
  if(s.Account.Length==0){Message="等待账号加载";return;}
  long elapsed=Math.Max(0,now-lastPoll);lastPoll=now;
  active.Expires=now+TimeSpan.FromSeconds(10).Ticks;
  recovery.Sync(s);
  if(s.Owner==active.Owner&&s.State=="error"){StopLocked("组件已暂停："+s.Message);return;}
  if(active.Command>0)
  {
   if(s.Owner!=active.Owner||s.Ack<active.Command)
   {
    bool accepted=s.Owner==active.Owner&&s.AcceptedCommand==active.Command;
    if(accepted&&GameFlow.NetworkBlocked(s))watchAt+=elapsed;
    bool unacceptedExpired=!accepted&&s.At>active.SnapshotAt+TimeSpan.FromSeconds(4).Ticks;
    bool acceptedExpired=accepted&&!GameFlow.NetworkBlocked(s)&&now-watchAt>TimeSpan.FromSeconds(GameFlow.TimeoutSeconds(active.Action.Kind)+5).Ticks;
    if(active.CancelThrough<active.Command&&(unacceptedExpired||acceptedExpired))
    {
     active.CancelThrough=active.Command;
     Diagnostic?.Invoke($"#{active.Command} cancel-request stage={s.Stage} accepted={s.AcceptedCommand} ack={s.Ack} ageMs={(now-active.SnapshotAt)/TimeSpan.TicksPerMillisecond}");
    }
    Message=active.CancelThrough>=active.Command?"等待组件取消旧操作，恢复后重新决策":GameFlow.NetworkBlocked(s)?s.NetworkMessage:"等待操作确认："+active.Action.Reason;
    return;
   }
   Diagnostic?.Invoke($"#{active.Command} {s.AckResult} {s.AckMessage}");
   if(s.AckResult=="error"){StopLocked(s.AckMessage);return;}
   if(s.AckResult is "timeout" or "retry")
   {recovery.Record(active.Action,false,now);Diagnostic?.Invoke("单次操作未完成，保留自动化并按当前界面恢复："+s.AckMessage);}
   else if(s.AckResult=="ok")recovery.Record(active.Action,true,now);
   if(active.Action.Kind=="sell"&&s.AckResult=="ok"){refillPending=true;refillRoom=active.Room;}
   if(active.Action.Kind=="summon"&&s.AckResult=="ok")refillPending=false;
   if(s.AckResult=="ok")planner.Confirm(active.Action,s,now);
   active.Command=0;active.Action=new();nextAction=now+TimeSpan.FromMilliseconds(settings.IntervalMs).Ticks;
  }
  if(GameFlow.NetworkBlocked(s)){Message=s.NetworkMessage;return;}
  if(now<nextAction){return;}
  var action=Plan(s,now);Message=action.Reason;
  if(!recovery.Ready(action,now)){Message="等待短暂冷却后重新决策："+action.Reason;return;}
  if(action.Kind=="complete"){StopLocked(action.Reason);return;}
  if(action.Kind!="wait")
  {
   active.Command=++command;active.Action=action;active.Room=s.Room;active.BoardKey=s.BoardKey;active.SnapshotAt=s.At;watchAt=now;
   Diagnostic?.Invoke($"#{command} {action.Kind} W{s.Wave} gold={s.Gold} enemy={s.EnemyCount}: {action.Reason}");
  }

 }
 private Decision Plan(Snapshot s,long now)
 {
  if(refillPending&&((s.Room.Length>0&&s.Room!=refillRoom)||GameFlow.RoundFinished(s)||s.Exiting||s.Stage=="lobby"||s.Stage=="match-failed"))refillPending=false;
  if(!refillPending)return planner.Decide(s,settings,now,index=>recovery.CanMove(index,now));
  if(s.Stage=="playing"&&s.Ready&&s.MyView&&s.Blocker.Length==0)
  {
   if(s.Units.Length>=s.Boards.Length){refillPending=false;return planner.Decide(s,settings,now,index=>recovery.CanMove(index,now));}
   if(s.CanSummon&&s.Gold>=s.Catalog.SummonCost)return new(){Kind="summon",Reason="出售已确认，优先补抽填回空位"};
  }
  return new(){Reason=s.Blocker.Length>0?"等待关闭弹窗："+s.Blocker:"出售已确认，等待金币和召唤按钮就绪后补位"};
 }
}
