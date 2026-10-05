#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
namespace BD2Sichuan
{
    public sealed class SichuanActionLease
    {
        public string OwnerId {get;set;}="";
        public long UntilUtcTicks {get;set;}
        public string StopReason {get;set;}="";
        public int IntervalMilliseconds {get;set;}=1000;
    }
    public sealed class SichuanRunCommand
    {
        public int Protocol {get;set;}=3;
        public string OwnerId {get;set;}="";
        public string SessionId {get;set;}="";
        public int ProcessId {get;set;}
        public long CreatedUtcTicks {get;set;}
        public int IntervalMilliseconds {get;set;}=1000;
        public bool Automatic {get;set;}
    }
    public sealed class SichuanRunStatus
    {
        public string OwnerId {get;set;}="";
        public string State {get;set;}="idle";
        public string Reason {get;set;}="";
        public int IntervalMilliseconds {get;set;}=1000;
        public int ConfirmedPairs {get;set;}
        public long Generation {get;set;}
        public double LastPairMilliseconds {get;set;}
        public double MeanIntervalMilliseconds {get;set;}
        public double LastPlanningMilliseconds {get;set;}
    }
    public sealed class SichuanRunEvent
    {
        public string AtUtc {get;set;}="";
        public string SessionId {get;set;}="";
        public SichuanRunStatus Run {get;set;}
    }
    public sealed class SichuanActionReceipt
    {
        public string RequestId {get;set;}="";
        public string OwnerId {get;set;}="";
        public string SessionId {get;set;}="";
        public long Generation {get;set;}
        public string Status {get;set;}="idle";
        public string Reason {get;set;}="";
        public string AtUtc {get;set;}="";
        public int First {get;set;}
        public int Second {get;set;}
        public bool PairConfirmed {get;set;}
        public double PlanningMilliseconds {get;set;}
        public double PairMilliseconds {get;set;}
        public int[] BeforeCells {get;set;}=new int[0];
        public int[] AfterCells {get;set;}=new int[0];
    }
    public sealed class SichuanFastMove
    {
        public int First {get;set;}
        public int Second {get;set;}
        public int[] Path {get;set;}=new int[0];
    }
    public static class SichuanExecutionGuard
    {
        public static string Reject(SichuanSnapshot s,SichuanFastMove move)
        {
            if(s==null||s.ExecutionProtocol!=3||!s.InputReady||s.State!="ready"||s.RemainingSeconds<=0)return "board_not_ready";
            if(s.SelectedIndex>=0)return "manual_selection_present";
            var b=s.Cells;int a=move.First,z=move.Second,w=s.Width,h=s.Height;
            if(b==null||w<1||h<1||w>32||h>32||b.Length!=w*h||a<0||z<0||a>=b.Length||z>=b.Length||a==z)return "invalid_pair";
            if(!SichuanFastSolver.Pairable(b[a])||b[a]!=b[z])return "invalid_pair";
            var path=move.Path;
            if(path==null||path.Length<2||path.Length>b.Length||path[0]!=a||path[path.Length-1]!=z)return "invalid_path";
            int turns=0,lastDx=0,lastDy=0;var visited=new HashSet<int>{a};
            for(int i=1;i<path.Length;i++)
            {
                int prev=path[i-1],next=path[i];if(next<0||next>=b.Length||!visited.Add(next))return "invalid_path";
                int dx=next%w-prev%w,dy=next/w-prev/w;
                if(Math.Abs(dx)+Math.Abs(dy)!=1||i<path.Length-1&&b[next]!=0)return "invalid_path";
                if(i>1&&(dx!=lastDx||dy!=lastDy))turns++;
                if(turns>2)return "invalid_path";lastDx=dx;lastDy=dy;
            }
            return "";
        }
        public static int[] ExpectedAfter(int[] before,int first,int second)
        {
            var b=(int[])before.Clone();int id=b[first];b[first]=b[second]=0;
            if(id/1000==11)for(int i=0;i<b.Length;i++)if(b[i]==id+1000)b[i]=0;
            return b;
        }
    }
    // Run stays in the game process. No per-pair GUI, file or global animation wait.
    public sealed class SichuanRunEngine
    {
        public const int MinimumIntervalMilliseconds=50;
        public const int MaximumIntervalMilliseconds=60000;
        public const int DefaultIntervalMilliseconds=1000;
        public static bool ValidInterval(int value)=>value>=MinimumIntervalMilliseconds&&value<=MaximumIntervalMilliseconds;
        public SichuanRunStatus Status {get;private set;}=new SichuanRunStatus();
        public SichuanActionReceipt Receipt {get;private set;}=new SichuanActionReceipt();
        public bool Active {get{return Status.State=="running"||Status.State=="waiting";}}
        private readonly HashSet<string> consumed=new HashSet<string>();
        private readonly Queue<string> order=new Queue<string>();
        private SichuanRunCommand command;
        private bool bound;
        private long generation;
        private double nextPair,firstPair,lastPair;
        private DateTime unavailableSince;
        private void State(string state,string reason)
        {Status=new SichuanRunStatus{OwnerId=Status.OwnerId,State=state,Reason=reason,ConfirmedPairs=Status.ConfirmedPairs,Generation=generation,IntervalMilliseconds=Status.IntervalMilliseconds,
            LastPairMilliseconds=Status.LastPairMilliseconds,LastPlanningMilliseconds=Status.LastPlanningMilliseconds,MeanIntervalMilliseconds=Status.MeanIntervalMilliseconds};}
        public void Stop(string reason){if(Active)State("stopped",reason);}
        public void Tick(SichuanSnapshot s,SichuanRunCommand incoming,SichuanActionLease lease,DateTime now,double clockMs,
            Func<SichuanSnapshot,SichuanFastMove> choose,Func<SichuanFastMove,bool> apply,Func<SichuanSnapshot> readback)
        {
            if(!Active&&incoming!=null&&!string.IsNullOrEmpty(incoming.OwnerId)&&!consumed.Contains(incoming.OwnerId))
            {
                consumed.Add(incoming.OwnerId);order.Enqueue(incoming.OwnerId);if(order.Count>256)consumed.Remove(order.Dequeue());
                command=incoming;bound=false;generation=0;firstPair=lastPair=nextPair=0;unavailableSince=default(DateTime);
                Status=new SichuanRunStatus{OwnerId=incoming.OwnerId,State="running",IntervalMilliseconds=incoming.IntervalMilliseconds};
                if(!ValidInterval(incoming.IntervalMilliseconds)||incoming.Protocol!=3||incoming.SessionId!=s.SessionId||incoming.ProcessId!=s.ProcessId||now.Ticks-incoming.CreatedUtcTicks>TimeSpan.FromSeconds(3).Ticks||incoming.CreatedUtcTicks>now.AddSeconds(1).Ticks)
                {Stop("invalid_or_expired_run");return;}
            }
            if(!Active)return;
            if(lease==null||lease.OwnerId!=command.OwnerId){Stop("execution_owner_missing");return;}
            if(lease.UntilUtcTicks<=now.Ticks){Stop(!string.IsNullOrEmpty(lease.StopReason)?lease.StopReason:"execution_heartbeat_expired");return;}
            if(!ValidInterval(lease.IntervalMilliseconds)){Stop("invalid_interval");return;}
            if(Status.IntervalMilliseconds!=lease.IntervalMilliseconds)
            {
                Status.IntervalMilliseconds=lease.IntervalMilliseconds;
                if(Status.ConfirmedPairs>0)nextPair=lastPair+lease.IntervalMilliseconds;
            }
            if(s.ExecutionProtocol!=3||s.SessionId!=command.SessionId||s.ProcessId!=command.ProcessId){Stop("session_changed");return;}
            if(bound&&s.Generation!=generation){State("completed","round_changed");return;}
            if(bound&&s.Cells.Length>0&&SichuanFastSolver.Cleared(s.Cells)){State("completed","board_cleared");return;}
            if(bound&&s.State=="finished"){State("completed",s.RemainingSeconds<=0?"time_finished":"game_finished");return;}
            if(s.State=="paused"||s.State=="shuffling"||s.State=="waiting_animation")
            {unavailableSince=default(DateTime);State("waiting",s.State);return;}
            if(s.State!="ready"||s.Cells.Length==0)
            {
                if(unavailableSince==default(DateTime))unavailableSince=now;
                if(bound&&now-unavailableSince>TimeSpan.FromSeconds(8))Stop("board_unavailable_timeout");
                else State("waiting","waiting_board");return;
            }
            unavailableSince=default(DateTime);
            if(SichuanFastSolver.Cleared(s.Cells)){State("waiting","waiting_next_board");return;}
            if(!bound){bound=true;generation=s.Generation;}
            if(!s.InputReady){State("waiting","input_locked");return;}
            // Only live non-empty selected cells are reported: removal animation highlights are not manual input.
            if(s.SelectedIndex>=0){State("waiting","manual_selection_present");return;}
            if(clockMs<nextPair)return;
            var watch=System.Diagnostics.Stopwatch.StartNew();SichuanFastMove move;
            try{move=choose(s);}catch(Exception e){Stop("planning_error:"+e.GetType().Name);return;}
            double planning=watch.Elapsed.TotalMilliseconds;
            if(move==null){State("waiting","waiting_legal_pair");nextPair=clockMs+50;return;}
            var rejected=SichuanExecutionGuard.Reject(s,move);if(rejected.Length>0){Stop(rejected);return;}
            var before=(int[])s.Cells.Clone();bool confirmed=false;SichuanSnapshot after=null;string reason="",result="unconfirmed";
            try
            {
                confirmed=apply(move);
                if(!confirmed)reason="client_pair_event_missing";
                else
                {
                    after=readback();
                    if(after.SessionId!=s.SessionId||after.Generation!=s.Generation)reason="round_changed_after_pair";
                    else if(before.SequenceEqual(after.Cells))reason="logical_readback_unchanged";
                    else {result="completed";reason=SichuanExecutionGuard.ExpectedAfter(before,move.First,move.Second).SequenceEqual(after.Cells)?"expected_board":"replan_after_game_effects";}
                }
            }
            catch(Exception e){reason="pair_exception:"+e.GetType().Name;}
            Receipt=new SichuanActionReceipt{RequestId=Guid.NewGuid().ToString("N"),OwnerId=command.OwnerId,SessionId=s.SessionId,Generation=s.Generation,
                Status=result,Reason=reason,AtUtc=now.ToString("O"),First=move.First,Second=move.Second,PairConfirmed=confirmed,BeforeCells=before,AfterCells=after?.Cells??new int[0],
                PlanningMilliseconds=planning,PairMilliseconds=watch.Elapsed.TotalMilliseconds};
            nextPair=clockMs+Math.Max(Status.IntervalMilliseconds,watch.Elapsed.TotalMilliseconds);
            if(result!="completed"){Stop(reason);return;}
            int count=Status.ConfirmedPairs+1;if(count==1)firstPair=clockMs;lastPair=clockMs;
            Status=new SichuanRunStatus{OwnerId=command.OwnerId,State="running",Reason="logical_readback_confirmed",ConfirmedPairs=count,Generation=generation,IntervalMilliseconds=Status.IntervalMilliseconds,
                LastPairMilliseconds=watch.Elapsed.TotalMilliseconds,LastPlanningMilliseconds=planning,MeanIntervalMilliseconds=count>1?(lastPair-firstPair)/(count-1):0};
            if(SichuanFastSolver.Cleared(after.Cells))State("completed","board_cleared");
            else if(!command.Automatic)State("completed","single_pair_completed");
        }
    }
}
