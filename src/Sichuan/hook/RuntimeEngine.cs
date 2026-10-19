using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using Google.Protobuf;
namespace BD2Sichuan.Runtime
{
    internal sealed class RuntimeEngine
    {
        private SichuanCapture capture;private SichuanNetworkTrace network;private Timer heartbeat;
        internal static MethodInfo Pump()=>typeof(GameCameraManager).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic,null,Type.EmptyTypes,null)??throw new MissingMethodException("GameCameraManager.LateUpdate");
        internal static MethodInfo Send()=>typeof(BDNetwork.NetworkManager).GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Single(m=>m.Name=="Send"&&m.ReturnType==typeof(void)&&m.GetParameters().Length==6&&m.GetParameters()[0].ParameterType==typeof(IMessage));
        internal void Start()
        {
            if(capture!=null)return;
            SichuanBindings.Validate();
            var pump=Pump();var send=Send();SichuanNetworkTrace.ResolveResponseHandlers();
            network=new SichuanNetworkTrace();network.Start(send);
            capture=new SichuanCapture();capture.Start(pump);
            heartbeat=new Timer(_=>Loader.WriteStatus("active",""),null,0,1000);
        }
        internal void PrepareHandoff(){capture?.PrepareHandoff();}
        internal string HandoffBusy(){network?.Reconcile();return capture!=null&&capture.Writing?"snapshot writer":network!=null&&network.Waiting?"pending game response":"";}
        internal void Stop(){heartbeat?.Dispose();heartbeat=null;capture?.Dispose();capture=null;network?.Dispose();network=null;}
    }
}
