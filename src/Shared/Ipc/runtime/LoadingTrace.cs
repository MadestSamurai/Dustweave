using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace BD2.LocalIpc
{
    // Explicit diagnostic sessions only. No Unity access, payloads or synchronous
    // disk writes on game callbacks. The host mirrors the sandbox spool without
    // the normal export budget; neither side rotates or deletes these records.
    public static class LoadingTrace
    {
        const string Key="Dustweave.LoadingTrace.v1";
        static Action<string,string,long> sink;
        static Func<bool> hold;
        static Func<bool> active;
        static bool initialized;
        [ThreadStatic] static bool reportingException;
        public static string ControlPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Dustweave-loading-experiment","active.json"); } }
        [DataContract] public sealed class Control
        {
            [DataMember] public string Id="",Mode="",Output="";
            [DataMember] public long UntilUtcTicks,PauseUntilUtcTicks;
            [DataMember] public bool DeepHooks;
        }
        [DataContract] sealed class Row
        {
            [DataMember] public long Utc,Stamp,Started;
            [DataMember] public int Thread;
            [DataMember] public uint NativeThread;
            [DataMember] public string Kind,Name;
        }
        public static void Start()
        {
            if(initialized)return;initialized=true;
            try { lock(AppDomain.CurrentDomain) {
                sink=AppDomain.CurrentDomain.GetData(Key) as Action<string,string,long>;
                hold=AppDomain.CurrentDomain.GetData(Key+".hold") as Func<bool>;
                active=AppDomain.CurrentDomain.GetData(Key+".active") as Func<bool>;
                if(sink!=null)return;
                var control=ReadControl();if(control==null)return;
                var recorder=new Recorder(control);sink=recorder.Add;hold=()=>recorder.Paused;active=()=>recorder.Active;
                AppDomain.CurrentDomain.SetData(Key,sink);AppDomain.CurrentDomain.SetData(Key+".hold",hold);AppDomain.CurrentDomain.SetData(Key+".active",active);
                recorder.Start();
                foreach(var a in AppDomain.CurrentDomain.GetAssemblies())AssemblyInfo(a);
                AppDomain.CurrentDomain.AssemblyLoad+=(sender,args)=>AssemblyInfo(args.LoadedAssembly);
                AppDomain.CurrentDomain.FirstChanceException+=(sender,args)=>{
                    if(reportingException||!control.DeepHooks||!Enabled)return;
                    var e=args.Exception;
                    if(!(e is IOException||e is TimeoutException||e is System.Net.Sockets.SocketException||e is UnauthorizedAccessException))return;
                    try{reportingException=true;Note("first-chance-wait-io",e.GetType().FullName+";hresult="+e.HResult+";stack="+new StackTrace(1,false));}finally{reportingException=false;}
                };
            }}catch { sink=null;hold=null;active=null; }
        }
        static void AssemblyInfo(System.Reflection.Assembly assembly){if(!Enabled)return;try{Note("assembly-loaded",assembly.FullName+";mvid="+assembly.ManifestModule.ModuleVersionId+";path="+assembly.Location);}catch{}}
        public static Control ReadControl()
        {
            string failure;var c=ReadControlFile(out failure);long now=DateTime.UtcNow.Ticks;
            return c!=null&&c.UntilUtcTicks>now&&c.UntilUtcTicks<now+TimeSpan.FromSeconds(45).Ticks?c:null;
        }
        static Control ReadControlFile(out string failure)
        {
            failure="";
            try { using(var f=new FileStream(ControlPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                // Only fixed scalar opt-in fields are needed. Avoid initializing
                // Mono's configuration/serialization system while diagnosing startup.
                string text;using(var reader=new StreamReader(f))text=reader.ReadToEnd();
                var c=new Control{Id=Scalar(text,"Id"),Mode=Scalar(text,"Mode"),DeepHooks=Scalar(text,"DeepHooks")=="true"};
                if(!long.TryParse(Scalar(text,"UntilUtcTicks"),out c.UntilUtcTicks)||!long.TryParse(Scalar(text,"PauseUntilUtcTicks"),out c.PauseUntilUtcTicks)){failure="invalid lease fields";return null;}
                Guid id;
                if(!Guid.TryParseExact(c.Id,"N",out id)){failure="invalid session id";return null;}
                return c;
            }}catch(Exception e){failure=e.GetType().Name+";hresult="+e.HResult;return null;}
        }
        static string Scalar(string json,string key){var m=System.Text.RegularExpressions.Regex.Match(json,"\""+key+"\"\\s*:\\s*(?:\"([a-zA-Z0-9-]*)\"|([0-9]+|true|false))");return m.Success?(m.Groups[1].Success?m.Groups[1].Value:m.Groups[2].Value):"";}
        static string Quote(string text){var b=new StringBuilder("\"");foreach(char c in text){if(c=='"'||c=='\\')b.Append('\\').Append(c);else if(c<32)b.Append("\\u").Append(((int)c).ToString("x4"));else b.Append(c);}return b.Append('"').ToString();}
        public static bool Enabled { get { return sink!=null&&active!=null&&active(); } }
        public static bool Paused { get { return hold!=null&&hold(); } }
        public static long Begin(string name) { if(!Enabled)return 0;long at=Stopwatch.GetTimestamp();sink("enter",name,at);return at; }
        public static void End(string name,long at) { if(at!=0&&sink!=null)sink("leave",name,at); }
        public static void Note(string kind,string name) { if(Enabled)sink(kind,name,0); }
        sealed class Recorder
        {
            readonly ConcurrentQueue<Row> rows=new ConcurrentQueue<Row>();
            readonly Control initial;
            volatile bool enabled=true;
            Thread writer;
            long pauseUntil,dropped;int queued;
            string stopReason="process-exit";
            public bool Active { get { return enabled; } }
            public bool Paused { get { return enabled&&DateTime.UtcNow.Ticks<Interlocked.Read(ref pauseUntil); } }
            public Recorder(Control c){initial=c;}
            public void Start(){writer=new Thread(Write){IsBackground=true,Name="Dustweave loading trace"};AppDomain.CurrentDomain.ProcessExit+=OnExit;writer.Start();}
            void OnExit(object sender,EventArgs e){enabled=false;if(writer!=null&&writer!=Thread.CurrentThread)writer.Join(2000);}
            public void Add(string kind,string name,long started)
            {
                if(!enabled)return;
                // RAM backpressure is reported explicitly; storage has no quota.
                if(Interlocked.Increment(ref queued)>65536){Interlocked.Decrement(ref queued);Interlocked.Increment(ref dropped);return;}
                rows.Enqueue(new Row{Utc=DateTime.UtcNow.Ticks,Stamp=Stopwatch.GetTimestamp(),Started=started,Thread=Thread.CurrentThread.ManagedThreadId,NativeThread=GetCurrentThreadId(),Kind=kind,Name=name});
            }
            void Write()
            {
                string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2DailyAssistant","loading-experiment",initial.Id);
                string path="";
                try {
                    Directory.CreateDirectory(directory);
                    using(var p=Process.GetCurrentProcess())path=Path.Combine(directory,"runtime-"+p.Id+"-"+p.StartTime.ToUniversalTime().Ticks+".jsonl");
                    using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete))
                    using(var w=new StreamWriter(f,new UTF8Encoding(false))) {
                        long refresh=0,leaseUntil=initial.UntilUtcTicks,readFailures=0;
                        Add("clock","frequency="+Stopwatch.Frequency,0);
                        while(enabled||!rows.IsEmpty) {
                            var now=DateTime.UtcNow.Ticks;
                            if(enabled&&now>=refresh){
                                string failure;var c=ReadControlFile(out failure);
                                if(c!=null){
                                    if(c.Id!=initial.Id||c.UntilUtcTicks<=now||c.UntilUtcTicks>=now+TimeSpan.FromSeconds(45).Ticks){
                                        stopReason=c.Id!=initial.Id?"session-replaced":"lease-ended";
                                        Add("trace-stop",stopReason,0);enabled=false;
                                    }else{
                                        leaseUntil=c.UntilUtcTicks;
                                        if(readFailures>0){Add("control-read-recovered","failures="+readFailures,0);readFailures=0;}
                                        Interlocked.Exchange(ref pauseUntil,c.PauseUntilUtcTicks<now+TimeSpan.FromSeconds(35).Ticks?c.PauseUntilUtcTicks:0);
                                    }
                                }else{
                                    // Atomic replacement, antivirus or sandbox sharing can cause
                                    // a transient read failure. Honor the last granted lease;
                                    // one missed refresh must not permanently kill this recorder.
                                    readFailures++;Add("control-read-gap",failure+";failures="+readFailures+";leaseUntil="+leaseUntil,0);
                                    if(now>=leaseUntil){stopReason="lease-expired-after-read-gap";Add("trace-stop",stopReason,0);enabled=false;}
                                }
                                if(!enabled)Interlocked.Exchange(ref pauseUntil,0);
                                int worker,io;ThreadPool.GetAvailableThreads(out worker,out io);
                                Add("heartbeat","workers="+worker+";io="+io+";memory="+GC.GetTotalMemory(false)+";gc="+GC.CollectionCount(0)+","+GC.CollectionCount(1)+","+GC.CollectionCount(2)+";dropped="+Interlocked.Read(ref dropped),0);
                                refresh=now+TimeSpan.FromSeconds(1).Ticks;
                            }
                            Row row;int written=0;
                            while(written++<4096&&rows.TryDequeue(out row)){Interlocked.Decrement(ref queued);w.WriteLine("{\"Utc\":"+row.Utc+",\"Stamp\":"+row.Stamp+",\"Started\":"+row.Started+",\"Thread\":"+row.Thread+",\"NativeThread\":"+row.NativeThread+",\"Kind\":"+Quote(row.Kind)+",\"Name\":"+Quote(row.Name)+"}");}
                            w.Flush();Thread.Sleep(100);
                        }
                        w.WriteLine("{\"Utc\":"+DateTime.UtcNow.Ticks+",\"Kind\":\"closed\",\"Reason\":"+Quote(stopReason)+",\"Dropped\":"+Interlocked.Read(ref dropped)+"}");w.Flush();
                    }
                }catch(Exception e){enabled=false;try{File.WriteAllText(Path.Combine(directory,"capture-error.txt"),DateTime.UtcNow.ToString("O")+" "+e.GetType().Name+" "+e.HResult);}catch{}}
                finally{enabled=false;Interlocked.Exchange(ref pauseUntil,0);}
            }
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
    }
}
