using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Proto.Net;
using Proto.Design.common;
using Neo.Unity.Neon;
using UnityEngine;
namespace BD2InfiniteGacha.Runtime
{
    internal sealed class RuntimeEngine
    {
        private bool networkInFlight,needsRecovery;
        private long sentTicks,refreshSent;private string sentAccount;private int sentPool;
        private GachaResultUI ownedBusyUi;
        private volatile int[] reconciledResult;private bool reconciledLocked;
        private readonly BD2.LocalIpc.NativeNetworkWatch network=new BD2.LocalIpc.NativeNetworkWatch();
        private static RuntimeEngine active;private Harmony harmony;private Timer heartbeat;
        private readonly Dictionary<string,MemberInfo> members=new Dictionary<string,MemberInfo>();
        private readonly AutomationMachine machine=new AutomationMachine();
        private long sequence,response,result,readAt,catalogAt;private int responseError,pid;private long start;
        private readonly string instance=Guid.NewGuid().ToString("N");private Pool[] pools=new Pool[0];private string account="";private bool inventoryReady;
        private GachaResultUI ui;
        private int inventoryCount,skipCount;private long nextSkip;
        private bool probeOnly;
        private Control lastControl;private readonly PublicationBuffer publication=new PublicationBuffer();private long readFailedAt,publishedAt;private string publishedKey="";private int publishedSkips;private string loggedState="",loggedMessage="";
        private MemberInfo Member(string role)
        {
            MemberInfo m;if(members.TryGetValue(role,out m))return m;
            var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
            var typeName=(string)typeof(Names).GetField(role+"Type",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
            var name=(string)typeof(Names).GetField(role+"Name",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
            var type=typeof(UserDBInfo).Assembly.GetType(typeName,true);
            int count=role=="Expand"?2:role=="Preview"?6:role=="Response"||role=="RefreshResponse"||role=="SetResult"?3:role=="Frame"||role=="Product"||role=="SavedPreview"?0:1;
            m=(MemberInfo)type.GetProperty(name,flags)??type.GetField(name,flags)??(MemberInfo)type.GetMethods(flags).Single(x=>x.Name==name&&x.GetParameters().Length==count);
            members[role]=m;return m;
        }
        private object Get(string role,object owner=null){var m=Member(role);return m is PropertyInfo?((PropertyInfo)m).GetValue(owner,null):((FieldInfo)m).GetValue(owner);}
        private object Call(string role,params object[] args){return ((MethodInfo)Member(role)).Invoke(null,args);}
        private string Text(int id){return id>0?(string)Call("Text",id)??"":"";}
        internal void Start()
        {
            if(harmony!=null)return;using(var p=Process.GetCurrentProcess()){pid=p.Id;start=p.StartTime.ToUniversalTime().Ticks;}
            active=this;harmony=new Harmony("bd2.infinite-gacha.v1");
            network.Start(typeof(BDNetwork.NetworkManager),"bd2.gacha.network-watch");Patch("Frame","AfterFrame");Patch("Response","AfterResponse");Patch("SetResult","AfterResult");Patch("RefreshResponse","AfterRefresh");
            heartbeat=new Timer(_=>Loader.Status("active",""),null,0,1000);
        }
        internal void StartProbe()
        {
            probeOnly=true;using(var p=Process.GetCurrentProcess()){pid=p.Id;start=p.StartTime.ToUniversalTime().Ticks;}
            active=this;harmony=new Harmony("bd2.infinite-gacha.probe."+instance);Patch("Frame","AfterFrame");
        }
        private void ProbeTick(long now)
        {
            try{var s=Read(now);s.State="read_only_probe";Storage.Write("probe.json",s);}
            catch(Exception e){Storage.Write("probe.json",new Snapshot{ProcessId=pid,ProcessStart=start,At=now,State="error",Message=e.GetBaseException().Message});}
            finally{active=null;harmony.Unpatch((MethodInfo)Member("Frame"),HarmonyPatchType.All,harmony.Id);harmony=null;}
        }
        private void Patch(string role,string name){harmony.Patch((MethodInfo)Member(role),postfix:new HarmonyMethod(typeof(RuntimeEngine).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static)));}
        private static void AfterFrame(){if(active!=null)active.Tick();}
        private static void AfterResponse(bool __result,int __2){var a=active;if(a!=null){a.networkInFlight=false;a.responseError=__result&&__2==0?0:__2==0?-1:__2;Interlocked.Increment(ref a.response);}}
        private static void AfterRefresh(byte[] __0,int __2,bool __result){
            var a=active;if(a==null||!a.needsRecovery||__2!=0||!__result||a.refreshSent<a.sentTicks)return;
            try{var info=GachaInfoResponse.Parser.ParseFrom(__0);
                if(info.ResemaraPreviewItemInfo.Count!=1)return;
                var saved=info.ResemaraPreviewItemInfo[0];var cards=saved.ResemaraPreviewItemInfo;
                if(cards==null||cards.CostumeInfo.Count!=10)return;
                a.reconciledLocked=saved.IsLock;a.reconciledResult=cards.CostumeInfo.Select(c=>c.Id).ToArray();
            }catch(Exception e){Storage.Event("recovery_read_retry",e.GetBaseException().Message);}
        }
        private void ReconcileNetwork(Snapshot s,Control command,long now){
            if(networkInFlight&&now-sentTicks>=TimeSpan.FromSeconds(BD2.LocalIpc.RequestObservation.TimeoutSeconds).Ticks&&network.Idle){
                networkInFlight=false;needsRecovery=true;
                // Only clear the UI guard set by this tool, after the native queue has drained.
                if(ownedBusyUi!=null)((FieldInfo)Member("BusyPreview")).SetValue(ownedBusyUi,false);
                ownedBusyUi=null;Storage.Event("network_recovering","等待同步服务器抽取结果，恢复后自动继续");
            }
            if(needsRecovery&&s.Account==sentAccount&&s.PoolId==sentPool){
                var cards=reconciledResult;
                if(cards!=null&&network.Idle){s.Result=cards;s.Locked=reconciledLocked;s.ResultReady=!s.PopupOpen;s.ResultReconciled=true;}
                else if(command!=null&&command.Enabled&&command.Expires>now&&command.Account==s.Account&&command.PoolId==s.PoolId&&network.Idle&&now-refreshSent>=TimeSpan.FromSeconds(30).Ticks){
                    refreshSent=now;Call("Refresh",new object[]{null});
                }
            }
            s.NetworkPending=networkInFlight||(needsRecovery&&!s.ResultReconciled);
        }
        private static void AfterResult(){var a=active;if(a!=null)Interlocked.Increment(ref a.result);}
        private void RefreshCatalog(string identity)
        {
            var owned=Get("Inventory") as CostumeDBInfo[];inventoryReady=owned!=null&&owned.Length>0;inventoryCount=owned==null?0:owned.Length;
            var groups=new List<GachaGroupTable>();
            foreach(Define_GachaType type in Enum.GetValues(typeof(Define_GachaType))){var list=Call("Groups",type) as IEnumerable<GachaGroupTable>;if(list!=null)groups.AddRange(list);}
            var next=new List<Pool>();
            foreach(var g in groups.Where(g=>g.GachaSubType==(int)Define_GachaSubType.GstResemara).GroupBy(g=>g.Id).Select(g=>g.First()))
            {
                var draw=(GachaTable)Call("Draw",g.TenTimeGachaId);if(draw==null||draw.GachaCount!=10)continue;
                var candidates=new List<object>();
                foreach(int reward in new[]{draw.GachaRewardId,draw.FixedGachaRewardId}.Where(id=>id>0).Distinct())
                {var expanded=(IEnumerable)Call("Expand",reward,false);foreach(var item in expanded)if((int)Get("ItemType",item)==11)candidates.Add(item);}
                var costumes=new List<Costume>();
                foreach(int id in candidates.Select(c=>(int)Get("ItemId",c)).Distinct().OrderBy(i=>i))
                {
                    var c=(CostumeTable)Call("Costume",id);if(c==null)throw new InvalidOperationException("可抽服装缺少游戏数据："+id);
                    var character=(CharTable)Call("Character",c.UseUniqueCharId);
                    var have=owned==null?null:owned.Where(o=>o!=null&&o.Id==id).OrderByDescending(o=>o.Level).FirstOrDefault();
                    costumes.Add(new Costume{Id=id,Character=character==null?"":Text(character.CharNameTextId),Name=Text(c.CostumeNameTextId),Level=!inventoryReady?-2:have==null?-1:have.Level,MaxLevel=c.MaxLevel});
                }
                var schedule=(GachaScheduleDBInfo)Call("Schedule",g.Id);
                if(costumes.Count>0)next.Add(new Pool{EndTimeUnixMilliseconds=schedule==null?0:schedule.EndTime,Id=g.Id,DrawId=draw.Id,Size=draw.GachaCount,Name=Text(g.GachaNameTextId),Key=Identity.Hash(g.Id+"|"+draw.Id+"|"+string.Join(",",costumes.Select(c=>c.Id.ToString()).ToArray())),Costumes=costumes.ToArray()});
            }
            pools=next.ToArray();account=identity;
        }
        private Snapshot Read(long now)
        {
            var s=new Snapshot{ProcessId=pid,ProcessStart=start,At=now,Instance=instance,Sequence=++sequence};
            if(!NeonSdk.IsInitialized){s.Message="等待登录组件初始化";return s;}
            var member=NeonSdk.Auth==null?null:NeonSdk.Auth.LoggedMember;var user=(UserDBInfo)Get("Player");
            if(member==null||user==null||user.OwnerIndex<=0){s.Message="请先登录游戏";return s;}
            s.Account=Identity.Hash(member.MemberId.ToString()+"|"+user.OwnerIndex.ToString());s.Player=user.UserId??"";
            if(now>=catalogAt||account!=s.Account){RefreshCatalog(s.Account);catalogAt=now+TimeSpan.FromSeconds(5).Ticks;}
            s.InventoryReady=inventoryReady;s.InventoryCount=inventoryCount;s.Pools=pools;
            if(ui==null||!ui.gameObject.activeInHierarchy)ui=UnityEngine.Object.FindObjectsOfType<GachaResultUI>().FirstOrDefault(u=>u.gameObject.activeInHierarchy);
            if(ui==null){s.Message="请进入无限抽抽乐的十连结果页";return s;}
            s.PoolId=(int)Get("ResultGroup",ui);
            var pool=pools.FirstOrDefault(p=>p.Id==s.PoolId);if(pool==null){s.Message="当前不是可重复预览的无限抽抽乐卡池";return s;}
            var saved=(ResemaraGachaDBInfo)Call("SavedPreview");s.Locked=saved!=null&&saved.IsLock;
            var items=Get("ResultItems",ui) as IEnumerable;
            if(items!=null){var cards=new List<int>();foreach(var item in items)cards.Add((int)Get("ItemType",item)==11?(int)Get("ItemId",item):0);s.Result=cards.ToArray();}
            var button=Get("RedrawButton",ui) as GameObject;
            bool popup=UnityEngine.Object.FindObjectsOfType<UIPopupBase>().Any(p=>p.gameObject.activeInHierarchy);
            s.PopupOpen=popup;s.ResultReady=button!=null&&button.activeInHierarchy&&!popup&&!(bool)Get("BusyPreview",ui)&&s.Result.Length==10;
            var skip=Get("SkipButton",ui) as GameObject;s.AnimationStage=Get("AnimationStage",ui).ToString();
            s.CanSkip=!s.ResultReady&&!popup&&!s.Locked&&!(bool)Get("BusyPreview",ui)&&s.Result.Length==10&&skip!=null&&skip.activeInHierarchy&&s.AnimationStage!="ShowEnd";s.SkipCount=skipCount;
            s.ResultKey=instance+":"+Interlocked.Read(ref response)+":"+Interlocked.Read(ref result);
            s.Message=s.Locked?"当前结果已锁定":s.ResultReady?"结果已就绪":"等待结果动画或关闭游戏弹窗";return s;
        }
        private void Draw(Pool pool)
        {
            var product=(CashProductTable)Call("Product");if(product==null)throw new InvalidOperationException("游戏没有可用的无限抽抽乐活动");
            var group=(GachaGroupTable)Call("Group",pool.Id);
            if(group==null||group.GachaSubType!=(int)Define_GachaSubType.GstResemara||group.TenTimeGachaId!=pool.DrawId)throw new InvalidOperationException("卡池发生变化，已停止");
            var source=((MethodInfo)Member("Preview")).GetParameters()[0].ParameterType;
            var capturedUi=ui;((FieldInfo)Member("BusyPreview")).SetValue(capturedUi,true);
            networkInFlight=true;needsRecovery=false;reconciledResult=null;sentTicks=DateTime.UtcNow.Ticks;sentAccount=account;sentPool=pool.Id;ownedBusyUi=capturedUi;
            try{Call("Preview",Enum.Parse(source,"SourceFromSpecialGacha"),product.GroupId,product.Id,product.SaleGroup,pool.DrawId,(Action)(()=>{if(capturedUi!=null)((FieldInfo)Member("BusyPreview")).SetValue(capturedUi,false);}));}
            catch{networkInFlight=false;if(capturedUi!=null)((FieldInfo)Member("BusyPreview")).SetValue(capturedUi,false);throw;}
        }
        private void Tick()
        {
            long now=DateTime.UtcNow.Ticks;if(now<readAt)return;readAt=now+TimeSpan.FromMilliseconds(100).Ticks;
            if(probeOnly){ProbeTick(now);return;}
            Snapshot snapshot;
            if(publication.Blocked){if(publication.Retry(s=>Storage.Write("snapshot.json",s)))Storage.Event("publication_recovered","状态保存已恢复");return;}
            try{snapshot=Read(now);readFailedAt=0;}
            catch(Exception e){if(readFailedAt==0){readFailedAt=now;Storage.Event("read_retry",e.GetBaseException().Message);}if(now-readFailedAt<TimeSpan.FromSeconds(15).Ticks)return;machine.Halt("error","读取游戏状态连续失败："+e.GetBaseException().Message);snapshot=new Snapshot{ProcessId=pid,ProcessStart=start,At=now,State="error",Message=machine.Message,Owner=machine.Owner};Publish(snapshot);return;}
            try{var command=Storage.Read<Control>("control.json")??lastControl;lastControl=command;
                ReconcileNetwork(snapshot,command,now);bool draw=machine.Step(command,snapshot,Interlocked.Read(ref response),Interlocked.Read(ref result),responseError,now);
                if(draw)Draw(snapshot.Pools.Single(p=>p.Id==snapshot.PoolId));
                else if(machine.SkipAnimation&&now>=nextSkip){var skip=Get("SkipButton",ui) as GameObject;if(skip!=null&&skip.activeInHierarchy){ui.OnClickUI(skip);skipCount++;nextSkip=now+TimeSpan.FromMilliseconds(200).Ticks;snapshot.SkipCount=skipCount;}}
                if(machine.Owner!=""){snapshot.State=machine.State;snapshot.Message=machine.Message;}
                snapshot.Owner=machine.Owner;snapshot.Rolls=machine.Rolls;snapshot.A=machine.A;snapshot.B=machine.B;
            }catch(Exception e){machine.Halt("error",e.GetBaseException().Message);snapshot=new Snapshot{ProcessId=pid,ProcessStart=start,At=now,Instance=instance,Sequence=++sequence,State="error",Message=e.GetBaseException().Message,Owner=machine.Owner};}
            Publish(snapshot);
        }
        private void Publish(Snapshot snapshot)
        {
            bool changed=snapshot.State!=loggedState||snapshot.Message!=loggedMessage||snapshot.ResultKey!=publishedKey||snapshot.SkipCount!=publishedSkips;
            if(snapshot.State!=loggedState||snapshot.Message!=loggedMessage){Storage.Event(snapshot.State,snapshot.Message,snapshot);loggedState=snapshot.State;loggedMessage=snapshot.Message;}
            if(!changed&&snapshot.At-publishedAt<TimeSpan.FromMilliseconds(500).Ticks)return;publishedAt=snapshot.At;publishedKey=snapshot.ResultKey;publishedSkips=snapshot.SkipCount;
            if(!publication.Publish(snapshot,s=>Storage.Write("snapshot.json",s)))Storage.Event("publication_retry",publication.Error);
        }
        internal void PrepareHandoff(){lastControl=new Control();machine.Halt("stopped","component-handoff");}
        internal string HandoffBusy(){if(networkInFlight&&DateTime.UtcNow.Ticks-sentTicks>=TimeSpan.FromSeconds(30).Ticks&&network.Idle){networkInFlight=false;needsRecovery=true;if(ownedBusyUi!=null)((FieldInfo)Member("BusyPreview")).SetValue(ownedBusyUi,false);ownedBusyUi=null;}return networkInFlight||refreshSent>0&&!network.Idle?"gacha native request awaiting completion":"";}
        internal void Stop(){active=null;network.Dispose();machine.Halt("stopped","组件已停止");heartbeat?.Dispose();heartbeat=null;if(harmony!=null){foreach(string role in new[]{"Frame","Response","SetResult","RefreshResponse"})harmony.Unpatch((MethodInfo)Member(role),HarmonyPatchType.All,harmony.Id);harmony=null;}}
    }
}

