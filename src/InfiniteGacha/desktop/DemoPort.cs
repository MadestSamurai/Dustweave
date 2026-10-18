namespace BD2InfiniteGacha.Desktop;
public sealed class DemoPort:IClientPort
{
    public readonly Snapshot Snapshot;public Control Last=new();public int Commands;
    public DemoPort(Snapshot snapshot){Snapshot=snapshot;}
    public DemoPort()
    {
        var names=new[]{("黛安娜","魔法匠心"),("伊柯利普斯","梦中的新娘"),("莎赫拉查德","教授"),("海伦娜","B 级偶像"),("莱克莉斯","安卓女王"),("格雷","雾之神射手"),("安洁莉卡","海边天使"),("鲁","白猫"),("奥利维耶","铁血女王"),("布莱德","使徒")};
        var costumes=names.Select((n,i)=>new Costume{Id=i+1,Character=n.Item1,Name=n.Item2,Level=i==0?5:i%5==0?-1:i%5}).ToArray();
        Snapshot=new(){Account=Identity.Hash("fixture-account"),Player="演示账号",ProcessId=10,ProcessStart=100,At=DateTime.UtcNow.Ticks,Instance="fixture",Sequence=1,PoolId=55,InventoryReady=true,InventoryCount=10,ResultReady=true,Pools=[new(){Id=55,DrawId=550,Size=10,Name="无限抽抽乐",EndTimeUnixMilliseconds=DateTimeOffset.Parse("2026-10-01T00:00:00Z").ToUnixTimeMilliseconds(),Key=Identity.Hash("fixture-pool"),Costumes=costumes},new(){Id=44,Name="无限抽抽乐",EndTimeUnixMilliseconds=DateTimeOffset.Parse("2026-09-01T00:00:00Z").ToUnixTimeMilliseconds()},new(){Id=66,Name="无限抽抽乐",EndTimeUnixMilliseconds=DateTimeOffset.Parse("2026-11-01T00:00:00Z").ToUnixTimeMilliseconds()},new(){Id=77,Name="无限抽抽乐"}],Result=Enumerable.Range(1,10).ToArray(),Message="结果已就绪",ResultKey="initial"};
    }
    public GameProcess? Find()=>new(Snapshot.ProcessId,Snapshot.ProcessStart,"fixture.exe");
    public Snapshot? Read(){Snapshot.At=DateTime.UtcNow.Ticks;Snapshot.Sequence++;return Snapshot;}
    public void Write(Control c){Commands++;Last=JsonFiles.Clone(c);}
    public Task ConnectAsync(Action<string> progress,CancellationToken cancellation){progress("演示连接已就绪");return Task.CompletedTask;}
}
