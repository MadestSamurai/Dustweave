using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Threading;
using HarmonyLib;
using Rhythm;
using UnityEngine;
namespace BD2Rhythm.Runtime {
 internal sealed class RuntimeEngine {
  const string Patch="bd2.rhythm.runtime3";static RuntimeEngine current;Harmony harmony;Timer timer;int timerBusy;
  readonly Module module=typeof(RhythmHUD).Module;FieldInfo looperField,chartField,inputField,clockField,pausedField,blockerField;MethodInfo stateGetter,feverGetter;
  volatile RhythmControl control=new RhythmControl();volatile RhythmSnapshot snapshot=new RhythmSnapshot();long mainTicks;long hudTicks;
  RhythmHUD active;RhythmGameInput input;object activeChart;InputPlan plan;string owner="";string failed="";string currentSong="";int planJitter=50;int planOffset;int lastClock=int.MinValue;int complete;bool counted;double nextFever;bool lastFever;
  readonly int pid=System.Diagnostics.Process.GetCurrentProcess().Id;
  internal void Start(){
   if(module.ModuleVersionId.ToString()!=ClientMap.Mvid)throw new InvalidOperationException("客户端已变化，请重新连接。");
   looperField=module.ResolveField(ClientMap.Looper);chartField=module.ResolveField(ClientMap.Chart);inputField=module.ResolveField(ClientMap.Input);clockField=module.ResolveField(ClientMap.Clock);pausedField=module.ResolveField(ClientMap.Paused);blockerField=module.ResolveField(ClientMap.Blocker);stateGetter=(MethodInfo)module.ResolveMethod(ClientMap.State);feverGetter=(MethodInfo)module.ResolveMethod(ClientMap.Fever);
   current=this;harmony=new Harmony(Patch);harmony.Patch(module.ResolveMethod(ClientMap.Update),postfix:new HarmonyMethod(typeof(RuntimeEngine).GetMethod("HudTick",BindingFlags.Static|BindingFlags.NonPublic)));
   harmony.Patch(module.ResolveMethod(ClientMap.FirstUpdate),postfix:new HarmonyMethod(typeof(RuntimeEngine).GetMethod("Reset",BindingFlags.Static|BindingFlags.NonPublic)));
   harmony.Patch(module.ResolveMethod(ClientMap.Disable),prefix:new HarmonyMethod(typeof(RuntimeEngine).GetMethod("Disable",BindingFlags.Static|BindingFlags.NonPublic)));
   harmony.Patch(module.ResolveMethod(ClientMap.Pump),postfix:new HarmonyMethod(typeof(RuntimeEngine).GetMethod("Pump",BindingFlags.Static|BindingFlags.NonPublic)));
   mainTicks=DateTime.UtcNow.Ticks;timer=new Timer(Background,null,0,100);Loader.Status("active","");LocalStorage.Log("runtime_start live_chart=true");
  }
  internal void PrepareHandoff(){control=new RhythmControl();Clear(null);}
  internal string HandoffBusy()=>timerBusy!=0?"snapshot writer":"";
  internal void Stop(){Clear(null);if(timer!=null){timer.Dispose();timer=null;}if(harmony!=null)harmony.UnpatchAll(Patch);if(current==this)current=null;}
  static void Pump(){if(current!=null)Interlocked.Exchange(ref current.mainTicks,DateTime.UtcNow.Ticks);}
  static void Reset(RhythmHUD __instance){if(current==null)return;current.Clear(__instance);LocalStorage.Log("song_reset");}
  static void Disable(RhythmHUD __instance){if(current!=null&&current.active==__instance)current.Clear(null);}
  void Clear(RhythmHUD hud){if(plan!=null&&input!=null)try{plan.Release(Send);}catch{}active=hud;activeChart=null;plan=null;failed="";currentSong="";lastClock=int.MinValue;counted=false;lastFever=false;}
  static void HudTick(RhythmHUD __instance){if(current==null)return;try{current.Tick(__instance);}catch(Exception e){current.failed=e.GetBaseException().Message;LocalStorage.Log("error "+current.failed);current.Publish("error",current.failed,0);}}
  void Background(object unused){if(Interlocked.Exchange(ref timerBusy,1)!=0)return;try{
   var path=Path.Combine(LocalStorage.DataRoot,"control.json");try{using(var f=new MemoryStream(BD2.LocalIpc.RuntimeFiles.Read(path)??new byte[0]))control=(RhythmControl)new DataContractJsonSerializer(typeof(RhythmControl)).ReadObject(f);}catch{} // Retain the previous command until its existing lease expires.
   var now=DateTime.UtcNow;var s=snapshot;
   if(now.Ticks-Interlocked.Read(ref hudTicks)>TimeSpan.FromSeconds(1).Ticks){s=new RhythmSnapshot{ProcessId=pid,CapturedUtcTicks=Interlocked.Read(ref mainTicks),State="waiting",Reason="等待曲目开始",Armed=control.Valid(now.Ticks,pid),OwnerId=control.OwnerId,CompletedSongs=complete,JitterMs=control.JitterMs,OffsetMs=control.OffsetMs};}
   LocalStorage.WriteJsonAtomically(Path.Combine(LocalStorage.DataRoot,"latest.json"),s);
   LocalStorage.WriteJsonAtomically(Path.Combine(LocalStorage.DataRoot,"runtime.json"),new RhythmRuntimeStatus{State="active",ProcessId=pid,AtUtc=new DateTime(Interlocked.Read(ref mainTicks),DateTimeKind.Utc).ToString("O")});
  }catch(Exception e){LocalStorage.Log("io "+e.Message);}finally{Interlocked.Exchange(ref timerBusy,0);}}
  Chart LiveChart(RhythmLevelDataScriptable asset){return new Chart{Name=asset.name,Difficulty=Convert.ToInt32(asset.difficulty),Bpm=asset.bpm,Notes=asset.noteDataList.Select(n=>new Note{Id=n.noteId,Type=Convert.ToInt32(n.noteType),Lane=n.coord.xPos,Time=n.coord.yPos,Extra=n.additionalCoordList.Select(e=>new Coord{Lane=e.xPos,Time=e.yPos}).ToList()}).ToList()};}
  bool CanInput(object loop){var blocker=blockerField.GetValue(active) as GameObject;return Convert.ToInt32(stateGetter.Invoke(loop,null))==1&&!(bool)pausedField.GetValue(active)&&(blocker==null||!blocker.activeSelf);}
  void Tick(RhythmHUD hud){
   Pump();Interlocked.Exchange(ref hudTicks,DateTime.UtcNow.Ticks);if(active!=hud)Clear(hud);input=(RhythmGameInput)inputField.GetValue(hud);var loop=looperField.GetValue(hud);if(loop==null||input==null){Publish("waiting","等待谱面载入",0);return;}
   int clock=(int)clockField.GetValue(loop);bool allowed=CanInput(loop);var c=control;bool armed=c.Valid(DateTime.UtcNow.Ticks,pid);
   if(!armed||owner!=c.OwnerId){if(plan!=null&&plan.HasHeld&&!allowed){Publish("stopping","等待游戏恢复后释放长按",clock);return;}if(plan!=null)plan.Release(Send);plan=null;owner=c.OwnerId;failed="";if(!armed){Publish("stopped","自动演奏已停止",clock);return;}}
   var asset=chartField.GetValue(hud) as RhythmLevelDataScriptable;if(asset==null){Publish("waiting","等待谱面载入",clock);return;}
   if(activeChart!=asset||clock<lastClock-100){if(plan!=null)plan.Release(Send);plan=null;activeChart=asset;failed="";counted=false;lastFever=false;}
   lastClock=clock;
   if(Convert.ToInt32(stateGetter.Invoke(loop,null))!=1){if(plan!=null){plan.Release(Send);if(!counted){complete++;counted=true;LocalStorage.Log("song_end "+currentSong+" sent="+plan.Sent+" skipped="+plan.Skipped);}}Publish("waiting","本曲结束，等待下一首",clock);return;}
   if(!allowed){Publish("paused","游戏暂停或倒计时，等待继续",clock);return;}
   if(failed!=""){Publish("error",failed,clock);return;}
   if(plan==null){var chart=LiveChart(asset);
    if(input.onPressedLeftButton==null||input.onReleasedLeftButton==null||input.onPressedRightButton==null||input.onReleasedRightButton==null||input.onPressedLeftSlide==null||input.onPressedRightSlide==null)throw new InvalidOperationException("音游输入尚未绑定。");
    planJitter=c.JitterMs;planOffset=c.OffsetMs;plan=InputPlan.Build(chart,planJitter,planOffset,Guid.NewGuid().GetHashCode());plan.SkipBefore(clock-30);currentSong=chart.Name;LocalStorage.Log("song_start "+currentSong+" jitter="+c.JitterMs+" offset="+c.OffsetMs+" clock="+clock);
   }
   bool fever=feverGetter.Invoke(loop,null).ToString()=="FeverTime";
   if(fever){if(!lastFever){plan.Release(Send);nextFever=clock;}plan.SkipBefore(clock+1);if(clock>=nextFever){input.onPressedLeftButton(0);input.onReleasedLeftButton(0);input.onPressedRightButton(0);input.onReleasedRightButton(0);nextFever=clock+100;}Publish("playing","Fever 连打中",clock);}
   else {if(lastFever)plan.Release(Send);plan.Advance(clock,Send);Publish("playing",plan.Cursor==plan.Events.Count?"谱面已完成，等待游戏结算":"按谱面演奏中",clock);}
   lastFever=fever;
  }
  void Send(InputEvent e){if(input==null)return;Action<int> action=e.Lane==1?(e.Kind==InputKind.Press?input.onPressedLeftButton:e.Kind==InputKind.Release?input.onReleasedLeftButton:input.onPressedLeftSlide):(e.Kind==InputKind.Press?input.onPressedRightButton:e.Kind==InputKind.Release?input.onReleasedRightButton:input.onPressedRightSlide);if(action==null)throw new InvalidOperationException("输入回调已解除。");action(e.Channel);}
  void Publish(string state,string reason,int clock){snapshot=new RhythmSnapshot{ProcessId=pid,CapturedUtcTicks=DateTime.UtcNow.Ticks,State=state,Reason=reason,Song=currentSong,ClockMs=clock,DurationMs=plan==null?0:plan.Events.Last().At,Sent=plan==null?0:plan.Sent,Skipped=plan==null?0:plan.Skipped,Total=plan==null?0:plan.Events.Count(e=>e.Kind==InputKind.Press),LastErrorMs=plan==null?0:plan.LastError,Armed=control.Valid(DateTime.UtcNow.Ticks,pid),OwnerId=control.OwnerId,JitterMs=plan==null?control.JitterMs:planJitter,OffsetMs=plan==null?control.OffsetMs:planOffset,Error=failed,CompletedSongs=complete};}
 }
}
