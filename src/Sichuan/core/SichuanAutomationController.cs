namespace BD2Sichuan;

// Desktop owns intent and observes progress. Pair planning/dispatch never waits for this observer.
public sealed class SichuanAutomationController
{
    public bool Active {get;private set;}
    public string OwnerId {get;private set;}="";
    public string Status {get;private set;}="自动执行默认关闭";
    public int CompletedPairs {get;private set;}
    private DateTime started;
    private string session="";
    public SichuanRunCommand Start(bool automatic,SichuanSnapshot s,DateTime now)
    {
        if(Active)throw new InvalidOperationException("已有自动任务");
        if(s.ExecutionProtocol!=3)throw new InvalidOperationException("请连接 独立连连看组件");
        Active=true;OwnerId=Guid.NewGuid().ToString("N");session=s.SessionId;started=now;CompletedPairs=0;
        Status=automatic?"自动本局已开启，等待本局可操作盘面":"正在执行下一对";
        return new(){OwnerId=OwnerId,SessionId=s.SessionId,ProcessId=s.ProcessId,CreatedUtcTicks=now.Ticks,Automatic=automatic};
    }
    public void Stop(string reason="已停止"){Active=false;Status=Reason(reason);}
    public void Observe(SichuanSnapshot s,DateTime now)
    {
        if(!Active)return;
        if(s.SessionId!=session){Stop("session_changed");return;}
        var run=s.Run;
        if(run==null||run.OwnerId!=OwnerId)
        {if(now-started>TimeSpan.FromSeconds(5))Stop("run_ack_timeout");return;}
        CompletedPairs=run.ConfirmedPairs;Status=Reason(run.Reason);
        if(run.MeanIntervalMilliseconds>0)Status+=$" · 平均间隔 {run.MeanIntervalMilliseconds:F0} ms";
        if(run.State is "completed" or "stopped")Active=false;
    }
    private static string Reason(string value)=>value switch {
        "logical_readback_confirmed"=>"自动执行中，每对均经客户端事件和逻辑盘面确认",
        "paused"=>"游戏暂停，恢复后自动继续",
        "shuffling"=>"等待游戏洗牌，完成后自动继续",
        "waiting_animation" or "input_locked"=>"等待游戏解除输入锁定",
        "manual_selection_present"=>"等待你完成或取消当前手动选择",
        "waiting_legal_pair"=>"等待游戏更新可配对盘面",
        "waiting_board" or "waiting_next_board"=>"等待本局盘面",
        "board_cleared"=>"本局已清空，自动执行完成",
        "time_finished"=>"本局时间结束",
        "game_finished"=>"本局结束",
        "round_changed"=>"关卡已切换，本次执行结束",
        "single_pair_completed"=>"已完成一对",
        "user_stop"=>"已手动停止",
        "window_closed"=>"窗口已关闭",
        "capture_disabled"=>"提示及自动执行已暂停",
        "execution_heartbeat_expired"=>"执行心跳过期，已停止",
        "execution_owner_missing"=>"执行控制权已变化，已停止",
        "capture_heartbeat_expired"=>"采集心跳过期，已停止",
        "session_changed"=>"游戏连接已变化，已停止",
        "run_ack_timeout"=>"Hook 未确认启动请求，已停止",
        "client_pair_event_missing"=>"客户端未确认配对，已停止且不会重复点击",
        "logical_readback_unchanged"=>"逻辑盘面未确认变化，已停止且不会重复点击",
        _=>value};
}
