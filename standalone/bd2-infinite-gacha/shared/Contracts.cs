using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
namespace BD2InfiniteGacha
{
    public static class Identity
    {
        public const string LiveEntries="runtime.json|control.json|snapshot.json";
        public const string Version="0.2.3",Runtime="BD2InfiniteGacha.Runtime5";
        public static string Root {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2InfiniteGacha");}}
        public static string Hash(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
    }
    [DataContract] public class Costume
    {
        [DataMember] public int Id;
        [DataMember] public string Character="",Name="";
        [DataMember] public int Level=-1,MaxLevel=5;
        public bool Full {get{return Level>=5;}}
    }
    [DataContract] public class Pool
    {
        [DataMember] public int Id,DrawId,Size;
        [DataMember] public long EndTimeUnixMilliseconds;
        [DataMember] public string Name {get;set;} = "";
        [DataMember] public string Key="";
        [DataMember] public Costume[] Costumes=new Costume[0];
    }
    [DataContract] public class RuleRow
    {
        [DataMember] public int A;
        [DataMember] public bool Enabled;
        [DataMember] public string B="";
    }
    [DataContract] public class RuleDraft
    {
        [DataMember] public string Threshold="";
        [DataMember] public bool CountCopies=true;
        [DataMember] public bool ExcludeOverflow=true;
        [OnDeserializing] private void Defaults(StreamingContext context){ExcludeOverflow=true;}
        [DataMember] public int[] A=new int[0],B=new int[0];
        [DataMember] public RuleRow[] Rows=Enumerable.Range(0,10).Select(a=>new RuleRow{A=a}).ToArray();
        [DataMember] public string Interval="1000";
    }
    public sealed class MatchResult
    {
        public int A,B,Overflow;public bool[] OverflowSlots=new bool[0];public bool Matched;public string Reason="";
    }
    public static class Rules
    {
        public static string RowError(int a,string b)
        {int n;if(!int.TryParse(b,out n)||n<0)return "B 数量须为非负整数";return a+n>10?"A + B 不能超过 10":"";}
        public static int Capacity(Costume costume)
        {
            if(costume==null||costume.Level< -1||costume.Level>5)throw new InvalidOperationException("服装库存破度尚未正确读取");
            return 5-costume.Level; // 未拥有(-1)需要6张；+4需要1张；+5为0。
        }
        public static string Validate(RuleDraft d,IEnumerable<Costume> inventory)
        {
            int top,interval;
            if(d==null||!int.TryParse(d.Threshold,out top)||top<1||top>10)return "无条件停止的 A 数量须为 1–10";
            if(!int.TryParse(d.Interval,out interval)||interval<100||interval>60000)return "抽取间隔须为 100–60000 ms";
            if(d.A==null||d.B==null||d.Rows==null||d.Rows.Any(r=>r==null))return "配置不完整";
            var items=inventory.ToArray();var allowed=new HashSet<int>(items.Select(c=>c.Id));
            if(d.A.Concat(d.B).Any(id=>!allowed.Contains(id)))return "所选服装已不在当前可抽列表，请重新检查分组";
            if(d.A.Intersect(d.B).Any())return "同一服装不能同时属于 A 组和 B 组";
            if(d.Rows.Select(r=>r.A).Distinct().Count()!=d.Rows.Length||d.Rows.Any(r=>r.A<0||r.A>9))return "条件档位无效";
            foreach(var row in d.Rows.Where(r=>r.Enabled&&r.A<top))
            {var error=RowError(row.A,row.B);if(error!="")return "A = "+row.A+"："+error;}
            bool limited=d.CountCopies&&d.ExcludeOverflow;
            if(limited&&items.Any(c=>c.Level< -1||c.Level>5))return "服装库存破度尚未正确读取，无法排除溢出";
            Func<int[],int> capacity=ids=>!d.CountCopies?ids.Distinct().Count():limited?items.Where(c=>ids.Contains(c.Id)).Sum(Capacity):ids.Length>0?10:0;
            int aMax=capacity(d.A),bMax=capacity(d.B);
            bool possible=aMax>=top;
            foreach(var row in d.Rows.Where(r=>r.Enabled&&r.A<top))
            {if(row.A<=aMax&&int.Parse(row.B)<=bMax)possible=true;}
            return possible?"":limited?"排除溢出后，当前分组没有任何可达到的停止条件":"当前分组没有任何可达到的停止条件";
        }
        public static MatchResult Count(RuleDraft d,int[] result,IEnumerable<Costume> inventory)
        {
            if(result==null||result.Length!=10)throw new InvalidOperationException("必须读取完整的 10 个抽取结果");
            var a=new HashSet<int>(d.A);var b=new HashSet<int>(d.B);var seen=new Dictionary<int,int>();
            var stock=inventory.ToDictionary(c=>c.Id);var match=new MatchResult{OverflowSlots=new bool[result.Length]};
            for(int i=0;i<result.Length;i++)
            {
                int id=result[i],count;seen.TryGetValue(id,out count);seen[id]=++count;
                if(!d.CountCopies&&count>1)continue;
                if(d.CountCopies&&d.ExcludeOverflow)
                {
                    Costume costume;if(!stock.TryGetValue(id,out costume))throw new InvalidOperationException("抽取结果包含未识别服装");
                    if(count>Capacity(costume)){match.Overflow++;match.OverflowSlots[i]=true;continue;}
                }
                if(a.Contains(id))match.A++;if(b.Contains(id))match.B++;
            }
            return match;
        }
        public static MatchResult Evaluate(RuleDraft d,int[] result,IEnumerable<Costume> inventory)
        {
            var match=Count(d,result,inventory);
            int top=int.Parse(d.Threshold);
            if(match.A>=top){match.Matched=true;match.Reason="A ≥ "+top+"，无条件停止";return match;}
            var row=d.Rows.SingleOrDefault(r=>r.A==match.A&&r.A<top&&r.Enabled);
            if(row!=null&&match.B>=int.Parse(row.B)){match.Matched=true;match.Reason="A = "+row.A+" 且 B ≥ "+row.B;}
            return match;
        }
    }

    [DataContract] public class Control
    {
        [DataMember] public bool Enabled;
        [DataMember] public string Owner="",Account="",PoolKey="";
        [DataMember] public int ProcessId,PoolId;
        [DataMember] public long ProcessStart,Expires;
        [DataMember] public RuleDraft Rules=new RuleDraft();
    }
    [DataContract] public class Snapshot
    {
        [DataMember] public string Runtime=Identity.Runtime,Instance="",Account="",Player="",State="waiting_game",Message="",Owner="",ResultKey="";
        [DataMember] public int ProcessId,PoolId,Rolls,A,B,InventoryCount,SkipCount;
        [DataMember] public long ProcessStart,At,Sequence;
        [DataMember] public bool InventoryReady,ResultReady,Locked,CanSkip,PopupOpen;
        [DataMember] public string AnimationStage="";
        [DataMember] public Pool[] Pools=new Pool[0];
        [DataMember] public int[] Result=new int[0];
    }
    [DataContract] public class RuntimeStatus
    {
        [DataMember] public string State="",Error="",Runtime=Identity.Runtime;
        [DataMember] public int ProcessId;
        [DataMember] public long ProcessStart,At;
    }
}
