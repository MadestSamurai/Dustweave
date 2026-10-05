using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Threading;
using HarmonyLib;
using Proto.Net;
namespace BD2Sichuan.Runtime
{
    // Observes the existing client requests and handlers without replacing callbacks or responses.
    internal sealed class SichuanNetworkTrace:IDisposable
    {
        private static SichuanNetworkTrace current;private readonly BD2.LocalIpc.NativeNetworkWatch network=new BD2.LocalIpc.NativeNetworkWatch();private bool nativeIdle,observedSend;
        private readonly Harmony harmony=new Harmony("bd2.standalone.sichuan.network-trace");
        private readonly object sync=new object();
        private readonly Queue<SichuanNetworkEvent> events=new Queue<SichuanNetworkEvent>();
        private readonly Dictionary<MethodBase,string> handlers=new Dictionary<MethodBase,string>();
        private readonly Dictionary<string,DateTime> awaiting=new Dictionary<string,DateTime>();
        private Timer writer;private int writing;private bool disposed;
        private string error="";
        internal bool Waiting {get{lock(sync)return awaiting.Count>0||observedSend&&!nativeIdle;}} internal void Reconcile(){bool idle=network.Idle;lock(sync)nativeIdle=idle;}
        internal string LastState {get;private set;}="尚未观察到连连看请求";
        internal static void ReconcileCurrent(){current?.Reconcile();}
        internal void Start(MethodInfo send)
        {
            current=this;network.Start(typeof(BDNetwork.NetworkManager),"bd2.sichuan.network-watch");
            try
            {
                foreach(var pair in ResolveResponseHandlers())
                {handlers[pair.Key]=pair.Value;harmony.Patch(pair.Key,postfix:new HarmonyMethod(typeof(SichuanNetworkTrace),nameof(Response)));}
                harmony.Patch(send,prefix:new HarmonyMethod(typeof(SichuanNetworkTrace),nameof(Send)));
            }
            catch(Exception e){error=e.Message;LocalStorage.Log("Sichuan network diagnostics unavailable: "+error);harmony.UnpatchAll("bd2.standalone.sichuan.network-trace");}
            writer=new Timer(_=>Flush(),null,0,250);
        }
        internal static Dictionary<MethodBase,string> ResolveResponseHandlers()
        {
            var result=new Dictionary<MethodBase,string>();
                foreach(var type in new[]{typeof(MiniGameSichuanStartResponse),typeof(MiniGameSichuanEndResponse)})
                {

                    var methods=SichuanBindings.Type("NetworkHelper").GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic)
                        .SelectMany(t=>t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly))
                        .Where(m=>m.ReturnType==typeof(bool)&&m.GetParameters().Select(p=>p.ParameterType).SequenceEqual(new[]{typeof(byte[]),typeof(int),typeof(int)}))
                        .Where(m=>SichuanIl.CallsParser(m,type)).ToArray();
                    if(methods.Length!=1)throw new InvalidOperationException(type.Name+" handler count="+methods.Length);
                    result[methods[0]]=type.Name.Replace("Response","");
                }
            return result;
        }
        internal static string NetworkState=>current==null?"未连接":current.error.Length>0?"结算诊断不可用："+current.error:current.LastState;
        private static void Send(object __0)
        {
            try
            {
                var c=current;if(c==null)return;
                var start=__0 as MiniGameSichuanStartRequest;var end=__0 as MiniGameSichuanEndRequest;
                if(start==null&&end==null)return;
                string kind=start!=null?"MiniGameSichuanStart":"MiniGameSichuanEnd";
                lock(c.sync)
                {
                    c.observedSend=true;c.nativeIdle=false;c.awaiting[kind]=DateTime.UtcNow;c.LastState=kind+" 已发送，等待服务器";
                    c.events.Enqueue(new SichuanNetworkEvent{AtUtc=DateTime.UtcNow.ToString("O"),Kind="request",Message=kind,
                        Count=start!=null?start.BlockInfo.Count:end.TurnInfo.Count,ForceEnd=end!=null&&end.IsForceEnd});
                }
            }catch{}
        }
        private static void Response(byte[] __0,int __1,int __2,bool __result,MethodBase __originalMethod)
        {
            try
            {
                var c=current;if(c==null||!c.handlers.TryGetValue(__originalMethod,out var kind))return;
                lock(c.sync)
                {
                    c.awaiting.Remove(kind);c.LastState=kind+(__2==0&&__result?" 服务器回调成功":" 返回异常")+" / error="+__2;
                    c.events.Enqueue(new SichuanNetworkEvent{AtUtc=DateTime.UtcNow.ToString("O"),Kind="response",Message=kind,PacketCode=__1,ErrorType=__2,Accepted=__result,Bytes=__0?.Length??0});
                }
            }catch{}
        }
        private void Flush()
        {
            if(Interlocked.Exchange(ref writing,1)!=0)return;
            try
            {
                if(disposed)return;SichuanNetworkEvent[] list;
                lock(sync)
                {
                    foreach(var pair in awaiting.ToArray())if(nativeIdle&&DateTime.UtcNow-pair.Value>TimeSpan.FromSeconds(BD2.LocalIpc.RequestObservation.TimeoutSeconds))
                    {awaiting.Remove(pair.Key);LastState=pair.Key+" 超过 30 秒未观察到响应";events.Enqueue(new SichuanNetworkEvent{AtUtc=DateTime.UtcNow.ToString("O"),Kind="response_timeout",Message=pair.Key});}
                    list=events.ToArray();events.Clear();
                }
                var root=Path.Combine(LocalStorage.DataRoot,"sichuan");
                if(list.Length>0)
                {
                    Directory.CreateDirectory(root);var path=Path.Combine(root,"network.jsonl");
                    if(File.Exists(path)&&new FileInfo(path).Length>1000000){var old=Path.Combine(root,"network.previous.jsonl");if(File.Exists(old))File.Delete(old);File.Move(path,old);}
                    using(var f=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read))foreach(var item in list)
                    {using(var buffer=new MemoryStream()){new DataContractJsonSerializer(typeof(SichuanNetworkEvent)).WriteObject(buffer,item);var bytes=buffer.ToArray();f.Write(bytes,0,bytes.Length);f.WriteByte(10);}}
                    LocalStorage.WriteJsonAtomically(Path.Combine(root,"network-status.json"),list.Last());
                }
            }
            catch(Exception e){LocalStorage.Log("Sichuan network trace write: "+e.Message);}
            finally{Interlocked.Exchange(ref writing,0);}
        }
        public void Dispose(){disposed=true;current=null;network.Dispose();writer?.Dispose();harmony.UnpatchAll("bd2.standalone.sichuan.network-trace");}
    }
    public sealed class SichuanNetworkEvent
    {
        public string AtUtc {get;set;}="";
        public string Kind {get;set;}="";
        public string Message {get;set;}="";
        public int Count {get;set;}
        public bool ForceEnd {get;set;}
        public int PacketCode {get;set;}
        public int ErrorType {get;set;}
        public bool Accepted {get;set;}
        public int Bytes {get;set;}
        public string Correlation {get;set;}="message_type_temporal_only";
    }
}
