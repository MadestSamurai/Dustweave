using System;
using System.Linq;
namespace BD2InfiniteGacha
{
    // Production decision machine, also exercised without a game or injector in tests.
    public sealed class AutomationMachine
    {
        public string Owner="",State="idle",Message="等待开始";
        public int Rolls,A,B;
        public bool SkipAnimation {get;private set;}
        private long sentAt,lastResponse,lastResult,nextDraw,missingSince,animationSince;
        public bool Step(Control c,Snapshot s,long response,long result,int responseError,long now)
        {
            SkipAnimation=false;
            bool lease=c!=null&&c.Enabled&&!string.IsNullOrEmpty(c.Owner)&&c.Expires>now&&c.Expires<=now+TimeSpan.FromSeconds(25).Ticks;
            if(!lease||c==null){if(State=="running"||State=="pending")Halt("stopped","已停止刷新，保留当前抽取结果");return false;}
            if(c.Owner!=Owner){Owner=c.Owner;State="running";Message="正在检查当前结果";Rolls=0;sentAt=0;missingSince=0;animationSince=0;nextDraw=now;}
            if(State!="running"&&State!="pending")return false;
            if(c.ProcessId!=s.ProcessId||c.ProcessStart!=s.ProcessStart||(!string.IsNullOrEmpty(s.Account)&&c.Account!=s.Account))return Fail("账号或游戏进程已变化，请重新开始");
            if(string.IsNullOrEmpty(s.Account)||s.PoolId==0||s.Pools.Length==0||s.Pools.Any(p=>p.Costumes.Any(x=>x.Level< -1)))
            {
                if(missingSince==0)missingSince=now;
                Message="游戏界面暂未就绪，正在等待恢复";
                return now-missingSince>TimeSpan.FromSeconds(15).Ticks?Fail("游戏界面超过15秒未恢复，请检查当前页面"):false;
            }
            missingSince=0;
            var pool=s.Pools.SingleOrDefault(p=>p.Id==c.PoolId);
            if(pool==null||pool.Key!=c.PoolKey||s.PoolId!=c.PoolId)return Fail("当前卡池已变化，请返回已配置的无限抽抽乐结果页");
            var error=Rules.Validate(c.Rules,pool.Costumes);if(error!="")return Fail(error);
            if(s.Locked)return Fail("当前结果已在游戏内锁定，请先在游戏中处理锁定状态");
            if(State=="pending")
            {
                if(response>lastResponse&&responseError!=0)return Fail("本次刷新返回错误 "+responseError+"，已停止且不会自动重发");
                if(response<=lastResponse&&now-sentAt>TimeSpan.FromSeconds(30).Ticks)return Fail("等待本次抽取结果超时，请先检查游戏；不会自动重发");
                if(response>lastResponse&&animationSince==0)animationSince=now;
                if(s.PopupOpen){animationSince=now;Message="等待关闭游戏弹窗，关闭后自动继续";return false;}
                if(animationSince>0&&now-animationSince>TimeSpan.FromSeconds(120).Ticks)return Fail("结果已返回但动画超过120秒未完成，请在游戏内检查");
                if(response>lastResponse&&!s.ResultReady&&s.CanSkip){SkipAnimation=true;Message="正在跳过抽取动画";}
                if(response<=lastResponse||result<=lastResult||!s.ResultReady)return false;
                Rolls++;State="running";
            }
            if(!s.ResultReady){Message="等待游戏显示完整结果或关闭弹窗";return false;}
            if(s.Result.Length!=10||s.Result.Any(id=>!pool.Costumes.Any(p=>p.Id==id)))return Fail("抽取结果不完整或包含未识别服装，已停止");
            var match=Rules.Evaluate(c.Rules,s.Result,pool.Costumes);A=match.A;B=match.B;
            if(match.Matched){Halt("matched","已达标："+match.Reason+"。请在游戏内决定是否保留。");return false;}
            if(now<nextDraw){Message="当前结果未达标，等待下次刷新";return false;}
            State="pending";Message="已发起刷新，等待游戏返回新结果";
            sentAt=now;nextDraw=now+TimeSpan.FromMilliseconds(int.Parse(c.Rules.Interval)).Ticks;lastResponse=response;lastResult=result;animationSince=0;return true;
        }
        public void Halt(string state,string message){State=state;Message=message;SkipAnimation=false;}
        private bool Fail(string message){Halt("error",message);return false;}
    }
}
