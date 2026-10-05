using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
namespace BD2ApostleDefense
{
 public static class Identity
 {
        public const string LiveEntries="runtime.json|control.json|snapshot.json";
  public const string Version="0.3.6",Runtime="BD2ApostleDefense.Runtime9";
  public static string Root {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2ApostleDefense");}}
  public static string Hash(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
 }
 [DataContract] public class Point { [DataMember] public int Id; [DataMember] public double X,Y,Z; }
 [DataContract] public class Board : Point { [DataMember] public int Element; [DataMember] public double ScreenX,ScreenY; }
 // AttackState normalizes native Idle=1/Attack=2/FindTarget=3 to 0/1/2. Validated before injection.
 [DataContract] public class Unit { [DataMember] public int Index,Id,Grid,TargetEnemy=-1,AttackState=2; [DataMember] public double AttackElapsed; [DataMember] public bool Ready; }
 [DataContract] public class Enemy : Point { [DataMember] public int Table,Element,NextPoint=-1; [DataMember] public double Hp,Speed; }
 [DataContract] public class UnitDef
 {
  [DataMember] public int Id,Element,Grade,Sell,Weight,Attack,UpAttack;
  [DataMember] public string Name="";
  [DataMember] public double Interval,Range,SplashRange;
 }
 [DataContract] public class WaveDef { [DataMember] public int Id,Element,Count,Gold,EnemyTable; [DataMember] public bool Boss; [DataMember] public double Hp,Duration,Speed,SpawnInterval; }
 [DataContract] public class Catalog
 {
  [DataMember] public UnitDef[] Units=new UnitDef[0]; [DataMember] public WaveDef[] Waves=new WaveDef[0];
  [DataMember] public int SummonCost,MaxGrade,MaxUpgrade,GameOverCount; [DataMember] public double Advantage,Penalty;
  [DataMember] public string Key="";
 }
 [DataContract] public class Snapshot
 {
  [DataMember] public string Runtime=Identity.Runtime,Account="",Name="",Room="",Stage="waiting",State="idle",Message="",Owner="",AckResult="",AckMessage="",Blocker="",BoardKey="";
  [DataMember] public long At,ProcessStart,Sequence,Ack,AcceptedCommand;
  [DataMember] public int ProcessId,Wave,Gold,EnemyCount,RoundRare,ClearProgress,RareProgress,ClearTarget=1,RareTarget=3;
  [DataMember] public bool Ready,AchievementsKnown,CanSummon,Win,Dead,MyView,RoomEnded,CanStartMatch,Exiting,EventPopupOpen,CanDismissEvent;
  [DataMember] public bool NetworkHold,SettlementPending,SettlementRoomEnded;
  [DataMember] public string NetworkState="inactive",NetworkMessage="";
  [DataMember] public long NetworkLastPacketAt;
  [DataMember] public int NetworkProbeFailures,NetworkAvoidedDisconnects,NetworkNativeDisconnects;
  [DataMember] public double SecondsLeft;
  [DataMember] public int[] Levels=new int[5],UpgradeCosts=new int[5];
  [DataMember] public bool[] CanUpgrade=new bool[5];
  [DataMember] public Board[] Boards=new Board[0]; [DataMember] public Point[] Path=new Point[0];
  [DataMember] public Unit[] Units=new Unit[0]; [DataMember] public Enemy[] Enemies=new Enemy[0];
  [DataMember] public SurfaceInfo[] Surfaces=new SurfaceInfo[0];
  [DataMember] public Catalog Catalog=new Catalog();
 }
 [DataContract] public class Settings
 {
  [DataMember] public bool Clear50=true,Rare=true,AutoNext=true,StopAfterRound;
  [DataMember] public int IntervalMs=500;
  public string Validate(){return !Clear50&&!Rare?"请至少选择一个目标":IntervalMs<100||IntervalMs>3000?"操作间隔需要在100–3000ms之间":"";}
 }
 [DataContract] public class Decision
 {
  [DataMember] public string Kind="wait",Reason="",Policy="";
  [DataMember] public int UnitIndex=-1,UnitId=-1,Grid=-1,TargetGrid=-1,TargetIndex=-1,Element=-1,Wave,TargetEnemy=-1;
  [DataMember] public double Score;
 }
 [DataContract] public class Control
 {
  [DataMember] public bool Enabled;
  [DataMember] public string Owner="",Account="",Room="",BoardKey="";
  [DataMember] public long ProcessStart,Expires,Command,SnapshotAt,CancelThrough;
  [DataMember] public int ProcessId;
  [DataMember] public Decision Action=new Decision();
 }
 [DataContract] public class RuntimeStatus { [DataMember] public string State="",Error=""; [DataMember] public int ProcessId; [DataMember] public long ProcessStart,At; }
 [DataContract] public class ActionEvidence
 {
  [DataMember] public long At;
  [DataMember] public string Result="",Message="";
  [DataMember] public Control Command=new Control();
  [DataMember] public Snapshot Before=new Snapshot(),After=new Snapshot();
 }
 [DataContract] public class FlowEvidence
 {
  [DataMember] public long At,Command,Ack,AcceptedCommand;
  [DataMember] public int ProcessId,Wave;
  [DataMember] public string Runtime="",Stage="",State="",Blocker="",Action="",AckResult="",Message="";
  [DataMember] public bool Win,Dead,RoomEnded,Exiting,NetworkHold,SettlementPending,SettlementRoomEnded;
 }
 [DataContract] public class NetworkEvidence
 {
  [DataMember] public string Runtime=Identity.Runtime;
  [DataMember] public int ProcessId=System.Diagnostics.Process.GetCurrentProcess().Id;
  [DataMember] public long At,LastPacketAt;
  [DataMember] public string Kind="",Room="",State="";
  [DataMember] public int ProbeFailures,AvoidedDisconnects,NativeDisconnects;
 }
 public static class Guards
 {
  public static bool Fresh(Snapshot s,int pid,long start,long now){return s!=null&&s.Runtime==Identity.Runtime&&s.ProcessId==pid&&s.ProcessStart==start&&s.At>=now-TimeSpan.FromSeconds(5).Ticks&&s.At<=now+TimeSpan.FromSeconds(2).Ticks;}
  public static string Reject(Control c,Snapshot s,long now)
  {
   if(c==null||!c.Enabled||c.Expires<now||c.Expires>now+TimeSpan.FromSeconds(15).Ticks)return "控制租约失效";
   if(c.Owner.Length!=32||c.Account.Length!=64||c.Account!=s.Account||c.ProcessId!=s.ProcessId||c.ProcessStart!=s.ProcessStart)return "账号或进程已变化";
   if(c.SnapshotAt<now-TimeSpan.FromSeconds(3).Ticks||c.SnapshotAt>now+TimeSpan.FromSeconds(1).Ticks)return "决策盘面过期";
   if(GameFlow.NetworkBlocked(s))return s.NetworkMessage.Length>0?s.NetworkMessage:"网络恢复中，保留任务并等待服务器与盘面同步";
   if(c.Room!=s.Room||c.BoardKey!=s.BoardKey)return "盘面已变化，重新决策";
   if(c.Action.Kind=="close-event")return s.Stage=="lobby"&&!s.Exiting&&s.CanDismissEvent?"":"等待可关闭的大厅活动弹窗";
   if(s.Blocker.Length>0)return "等待关闭弹窗："+s.Blocker;
   var a=c.Action;
   if(a.Policy=="boss-chase")
   {
    var wave=s.Catalog.Waves.FirstOrDefault(w=>w.Id==s.Wave);
    if(a.Wave!=s.Wave||wave==null||!wave.Boss||!s.Enemies.Any(e=>e.Id==a.TargetEnemy&&e.Table==wave.EnemyTable&&e.Hp>0))return "追击目标或首领波已变化，重新决策";
    if(c.SnapshotAt<now-TimeSpan.FromMilliseconds(900).Ticks)return "追击位置已过期，重新读取首领位置";
   }
   if(a.Kind=="open-result"&&(s.Stage!="ended"||!GameFlow.CanSettle(s)))return "对局尚未结束或已开始退出";
   if(a.Kind=="settle"&&(s.Stage!="result"||!GameFlow.CanSettle(s)))return "等待胜败结算页面";
   if(a.Kind=="confirm-exit"&&(s.Stage!="confirm-exit"||!GameFlow.CanSettle(s)))return "不自动放弃进行中的对局或重复提交退出";
   if(a.Kind=="summon"||a.Kind=="sell"||a.Kind=="upgrade"||a.Kind=="move")
   {
    if(!s.Ready||s.Stage!="playing"||!s.MyView||GameFlow.RoundFinished(s)||s.Exiting)return "等待己方战斗盘面";
    if(a.Kind=="summon"&&(!s.CanSummon||s.Gold<s.Catalog.SummonCost||s.Units.Length>=s.Boards.Length))return "召唤条件变化";
    if(a.Kind=="upgrade"&&(a.Element<0||a.Element>=5||!s.CanUpgrade[a.Element]||s.UpgradeCosts[a.Element]<=0||s.Gold<s.UpgradeCosts[a.Element]))return "升级条件变化";
    if(a.Kind=="sell"||a.Kind=="move")
    {
     if(!s.Units.Any(u=>u.Index==a.UnitIndex&&u.Id==a.UnitId&&u.Grid==a.Grid&&u.Ready))return "目标使徒已变化";
     if(a.Kind=="sell"&&s.Units.Length<=1)return "保留最后一名使徒";
     if(a.Kind=="move"&&(!s.Boards.Any(b=>b.Id==a.TargetGrid)||a.TargetGrid==a.Grid||(s.Units.FirstOrDefault(u=>u.Grid==a.TargetGrid)?.Index??-1)!=a.TargetIndex))return "目标格已变化";
    }
   }
   return "";
  }
  public static string BoardKey(Snapshot s){return Identity.Hash(s.Room+"|"+string.Join(";",s.Units.OrderBy(u=>u.Index).Select(u=>u.Index+":"+u.Id+":"+u.Grid+":"+u.Ready))+"|"+string.Join(",",s.Levels));}
  public static bool GoalsMet(Snapshot s,Settings p){return s.AchievementsKnown&&(!p.Clear50||s.ClearProgress>=s.ClearTarget)&&(!p.Rare||s.RareProgress>=s.RareTarget);}
 }
}
