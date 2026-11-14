using System;
using System.Reflection;
using System.Threading;

namespace BD2.LocalIpc
{
    // Reflection avoids coupling the common lifecycle to a particular Unity build.
    // The subscribed delegate is removed once the operation is complete.
    public sealed class MainThread : IDisposable
    {
        readonly Func<bool> tick; readonly Action<Exception> failed;
        readonly object sync = new object();
        EventInfo frame; Delegate handler;
        Timer waiting; DateTime deadline; int generation;
        public MainThread(Func<bool> tick, Action<Exception> failed = null) { this.tick = tick; this.failed=failed; }
        public static void Drain(Action unload, Action<string,string> status)
        {
            var began=DateTime.UtcNow;
            new MainThread(()=>{try{unload();return false;}catch(Exception e){
                if(DateTime.UtcNow-began>TimeSpan.FromSeconds(30)){status("error",e.GetBaseException().Message);return false;}
                status("handoff",e.GetBaseException().Message);return true;
            }},e=>status("error",e.GetBaseException().Message)).Schedule();
        }
        public void Schedule()
        {
            lock(sync) {
                if (!TrySchedule()) throw new InvalidOperationException("Waiting for Unity UI to load; reconnect once the game UI is visible.");
                StopWaiting();
            }
        }
        // Mono can be running before Unity's UI assembly has loaded. Waiting here
        // does not reload the payload, touch login state or block a game thread.
        public void ScheduleWhenReady(TimeSpan timeout)
        {
            if(timeout<=TimeSpan.Zero)throw new ArgumentOutOfRangeException("timeout");
            lock(sync) {
                if(handler!=null||waiting!=null)return;
                if(TrySchedule())return;
                deadline=DateTime.UtcNow+timeout;
                waiting=new Timer(WaitForUi,++generation,250,250);
            }
        }
        void WaitForUi(object state)
        {
            lock(sync) {
                if(waiting==null||(int)state!=generation)return;
                try {
                    if(TrySchedule()){StopWaiting();return;}
                    if(DateTime.UtcNow>=deadline)throw new TimeoutException("Unity UI did not become ready within the connection wait; retry after the game UI is visible.");
                } catch(Exception error) {StopWaiting();Fail(error);}
            }
        }
        void StopWaiting(){generation++;var old=waiting;waiting=null;if(old!=null)old.Dispose();}
        bool TrySchedule()
        {
            if (handler != null) return true;
            Type canvas = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) { canvas = assembly.GetType("UnityEngine.Canvas", false); if (canvas != null) break; }
            if (canvas == null) return false;
            frame = canvas.GetEvent("willRenderCanvases", BindingFlags.Static | BindingFlags.Public);
            if (frame == null) throw new MissingMemberException("UnityEngine.Canvas.willRenderCanvases");
            var subscribed = Delegate.CreateDelegate(frame.EventHandlerType, this, GetType().GetMethod("Frame", BindingFlags.Instance | BindingFlags.NonPublic));
            frame.AddEventHandler(null, subscribed);handler=subscribed;
            return true;
        }
        void Fail(Exception error){if(failed!=null){try{failed(error);}catch{}}else System.Diagnostics.Trace.TraceError(error.ToString());}
        void Frame() { lock(sync){if(handler==null)return;try { if (!tick()) Dispose(); } catch(Exception error) { Dispose();Fail(error); }} }
        public void Dispose() { lock(sync){StopWaiting();if (handler == null) return; var previous = handler; handler = null; frame.RemoveEventHandler(null, previous);} }
    }
}
