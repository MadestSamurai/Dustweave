using System;
using System.IO;
using System.Reflection;
using BD2.LocalIpc;
namespace BD2Daily.Runtime
{
    public static class Loader
    {
        private static object engine;
        private static bool resolver;
        private static Assembly harmonyAssembly;
        private static Handoff handoff;
        private static MainThread frame;
        public static void Load()
        {
            lock(typeof(Loader))
            {
                // Resolve before scheduling callbacks: Mono may bind field types before Start runs.
                if(!resolver){AppDomain.CurrentDomain.AssemblyResolve+=Resolve;resolver=true;}
                LoadHarmony();
                if(handoff==null)handoff=new Handoff(typeof(Loader).Assembly.FullName,"daily-observer","daily",false,Start,Pause,Busy,Stop,WriteStatus);
                if(!handoff.IsActive&&!handoff.Pending)RuntimeFiles.Start(LocalStorage.Root,Build.Fingerprint,DailyIdentity.LiveEntries);
                handoff.Request(DateTime.UtcNow);
                if(frame==null)frame=new MainThread(()=>{LegacyPilots.Discover();handoff.Tick(DateTime.UtcNow);return handoff.Pending;},handoff.Fail);
                frame.Schedule();
            }
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Start()
        {
            RuntimeFiles.Start(LocalStorage.Root,Build.Fingerprint,DailyIdentity.LiveEntries);
            if(!resolver){AppDomain.CurrentDomain.AssemblyResolve+=Resolve;resolver=true;}
            LiveStore.Handles=RuntimeFiles.Handles;LiveStore.Read=RuntimeFiles.Read;LiveStore.Write=(p,b,c)=>RuntimeFiles.Write(p,b);
            engine=Activator.CreateInstance(typeof(Loader).Assembly.GetType("BD2Daily.Runtime.RuntimeEngine",true),true);Invoke("Start");
        }
        private static void Pause(){RuntimeFiles.Revoke();if(engine!=null)Invoke("PrepareHandoff");}
        private static string Busy(){return engine==null?"":(string)Invoke("HandoffBusy");}
        private static void Stop()
        {
            if(engine!=null)Invoke("Stop");engine=null;
            if(resolver){AppDomain.CurrentDomain.AssemblyResolve-=Resolve;resolver=false;}
        }
        private static object Invoke(string method){return engine.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(engine,null);}
        public static void Unload()
        {
            MainThread.Drain(()=>{if(handoff!=null)handoff.Unload();},WriteStatus);
        }
        internal static void WriteStatus(string state,string error)
        {if(state=="active"&&handoff!=null&&handoff.IsActive)RuntimeFiles.Activate();try{using(var p=System.Diagnostics.Process.GetCurrentProcess())LocalStorage.Write("runtime.json",new DailyRuntimeStatus{State=state,Error=error,ProcessId=p.Id,ProcessStartTicks=p.StartTime.ToUniversalTime().Ticks,AtUtcTicks=DateTime.UtcNow.Ticks});}catch{}}
        private static Assembly Resolve(object sender,ResolveEventArgs args)
        {return new AssemblyName(args.Name).Name=="0Harmony"?LoadHarmony():null;}
        private static Assembly LoadHarmony()
        {
            if(harmonyAssembly!=null)return harmonyAssembly;
            using(var s=typeof(Loader).Assembly.GetManifestResourceStream("BD2Daily.Harmony.dll"))
            using(var b=new MemoryStream()){
                if(s==null)throw new InvalidDataException("日常连接组件缺少内置依赖 BD2Daily.Harmony.dll，请更新完整工具包。");
                s.CopyTo(b);return harmonyAssembly=Assembly.Load(b.ToArray());
            }
        }
    }
}

