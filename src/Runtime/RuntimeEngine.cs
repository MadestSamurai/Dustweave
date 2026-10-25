using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Neo.Unity.Neon;
using Proto.Net;
using UnityEngine.SceneManagement;
namespace BD2Daily.Runtime
{
    internal sealed class RuntimeEngine
    {
        private static RuntimeEngine active;private Harmony harmony;private Timer heartbeat;private MethodInfo pump;private PropertyInfo player;
        private GuildRuntime guild;private StartupRuntime startup;private bool ticking;
        private long nextRead;private long sequence;private readonly string instance=Guid.NewGuid().ToString("N");private int pid;private long start;
        private double maxTotal,maxIdentity,maxControl,maxStartup,maxGuild,maxWrite;
        private readonly object healthSync=new object();private string healthState="active",healthError="",frameStage="waiting_frame";private bool stopped;
        private void Health(string state,string error){lock(healthSync){healthState=state;healthError=error;}}
        private void Heartbeat(){lock(healthSync){if(!stopped)Loader.WriteStatus(healthState,healthError);}}
        internal void Start()
        {
            if(harmony!=null)return;SuiteRuntime.Start();
            var game=typeof(UserDBInfo).Assembly;
            player=game.GetType(ClientNames.DataType,true).GetProperty(ClientNames.DataProperty,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
            pump=game.GetType(ClientNames.PumpType,true).GetMethod(ClientNames.PumpMethod,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,Type.EmptyTypes,null);
            if(player==null||pump==null)throw new MissingMemberException("Daily identity interface missing");
            using(var p=Process.GetCurrentProcess()){pid=p.Id;start=p.StartTime.ToUniversalTime().Ticks;}
            BD2.LocalIpc.LoadingTrace.Start();try{LoadingTraceHooks.Start();}catch(Exception e){BD2.LocalIpc.LoadingTrace.Note("hook-setup-failed",e.GetType().Name);}
            active=this;harmony=new Harmony("bd2.daily.identity.v1");harmony.Patch(pump,postfix:new HarmonyMethod(typeof(RuntimeEngine).GetMethod("AfterFrame",BindingFlags.NonPublic|BindingFlags.Static)));
            guild=new GuildRuntime();guild.Start();startup=new StartupRuntime();
            UnityEngine.Canvas.willRenderCanvases+=AfterFrame;
            heartbeat=new Timer(_=>Heartbeat(),null,0,1000);
        }
        private static void AfterFrame(){var a=active;if(a==null||a.ticking||BD2.LocalIpc.LoadingTrace.Paused)return;a.ticking=true;long trace=BD2.LocalIpc.LoadingTrace.Begin("common.frame");try{a.Tick();}catch(Exception ex){a.Health("error",a.frameStage+": "+ex.GetBaseException().GetType().Name+": "+ex.GetBaseException().Message);a.Heartbeat();}finally{BD2.LocalIpc.LoadingTrace.End("common.frame",trace);a.ticking=false;}}
        private void Tick()
        {
            var now=DateTime.UtcNow.Ticks;if(now<nextRead)return;nextRead=now+TimeSpan.FromMilliseconds(500).Ticks;
            var watch=Stopwatch.StartNew();
            var snapshot=new DailySnapshot{ProcessId=pid,ProcessStartTicks=start,InstanceId=instance,Sequence=++sequence,FrameUtcTicks=now};
            frameStage="identity-read";
            try{
                snapshot.Scene=SceneManager.GetActiveScene().name??"";
                string accountKey;
                if(!SdkIdentity.TryRead(out accountKey))snapshot.State="waiting_sdk";
                else {
                snapshot.AccountKey=accountKey;
                var user=(UserDBInfo)player.GetValue(null,null);
                if(user!=null&&user.OwnerIndex>0&&!string.IsNullOrEmpty(snapshot.AccountKey)){
                    snapshot.PlayerKey=DailyIdentity.PlayerKey(snapshot.AccountKey,user.OwnerIndex);snapshot.PlayerName=user.UserId??"";snapshot.State="identified";
                }else snapshot.State=string.IsNullOrEmpty(snapshot.AccountKey)?"waiting_login":"waiting_player";
                var lease=LocalStorage.Read<DailyLease>("lease.json");if(lease!=null&&lease.Matches(snapshot,now))snapshot.LeaseOwner=lease.Owner;}
            }catch(Exception ex){snapshot.State="read_error";snapshot.ErrorCode=ex.GetBaseException().GetType().Name;}
            double mark=watch.Elapsed.TotalMilliseconds;maxIdentity=Math.Max(maxIdentity,mark);
            double control=watch.Elapsed.TotalMilliseconds;
            frameStage="startup-observation";
            try{if(startup!=null)startup.Tick(snapshot,!SuiteRuntime.ToolActive);}catch(Exception ex){
                if(snapshot.Startup==null)snapshot.Startup=new StartupObservation();
                snapshot.Startup.Ready=false;snapshot.Startup.ClickEnabled=false;snapshot.Startup.DownloadReady=false;
                snapshot.Startup.BlockReason="启动页读取／操作异常："+ex.GetBaseException().Message;
            }
            double title=watch.Elapsed.TotalMilliseconds;maxStartup=Math.Max(maxStartup,title-control);
            try{if(guild!=null&&snapshot.State!="waiting_sdk"&&snapshot.State!="read_error")guild.Tick(snapshot,!SuiteRuntime.ToolActive);}catch(Exception ex){snapshot.Guild=new GuildObservation{Error=ex.GetBaseException().Message};}
            double observed=watch.Elapsed.TotalMilliseconds;maxGuild=Math.Max(maxGuild,observed-title);
            snapshot.ObserverMaxMs=Math.Max(maxTotal,observed);snapshot.ObserverIdentityMs=maxIdentity;snapshot.ObserverControlMs=maxControl;
            snapshot.ObserverStartupMs=maxStartup;snapshot.ObserverGuildMs=maxGuild;snapshot.ObserverWriteMs=maxWrite;
            // Publish identity after observing loading/title state, but before
            // unrelated module ownership/status callbacks. A failed module must
            // not starve the shared identity mailbox for every account/tool.
            frameStage="identity-publish";LocalStorage.Write("snapshot.json",snapshot);
            maxWrite=Math.Max(maxWrite,watch.Elapsed.TotalMilliseconds-observed);
            double moduleStart=watch.Elapsed.TotalMilliseconds;string moduleError="";
            frameStage="module-control";
            try{SuiteRuntime.Tick(snapshot);}catch(Exception ex){moduleError="module-control: "+ex.GetBaseException().GetType().Name+": "+ex.GetBaseException().Message;}
            maxControl=Math.Max(maxControl,watch.Elapsed.TotalMilliseconds-moduleStart);
            Health("active",moduleError);
            BD2.LocalIpc.LoadingTrace.Note("scene",snapshot.Scene+";loading="+snapshot.Loading+";state="+snapshot.State+";frame="+UnityEngine.Time.frameCount+";focused="+UnityEngine.Application.isFocused+";background="+UnityEngine.Application.runInBackground+";scale="+UnityEngine.Time.timeScale);
            LoadingTraceHooks.Observe();
            maxTotal=Math.Max(maxTotal,watch.Elapsed.TotalMilliseconds);
        }
        internal void PrepareHandoff(){SuiteRuntime.Stop();}
        internal string HandoffBusy(){return SuiteRuntime.Busy()!=""?SuiteRuntime.Busy():ticking?"observer frame":guild!=null&&guild.HandoffBusy?"guild attendance response":"";}
        internal void Stop(){lock(healthSync){stopped=true;}LoadingTraceHooks.Stop();SuiteRuntime.Dispose();UnityEngine.Canvas.willRenderCanvases-=AfterFrame;active=null;startup=null;guild?.Stop();guild=null;heartbeat?.Dispose();heartbeat=null;if(harmony!=null&&pump!=null)harmony.Unpatch(pump,HarmonyPatchType.All,harmony.Id);harmony=null;}
    }
}


