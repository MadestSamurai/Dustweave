using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using HarmonyLib;
using BD2Sichuan;
using UnityEngine;
using B = BD2Sichuan.Runtime.SichuanBindings;
namespace BD2Sichuan.Runtime
{
    // Reads and explicitly leased pair inputs run only on the game main-thread callback.
    internal sealed class SichuanCapture : IDisposable
    {
        private static SichuanCapture current;
        private readonly string session=Guid.NewGuid().ToString("N");
        private readonly string root=Path.Combine(LocalStorage.DataRoot,"sichuan");
        private readonly Harmony harmony=new Harmony("bd2.standalone.sichuan.capture");
        private readonly object sync=new object();
        private readonly Dictionary<int,string> icons=new Dictionary<int,string>();
        private readonly Queue<Tuple<string,byte[]>> imageWrites=new Queue<Tuple<string,byte[]>>();
        private readonly FieldInfo tile=(FieldInfo)B.Api("View.Tile");
        private readonly FieldInfo normalAnimations=(FieldInfo)B.Api("UI.NormalAnimations");
        private readonly FieldInfo comboAnimations=(FieldInfo)B.Api("UI.ComboAnimations");
        private readonly SichuanRunEngine executor=new SichuanRunEngine();
        private readonly Queue<SichuanActionReceipt> receipts=new Queue<SichuanActionReceipt>();
        private SichuanRunCommand request;
        private SichuanActionLease executionLease;
        private Timer writer;
        private volatile bool disposed;
        private long enabledUntilTicks;
        private SichuanSnapshot pending;
        private object model;
        private long generation,revision;
        private string lastHash="";
        private readonly Dictionary<int,DateTime> iconRetry=new Dictionary<int,DateTime>();
        private DateTime next=DateTime.MinValue;
        private int lastFrame=-1;
        private readonly Queue<SichuanRunEvent> runEvents=new Queue<SichuanRunEvent>();
        private string lastRunKey="";
        private int[] hashCells=new int[0];
        private int hashWidth,hashHeight;
        private readonly int processId=Process.GetCurrentProcess().Id;
        private int writing;
        private string error="";
        internal void Start(MethodInfo pump)
        {
            current=this;
            try
            {
                // Resolve required interfaces for this connection before patching.
                var frame=(MethodInfo)B.Api("UI.Frame");
                var create=(MethodInfo)B.Api("Model.Create");
                if(frame==null||create==null||tile==null||normalAnimations==null||comboAnimations==null)throw new InvalidOperationException("Sichuan capture ABI unresolved");
                harmony.Patch(frame,postfix:new HarmonyMethod(typeof(SichuanCapture),nameof(Pump)));
                harmony.Patch(pump,postfix:new HarmonyMethod(typeof(SichuanCapture),nameof(Pump)));
                harmony.Patch(create,postfix:new HarmonyMethod(typeof(SichuanCapture),nameof(GenerationChanged)));
            }
            catch(Exception e){error=e.GetBaseException().Message;harmony.UnpatchAll("bd2.standalone.sichuan.capture");throw;}
            writer=new Timer(_=>Flush(),null,0,50);
        }
        private static void GenerationChanged(object __instance)
        {
            try{var c=current;if(c!=null && ReferenceEquals(c.model,__instance)){c.generation++;c.lastHash="";c.next=DateTime.MinValue;}}catch{}
        }
        private static void Pump(){try{SichuanNetworkTrace.ReconcileCurrent();current?.Capture();}catch(Exception e){current?.Fault(e);}}
        private void Fault(Exception e)
        {
            executor.Stop("capture_exception:"+e.GetType().Name);JournalRun(DateTime.UtcNow);
            var s=Base("capture_error");s.Error=e.GetBaseException().Message;lock(sync)pending=s;
        }
        private SichuanSnapshot Base(string state)=>new SichuanSnapshot {
            SessionId=session,ProcessId=processId,Runtime=SichuanIdentity.RuntimeName,
            ClientMvid=typeof(SichuanBoardUI).Module.ModuleVersionId.ToString(),CapturedAtUtc=DateTime.UtcNow.ToString("O"),
            Generation=generation,Revision=revision,State=state,Error=error,ExecutionProtocol=3,Run=executor.Status,LastAction=executor.Receipt,NetworkState=SichuanNetworkTrace.NetworkState};
        private void Capture()
        {
            if(disposed)return;
            var now=DateTime.UtcNow;
            if(now.Ticks>Interlocked.Read(ref enabledUntilTicks)){executor.Stop("capture_heartbeat_expired");JournalRun(now);return;}
            bool publish=now>=next;
            if(!publish&&!executor.Active)return;
            if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
            if(publish)next=now.AddMilliseconds(100);
            var watch=Stopwatch.StartNew();
            if(!B.TryBoard(out var ui)||B.Read("UI.Model",ui)==null)
            {model=null;lastHash="";var empty=Base("waiting_board");Execute(empty,null,null,now);lock(sync)pending=empty;return;}
            var board=B.Read("UI.Model",ui);
            if(!ReferenceEquals(board,model)){model=board;generation++;lastHash="";}
            var s=Snapshot(ui,board);
            s=Execute(s,ui,board,now);
            if(!publish)return;
            var cells=s.Cells;var views=B.List("UI.Views",ui);
            // Image extraction is display work, never a prerequisite for a pair.
            if(!executor.Active)for(int i=0;i<Math.Min(cells.Length,views.Count);i++)
                if(cells[i]!=0&&!icons.ContainsKey(cells[i])&&(!iconRetry.TryGetValue(cells[i],out var retry)||now>=retry)&&B.Int("View.Kind",views[i])==cells[i])
                {TryIcon(cells[i],views[i]);break;}
            s.ImageFiles=cells.Select(id=>icons.TryGetValue(id,out var path)?path:"").ToArray();
            s.CaptureMilliseconds=watch.Elapsed.TotalMilliseconds;
            lock(sync)pending=s;
        }
        private SichuanSnapshot Snapshot(object ui,object board)
        {
            int w=B.Int("Model.Width",board),h=B.Int("Model.Height",board);
            var cells=B.List("Model.Cells",board).Cast<object>().Select(b=>B.Int("Block.Kind",b)).ToArray();
            if(w<1||h<1||w>32||h>32||cells.Length!=w*h)throw new InvalidOperationException("Sichuan board shape is incomplete");
            if(lastHash.Length==0||hashWidth!=w||hashHeight!=h||!hashCells.SequenceEqual(cells))
            {
                using(var sha=SHA256.Create())lastHash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(w+"x"+h+":"+string.Join(",",cells)))).Replace("-","");
                hashCells=cells;hashWidth=w;hashHeight=h;revision++;
            }
            bool playing=B.Flag("Model.Playing",board),paused=B.Flag("UI.Paused",ui),shuffling=B.Flag("Model.Shuffling",board),locked=B.Flag("UI.Locked",ui);
            // Mirrors TouchAutoQuickly: ordinary removal animations may overlap.
            string state=SichuanFastSolver.Cleared(cells)||!playing?"finished":shuffling?"shuffling":paused?"paused":locked?"waiting_animation":"ready";
            var s=Base(state);s.Width=w;s.Height=h;s.Cells=cells;s.BoardHash=lastHash;s.InputReady=state=="ready";
            s.AnimationCount=Math.Max(0,(int)normalAnimations.GetValue(ui))+Math.Max(0,(int)comboAnimations.GetValue(ui));
            s.LevelGroup=B.Int("UI.LevelGroup",ui);s.Level=B.Int("UI.Level",ui);
            s.RemainingSeconds=Convert.ToSingle(B.Call("Timer.Remaining",B.Read("Model.RemainingTimer",board)));
            s.ElapsedSeconds=Convert.ToSingle(B.Call("Timer.Elapsed",B.Read("Model.ElapsedTimer",board)));
            s.ComboSeconds=B.Num("Model.ComboSeconds",board);s.TimerBonusSeconds=B.Num("Model.TimerBonus",board);
            s.ComboCount=Convert.ToInt32(B.Call("Model.ComboCount",board))+1;
            var views=B.List("UI.Views",ui);
            for(int i=0;i<Math.Min(cells.Length,views.Count);i++)
                if(SichuanFastSolver.Pairable(cells[i])&&B.Flag("View.Selected",views[i])){s.SelectedIndex=i;break;}
            // After a shuffle wait for real views to agree; never click a stale tile.
            if(views.Count!=cells.Length)s.InputReady=false;
            else for(int i=0;i<cells.Length;i++)if(SichuanFastSolver.Pairable(cells[i])&&B.Int("View.Kind",views[i])!=cells[i]){s.InputReady=false;break;}
            return s;
        }
        private SichuanSnapshot Execute(SichuanSnapshot s,object ui,object board,DateTime now)
        {
            SichuanRunCommand r;SichuanActionLease lease;lock(sync){r=request;lease=executionLease;}
            var previous=executor.Receipt;SichuanSnapshot after=null;
            executor.Tick(s,r,lease,now,Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency,SichuanFastSolver.Choose,
                move=>ApplyPair(ui,s,move.First,move.Second),()=>after=Snapshot(ui,board));
            s=after??s;s.LastAction=executor.Receipt;s.Run=executor.Status;
            if(!ReferenceEquals(previous,s.LastAction))lock(sync)receipts.Enqueue(s.LastAction);
            JournalRun(now);return s;
        }
        private void JournalRun(DateTime now)
        {
            var s=executor.Status;string key=s.OwnerId+":"+s.State+":"+s.Reason+":"+s.IntervalMilliseconds;
            if(key==lastRunKey)return;lastRunKey=key;
            lock(sync)runEvents.Enqueue(new SichuanRunEvent{AtUtc=now.ToString("O"),SessionId=session,Run=s});
        }
        private bool ApplyPair(object ui,SichuanSnapshot s,int a,int b)
        {
            if(ui==null||!ReferenceEquals(model,B.Read("UI.Model",ui)))throw new InvalidOperationException("board changed before input");
            var board=model;var views=B.List("UI.Views",ui);
            if(views.Count!=s.Cells.Length||B.Int("View.Kind",views[a])!=s.Cells[a]||B.Int("View.Kind",views[b])!=s.Cells[b])
                throw new InvalidOperationException("view/model mismatch");
            bool confirmed=false;
            var handler=B.SubscribePair(board,(first,second)=>{if(first==a&&second==b||first==b&&second==a)confirmed=true;});
            try{B.Call("View.Click",views[a]);B.Call("View.Click",views[b]);return confirmed;}
            finally{B.UnsubscribePair(board,handler);}
        }
        private T ReadCommand<T>(string name) where T:class
        {
            try{using(var f=new MemoryStream((name=="run-command.json"?BD2.LocalIpc.RuntimeFiles.Take(Path.Combine(root,name)):BD2.LocalIpc.RuntimeFiles.Read(Path.Combine(root,name)))??new byte[0]))
            {if(f.Length>64000)return null;return new DataContractJsonSerializer(typeof(T)).ReadObject(f) as T;}}catch{return null;}
        }
        private void AppendJournal<T>(string name,T receipt)
        {
            var path=Path.Combine(root,name+".jsonl");
            if(File.Exists(path)&&new FileInfo(path).Length>1000000)
            {
                for(int i=3;i>=1;i--){var from=i==1?path:Path.Combine(root,name+"."+(i-1)+".jsonl");var to=Path.Combine(root,name+"."+i+".jsonl");if(File.Exists(to))File.Delete(to);if(File.Exists(from))File.Move(from,to);}
            }
            using(var buffer=new MemoryStream())
            {new DataContractJsonSerializer(typeof(T)).WriteObject(buffer,receipt);using(var f=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read)){var bytes=buffer.ToArray();f.Write(bytes,0,bytes.Length);f.WriteByte(10);}}
        }
        private void TryIcon(int id,object view)
        {
            iconRetry[id]=DateTime.UtcNow.AddSeconds(10);
            RenderTexture rt=null;Texture2D copy=null;var old=RenderTexture.active;
            try
            {
                var sprite=(tile.GetValue(view) as UISprite)?.sprite;if(sprite==null || sprite.packed && sprite.packingRotation!=SpritePackingRotation.None)return;
                var rect=sprite.textureRect;if(rect.width<1||rect.height<1||rect.width>1024||rect.height>1024)return;
                rt=RenderTexture.GetTemporary((int)rect.width,(int)rect.height,0,RenderTextureFormat.ARGB32);
                var texture=sprite.texture;
                Graphics.Blit(texture,rt,new Vector2(rect.width/texture.width,rect.height/texture.height),new Vector2(rect.x/texture.width,rect.y/texture.height));
                RenderTexture.active=rt;copy=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);
                copy.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);copy.Apply();
                var bytes=ImageConversion.EncodeToPNG(copy);var file=session+"-"+id+".png";
                lock(sync){imageWrites.Enqueue(Tuple.Create(file,bytes));}icons[id]=file;
            }
            catch{ /* Icon failure does not invalidate the logical board. Retry on a later snapshot. */ }
            finally{RenderTexture.active=old;if(rt!=null)RenderTexture.ReleaseTemporary(rt);if(copy!=null)UnityEngine.Object.Destroy(copy);}
        }
        private void Flush()
        {
            if(Interlocked.Exchange(ref writing,1)!=0)return;
            try
            {
                if(disposed)return;
                // The UI renews an explicit local lease; a crashed/closed UI cannot keep capturing.
                long ticks=Interlocked.Read(ref enabledUntilTicks);
                try{using(var f=new MemoryStream(BD2.LocalIpc.RuntimeFiles.Read(Path.Combine(root,"enabled-until.txt"))??new byte[0]))using(var r=new StreamReader(f))if(long.TryParse(r.ReadToEnd(),out var readTicks))ticks=readTicks;}catch{}
                Interlocked.Exchange(ref enabledUntilTicks,ticks);
                var command=ReadCommand<SichuanRunCommand>("run-command.json");
                var lease=ReadCommand<SichuanActionLease>("execution-lease.json");
                SichuanSnapshot s;Tuple<string,byte[]>[] images;SichuanActionReceipt[] events;SichuanRunEvent[] runs;
                lock(sync){if(command!=null)request=command;if(lease!=null)executionLease=lease;s=pending;pending=null;images=imageWrites.ToArray();imageWrites.Clear();events=receipts.ToArray();receipts.Clear();runs=runEvents.ToArray();runEvents.Clear();}
                foreach(var receipt in events)try{AppendJournal("actions",receipt);}catch(Exception e){LocalStorage.Log("Sichuan action journal: "+e.Message);}
                foreach(var run in runs)try{AppendJournal("runs",run);}catch(Exception e){LocalStorage.Log("Sichuan run journal: "+e.Message);}
                foreach(var item in images)
                {
                    try{LocalStorage.WriteAtomically(Path.Combine(root,"images",item.Item1),item.Item2);}
                    catch{lock(sync)imageWrites.Enqueue(item);}
                }
                if(DateTime.UtcNow.Ticks>ticks)s=Base("disabled");
                else if(error.Length>0)s=Base("unsupported_client");
                if(s!=null)LocalStorage.WriteJsonAtomically(Path.Combine(root,"latest.json"),s);
            }
            catch(Exception e){LocalStorage.Log("Sichuan capture write: "+e.GetBaseException().Message);}
            finally{Interlocked.Exchange(ref writing,0);}
        }
        internal bool Writing=>writing!=0;
        internal void PrepareHandoff(){Interlocked.Exchange(ref enabledUntilTicks,0);lock(sync){request=null;executionLease=null;}executor.Stop("component-handoff");}
        public void Dispose(){executor.Stop("component-unloaded");disposed=true;current=null;writer?.Dispose();harmony.UnpatchAll("bd2.standalone.sichuan.capture");}
    }
}
