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
        internal void Start()
        {
            if(harmony!=null)return;SuiteRuntime.Start();
            var game=typeof(UserDBInfo).Assembly;
            player=game.GetType(ClientNames.DataType,true).GetProperty(ClientNames.DataProperty,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
            pump=game.GetType(ClientNames.PumpType,true).GetMethod(ClientNames.PumpMethod,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,Type.EmptyTypes,null);
            if(player==null||pump==null)throw new MissingMemberException("Daily identity interface missing");
            using(var p=Process.GetCurrentProcess()){pid=p.Id;start=p.StartTime.ToUniversalTime().Ticks;}
            active=this;harmony=new Harmony("bd2.daily.identity.v1");harmony.Patch(pump,postfix:new HarmonyMethod(typeof(RuntimeEngine).GetMethod("AfterFrame",BindingFlags.NonPublic|BindingFlags.Static)));
            guild=new GuildRuntime();guild.Start();startup=new StartupRuntime();
            UnityEngine.Canvas.willRenderCanvases+=AfterFrame;
            heartbeat=new Timer(_=>Loader.WriteStatus("active",""),null,0,1000);
        }
        private static void AfterFrame(){var a=active;if(a==null||a.ticking)return;a.ticking=true;try{a.Tick();}catch(Exception ex){Loader.WriteStatus("error",ex.GetBaseException().Message);}finally{a.ticking=false;}}
        private void Tick()
        {
            var now=DateTime.UtcNow.Ticks;if(now<nextRead)return;nextRead=now+TimeSpan.FromMilliseconds(500).Ticks;
            var snapshot=new DailySnapshot{ProcessId=pid,ProcessStartTicks=start,InstanceId=instance,Sequence=++sequence,FrameUtcTicks=now};
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
            SuiteRuntime.Tick(snapshot);try{if(startup!=null&&!SuiteRuntime.ToolActive)startup.Tick(snapshot);}catch(Exception ex){snapshot.Startup=new StartupObservation{BlockReason=ex.GetBaseException().Message};}
            try{if(guild!=null&&snapshot.State!="waiting_sdk"&&snapshot.State!="read_error")guild.Tick(snapshot,!SuiteRuntime.ToolActive);}catch(Exception ex){snapshot.Guild=new GuildObservation{Error=ex.GetBaseException().Message};}
            try{LocalStorage.Write("snapshot.json",snapshot);}catch{}
        }
        internal void PrepareHandoff(){SuiteRuntime.Stop();}
        internal string HandoffBusy(){return SuiteRuntime.Busy()!=""?SuiteRuntime.Busy():ticking?"observer frame":guild!=null&&guild.HandoffBusy?"guild attendance response":"";}
        internal void Stop(){SuiteRuntime.Dispose();UnityEngine.Canvas.willRenderCanvases-=AfterFrame;active=null;startup=null;guild?.Stop();guild=null;heartbeat?.Dispose();heartbeat=null;if(harmony!=null&&pump!=null)harmony.Unpatch(pump,HarmonyPatchType.All,harmony.Id);harmony=null;}
    }
}


