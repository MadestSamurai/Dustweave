using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Proto.Net;
using UnityEngine;
using UnityEngine.UI;
namespace BD2Daily.Runtime {
 internal sealed class GuildRuntime {
  private static GuildRuntime active;
  private readonly GuildStore store=new GuildStore(LocalStorage.Root);
  private readonly Queue<Response> responses=new Queue<Response>();
  private readonly Queue<DailySnapshot> history=new Queue<DailySnapshot>();private Action requestCallback;
  private Harmony harmony;private MethodBase info,response;private DailySnapshot snapshot;
  private GuildReceipt receipt;private GuildCommand command;private string failure="";
  private bool popupClicked,backClicked,observedGuild;private long responseAt;private string lastDecision="";
  private sealed class Response {internal GuildInfoResponse Data;internal int Error;internal bool Accepted;internal Action Callback;}
  private static object Read(string role,object instance=null){return ((MethodInfo)GuildBindings.Get(role)).Invoke(instance,null);}
  private static object Field(string role,object instance){return ((FieldInfo)GuildBindings.Get(role)).GetValue(instance);}
  internal void Start(){
   if(!GuildBindings.Supported){failure="本机公会接口未匹配，账号识别仍可使用";return;}
   try{info=(MethodBase)GuildBindings.Get("Guild.Info");response=(MethodBase)GuildBindings.Get("Guild.Response");
    harmony=new Harmony("bd2.daily.guild.v2");active=this;
    harmony.Patch(info,prefix:new HarmonyMethod(typeof(GuildRuntime).GetMethod("BeforeInfo",BindingFlags.Static|BindingFlags.NonPublic)));
    harmony.Patch(response,postfix:new HarmonyMethod(typeof(GuildRuntime).GetMethod("AfterResponse",BindingFlags.Static|BindingFlags.NonPublic)));
   }catch(Exception e){Stop();failure="公会采证接口不可用："+e.GetBaseException().Message;}
  }
  internal void Stop(){if(active==this)active=null;if(harmony!=null){if(info!=null)harmony.Unpatch(info,HarmonyPatchType.All,harmony.Id);if(response!=null)harmony.Unpatch(response,HarmonyPatchType.All,harmony.Id);}harmony=null;}
  private UIBase[] Uis(){return UnityEngine.Object.FindObjectsOfType<UIBase>().Where(u=>u!=null&&u.gameObject.activeInHierarchy).ToArray();}
  internal GuildObservation Observe(bool includeSurfaces=true){
   var g=new GuildObservation{Supported=GuildBindings.Supported&&harmony!=null,Error=failure,ClientMvid=GuildBindings.Mvid};if(!g.Supported)return g;
   if(snapshot==null||snapshot.State!="identified"||snapshot.Scene=="ReGame"||(snapshot.Startup!=null&&snapshot.Startup.Visible)){g.Supported=false;g.BlockReason="等待游戏数据与角色加载完成，公会观察已暂停";return g;}
   try{
    var member=(GuildMemberDBInfo)Read("Guild.Member");var guild=(GuildBaseDBInfo)Read("Guild.Base");
    g.InGuild=member!=null&&member.Id>0&&guild!=null&&guild.Id==member.Id&&snapshot!=null&&DailyIdentity.PlayerKey(snapshot.AccountKey,member.OwnerIndex)==snapshot.PlayerKey;
    if(g.InGuild){g.GuildKey=DailyIdentity.Hash("guild|"+member.Id);g.GuildName=guild.Name??"";}
    var network=Read("Services.Network");var server=(string)Read("Network.Server",network);if(!string.IsNullOrEmpty(server))g.ServerKey=DailyIdentity.Hash("server|"+server);
    var timer=(gamfs.Thread.TimerManager)Read("Services.Timer");g.ServerTicks=timer.Now().Ticks;g.ResetTicks=timer.GetDailyResetTime().Ticks;g.CycleKey=g.ResetTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
    if(!includeSurfaces)return g;
    var app=(AppManager)Read("Services.App");var uis=Uis();g.ActiveUis=uis.Select(u=>u.GetType().Name).Distinct().OrderBy(n=>n).ToArray();
    var menu=uis.OfType<MenuUI>().FirstOrDefault();var button=menu==null?null:(GameObject)Field("Menu.Guild",menu);
    g.MenuVisible=button!=null&&button.activeInHierarchy;
    g.GuildVisible=uis.Any(u=>u is GuildUI);
    g.AttendancePopup=uis.OfType<GuildJoinAttendancePopupUI>().Any(p=>((GameObject)Field("Popup.Attendance",p)).activeInHierarchy);
    var foreign=uis.Where(u=>(bool)Field("UI.Popup",u)&&!(bool)((MethodInfo)GuildBindings.Get("UI.IsHud")).Invoke(null,new object[]{u})&&!(u is GuildUI)&&!(u is MenuUI)&&!(u is GuildJoinAttendancePopupUI)).Select(u=>u.GetType().Name).ToArray();
    if(app.IsActiveUILoading()||app.IsPlayAnimUILoading())g.BlockReason="等待游戏加载完成";
    else if(foreign.Length>0)g.BlockReason="请先处理游戏弹窗："+string.Join("、",foreign);
    else if(g.ActiveUis.Any(n=>n.Contains("Battle")||n.Contains("Login")))g.BlockReason="请先回到游戏主菜单";
    else if(uis.OfType<GuildJoinAttendancePopupUI>().Any()&&!g.AttendancePopup)g.BlockReason="入会弹窗需要手动处理";
   }catch(Exception e){g.Supported=false;g.Error=e.GetBaseException().GetType().Name+": "+e.GetBaseException().Message;}
   if(failure.Length>0)g.BlockReason=failure;return g;
  }
  internal void Tick(DailySnapshot s,bool allowActions=true){
   snapshot=s;s.Guild=Observe(allowActions);history.Enqueue(s);while(history.Count>10)history.Dequeue();if(s.Guild.Supported)s.Capabilities=new[]{"account.identity","guild.observe","guild.single"};
   if(!s.Guild.Supported||!allowActions)return;
   try{
    lock(responses){while(responses.Count>0)ApplyResponse(responses.Dequeue());}
    if(receipt!=null&&!GuildPolicy.Terminal(receipt.State))Advance();
    ReceiveCommand();
   }catch(Exception e){failure="操作记录或状态异常："+e.GetBaseException().Message;s.Guild.Error=failure;s.Guild.BlockReason=failure;
    if(receipt!=null&&!GuildPolicy.Terminal(receipt.State)){receipt.State="unknown";receipt.Message=failure;try{store.Save(receipt);}catch{}}
   }
  }
  private void Trace(string name){store.Append(receipt.Id,new GuildTrace{Event=name,Snapshot=snapshot,Receipt=receipt});}
  private void Save(string name){store.Save(receipt);Trace(name);}
  private bool SameIdentity(){return GuildPolicy.SameIdentity(snapshot,receipt);}
  private void Finish(string state,string message){receipt.State=state;receipt.Message=message;Save("terminal");}
  internal bool HandoffBusy{get{return receipt!=null&&!GuildPolicy.Terminal(receipt.State);}}
  private void ReceiveCommand(){
   var path=Path.Combine(store.Root,"guild-command.json");var bytes=BD2.LocalIpc.RuntimeFiles.Take(path);if(bytes==null)return;GuildCommand c;using(var memory=new MemoryStream(bytes))c=(GuildCommand)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(GuildCommand)).ReadObject(memory);if(c==null)return;
   if(!GuildStore.ValidId(c.Id))throw new InvalidDataException("Invalid command ID; no operation performed");
   if(!BD2.LocalIpc.RuntimeFiles.TryClaim(c.Id,c.ExpiresUtcTicks))return;
   var incoming=store.Receipt(c.Id);if(incoming==null)throw new InvalidDataException("Command has no durable intent");
   if(receipt!=null&&!GuildPolicy.Terminal(receipt.State)){incoming.State="rejected";incoming.Message="已有公会操作正在等待结果";store.Save(incoming);return;}
   var now=DateTime.UtcNow.Ticks;var lease=LocalStorage.Read<DailyLease>("lease.json");
   bool bound=GuildPolicy.Matches(c,snapshot,lease,now)&&incoming.State=="prepared"&&!incoming.MayHaveDispatched&&incoming.AccountKey==c.AccountKey&&incoming.PlayerKey==c.PlayerKey&&incoming.GuildKey==c.GuildKey&&incoming.ServerKey==c.ServerKey&&incoming.CycleKey==c.CycleKey;
   var reason=bound?GuildPolicy.Gate(snapshot,store.Prior(snapshot,c.Id),now):"操作身份、控制权或有效期不匹配";
   if(reason.Length>0){incoming.State="rejected";incoming.Message=reason;store.Save(incoming);return;}
   receipt=incoming;command=c;requestCallback=null;lastDecision="";foreach(var frame in history)store.Append(receipt.Id,new GuildTrace{Event="before_snapshot",Snapshot=frame});popupClicked=false;backClicked=false;observedGuild=false;responseAt=0;
   var menu=Uis().OfType<MenuUI>().Single();var button=(GameObject)Field("Menu.Guild",menu);
   receipt.MayHaveDispatched=true;receipt.State="waiting_response";receipt.Message="已记录进入公会意图，等待服务器结果";
   Save("before_entry"); // Must reach stable storage before touching the game.
   if(!GuildPolicy.Matches(c,snapshot,LocalStorage.Read<DailyLease>("lease.json"),DateTime.UtcNow.Ticks)){receipt.MayHaveDispatched=false;Finish("rejected","提交前控制权已过期，没有进入公会");return;}
   menu.OnClickUI(button); // Normal game entry, including its unlock and network gates.
  }
  private static void BeforeInfo(Action __0){var a=active;if(a==null)return;try{a.RecordRequest(__0);}catch(Exception e){a.failure="请求采证失败："+e.GetBaseException().Message;} }
  private void RecordRequest(Action callback){
   if(snapshot==null||snapshot.Guild==null||!snapshot.Guild.InGuild||snapshot.State!="identified"||snapshot.FrameUtcTicks<DateTime.UtcNow.AddSeconds(-5).Ticks)return;
   if(receipt==null||(GuildPolicy.Terminal(receipt.State)&&receipt.State!="unknown")){
    receipt=GuildStore.Intent(Guid.NewGuid().ToString("N"),snapshot);receipt.Passive=true;command=null;receipt.State="waiting_response";popupClicked=false;backClicked=false;responseAt=0;
   }
   if(receipt.RequestCount==0)requestCallback=callback;else requestCallback=null;
   receipt.MayHaveDispatched=true;receipt.RequestCount++;Save("request_observed");
  }
  private static void AfterResponse(object __instance,byte[] __0,int __2,bool __result){
   var a=active;if(a==null)return;
   try{var r=new Response{Error=__2,Accepted=__result,Callback=(Action)Field("Guild.Callback",__instance)};if(__0!=null)r.Data=GuildInfoResponse.Parser.ParseFrom(__0);lock(a.responses)a.responses.Enqueue(r);}
   catch{lock(a.responses)a.responses.Enqueue(new Response{Error=-1});}
  }
  private void ApplyResponse(Response response){
   if(receipt==null)return;
   if(requestCallback==null||!object.ReferenceEquals(requestCallback,response.Callback)){Trace("unattributed_response");if(receipt.RequestCount>1)Finish("unknown","存在交错请求，无法唯一归属回执；不会重发");return;}
   if(receipt.ResponseSeen){receipt.RequestCount++;Finish("unknown","收到重复或交错的公会响应，保留记录供核对");return;}
   receipt.CallbackMatched=true;receipt.ResponseSeen=true;receipt.ErrorCode=response.Error!=0?response.Error:response.Accepted?0:-2;
   var dto=response.Data;receipt.AttendanceGranted=dto!=null&&dto.IsAttendance;
   if(SameIdentity()&&dto!=null&&dto.GuildInfo!=null&&dto.GuildInfo.GuildBaseInfo!=null){
    var member=(GuildMemberDBInfo)Read("Guild.Member");var guild=(GuildBaseDBInfo)Read("Guild.Base");
    var id=dto.GuildInfo.GuildBaseInfo.Id;
    receipt.MemberMatched=DailyIdentity.Hash("guild|"+id)==receipt.GuildKey&&dto.MemberInfo.Any(m=>m.Id==id&&DailyIdentity.PlayerKey(snapshot.AccountKey,m.OwnerIndex)==receipt.PlayerKey);
    receipt.CacheMatched=member!=null&&guild!=null&&member.Id==id&&guild.Id==id&&DailyIdentity.PlayerKey(snapshot.AccountKey,member.OwnerIndex)==receipt.PlayerKey;
   }
   responseAt=DateTime.UtcNow.Ticks;receipt.State="response_received";Save("response");
   if(!SameIdentity()||GuildPolicy.Outcome(receipt)=="unknown")Finish("unknown","公会响应未能确认身份、请求归属或成功状态；不会重发请求");
   else if(receipt.Passive)Finish(GuildPolicy.Outcome(receipt),receipt.AttendanceGranted?"观察到服务器授予签到；界面由你继续操作":"观察到服务器未新增签到；本周期不重复请求");
  }
  private void Advance(){
   var now=DateTime.UtcNow.Ticks;var lease=LocalStorage.Read<DailyLease>("lease.json");observedGuild|=snapshot.Guild.GuildVisible;
   var input=new GuildTrace{Event="decision",Snapshot=snapshot,Receipt=receipt,NowUtcTicks=now,ResponseAtTicks=responseAt,HasControl=command!=null&&lease!=null&&lease.Matches(snapshot,now)&&lease.Owner==command.Owner,PopupClicked=popupClicked,BackClicked=backClicked,ObservedGuild=observedGuild};
   var next=GuildPolicy.Next(input);input.Decision=next;
   if(next!=lastDecision){store.Append(receipt.Id,input);lastDecision=next;}
   switch(next){
    case "identity_changed":Finish("unknown","操作期间账号、公会或重置周期发生变化；不会继续点击");return;
    case "timeout":Finish("unknown","等待公会响应超时；请求可能已完成，不会重发");return;
    case "unknown":Finish("unknown","响应证据不完整，不会继续点击");return;
    case "release":case "cleanup_timeout":Finish(receipt.AttendanceGranted?"confirmed_cleanup_pending":"checked_no_grant","响应已记录；请手动返回主菜单，程序不会重试签到");return;
    case "finish":receipt.ReturnedHome=true;Finish(GuildPolicy.Outcome(receipt),receipt.AttendanceGranted?"签到已确认，已返回主菜单；本次单步结束":"服务器未新增签到，已返回主菜单；本次单步结束");return;
    case "close_attendance":{
     var popup=Uis().OfType<GuildJoinAttendancePopupUI>().Single();var button=(Button)Field("Popup.Button",popup);
     popupClicked=true;Save("before_close_attendance");popup.OnClickUI(button.gameObject);return;
    }
    case "return_menu":{
     var guild=Uis().OfType<GuildUI>().Single();var back=(GameObject)Field("UI.Back",guild);if(back==null||!back.activeInHierarchy)return;
     backClicked=true;Save("before_return_menu");guild.OnClickUI(back);return;
    }
   }
  }
 }
}
