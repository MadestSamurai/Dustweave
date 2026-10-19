using System;
using System.IO;
using System.Reflection;
using BD2.LocalIpc;
namespace BD2Rhythm.Runtime
{
    public static class Loader
    {
        private static object engine;
        private static bool resolver;
        private static Handoff handoff;
        private static MainThread frame;
        public static void Load()
        {
            lock(typeof(Loader))
            {
                if(handoff==null)handoff=new Handoff(typeof(Loader).Assembly.FullName,"rhythm","rhythm",false,Start,Pause,Busy,Stop,Status);
                if(!handoff.IsActive&&!handoff.Pending)RuntimeFiles.Start(LocalStorage.DataRoot,Build.Fingerprint,RhythmIdentity.LiveEntries);
                handoff.Request(DateTime.UtcNow);
                if(frame==null)frame=new MainThread(()=>{LegacyPilots.Discover();handoff.Tick(DateTime.UtcNow);return handoff.Pending;},handoff.Fail);
                frame.Schedule();
            }
        }
        private static void Start()
        {
            RuntimeFiles.Start(LocalStorage.DataRoot,Build.Fingerprint,RhythmIdentity.LiveEntries);
            if(!resolver){AppDomain.CurrentDomain.AssemblyResolve+=Resolve;resolver=true;}
            engine=Activator.CreateInstance(typeof(Loader).Assembly.GetType("BD2Rhythm.Runtime.RuntimeEngine",true),true);Invoke("Start");
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
            MainThread.Drain(()=>{if(handoff!=null)handoff.Unload();},Status);
        }
        internal static void Status(string state,string error)
        {if(state=="active"&&handoff!=null&&handoff.IsActive)RuntimeFiles.Activate();try{LocalStorage.WriteJsonAtomically(Path.Combine(LocalStorage.DataRoot,"runtime.json"),new RhythmRuntimeStatus{State=state,Error=error,Runtime=RhythmIdentity.RuntimeName,AtUtc=DateTime.UtcNow.ToString("O"),ProcessId=System.Diagnostics.Process.GetCurrentProcess().Id});}catch(Exception e){LocalStorage.Log(e.Message);}}
        private static Assembly Resolve(object sender,ResolveEventArgs args)
        {if(new AssemblyName(args.Name).Name!="0Harmony")return null;using(var s=typeof(Loader).Assembly.GetManifestResourceStream("BD2Rhythm.Harmony.dll"))using(var b=new MemoryStream()){s.CopyTo(b);return Assembly.Load(b.ToArray());}}
    }
}
