#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
namespace BD2Territory
{
 public sealed class LayoutEntry
 {
  public int ObjectId{get;set;}public int ChunkId{get;set;}public int X{get;set;}public int Y{get;set;}public int Rotate{get;set;}
  public string Key=>ChunkId+":"+ObjectId+":"+X+":"+Y+":"+Rotate;
 }
 public sealed class LayoutDocument
 {
  public int Schema{get;set;}=1;public string Name{get;set;}="领地布局";public int WorldId{get;set;}
  public LayoutEntry[] Objects{get;set;}=new LayoutEntry[0];
 }
 public sealed class LayoutCost
 {
  public int Type{get;set;}public int Id{get;set;}public string Name{get;set;}="";public long Count{get;set;}public long Owned{get;set;}
  public string Key=>Type+":"+Id;public long Missing=>Math.Max(0,Count-Owned);
 }
 public sealed class LayoutItem
 {
  public int Id{get;set;}public string Name{get;set;}="";public int Function{get;set;}public int Layer{get;set;}public int[] OverlapLayers{get;set;}=new int[0];
  public int MaxCount{get;set;}public int LayerLimit{get;set;}public bool Unlocked{get;set;}public LayoutCost[] Costs{get;set;}=new LayoutCost[0];
 }
 public sealed class LayoutChunk
 {
  public int Id{get;set;}public int X{get;set;}public int Y{get;set;}public string Label=>""+Id+"（"+X+", "+Y+"）";
 }
 public sealed class LayoutWorld
 {
  public string Runtime{get;set;}=TerritoryIdentity.RuntimeName;public int ProcessId{get;set;}public long CapturedUtcTicks{get;set;}
  public string Account{get;set;}="";public int WorldId{get;set;}public LayoutChunk[] Chunks{get;set;}=new LayoutChunk[0];
  public LayoutItem[] Catalog{get;set;}=new LayoutItem[0];public LayoutEntry[] Objects{get;set;}=new LayoutEntry[0];
 }
 public sealed class LayoutQuote
 {
  public string Signature{get;set;}="";public int Existing{get;set;}public LayoutEntry[] Missing{get;set;}=new LayoutEntry[0];public LayoutEntry[] Sources{get;set;}=new LayoutEntry[0];public int Moved=>Sources.Count(v=>v!=null);public int Purchased=>Missing.Length-Moved;public LayoutCost[] Costs{get;set;}=new LayoutCost[0];
 }
 public sealed class LayoutRequest
 {
  public string Token{get;set;}="";public string Account{get;set;}="";public string Operation{get;set;}="preview";
  public string QuoteSignature{get;set;}="";public LayoutDocument Document{get;set;}
 }
 public sealed class LayoutStatus
 {
  public string Token{get;set;}="";public string State{get;set;}="idle";public string Message{get;set;}="";public int Done{get;set;}public int Total{get;set;}
  public LayoutQuote Quote{get;set;}public long CapturedUtcTicks{get;set;}
 }
 public sealed class LayoutIntent
 {
  public string Account{get;set;}="";public int WorldId{get;set;}public LayoutEntry Entry{get;set;}public string Token{get;set;}="";
 }
 public static class LayoutPlanner
 {
  public static void Validate(LayoutDocument d)
  {
   if(d==null||d.Schema!=1||d.WorldId<=0||d.Objects==null||d.Objects.Length==0||d.Objects.Length>2000||d.Name==null||d.Name.Length>160)throw new InvalidOperationException("布局文件无效：需要 1–2000 个设施和有效领地类型。");
   if(d.Objects.Any(v=>v==null||v.ObjectId<=0||v.ChunkId<0||v.X<0||v.X>=10||v.Y<0||v.Y>=10||v.Rotate<0||v.Rotate>3))throw new InvalidOperationException("布局坐标或朝向无效。");
   if(d.Objects.Select(v=>v.Key).Distinct().Count()!=d.Objects.Length)throw new InvalidOperationException("布局含重复设施。");
  }
  public static LayoutQuote Quote(LayoutWorld w,LayoutDocument d)
  {
   Validate(d);if(w==null||string.IsNullOrEmpty(w.Account)||w.WorldId!=d.WorldId)throw new InvalidOperationException("布局的领地类型与当前账号不一致。");
   var catalog=w.Catalog.ToDictionary(v=>v.Id);var current=new HashSet<string>(w.Objects.Select(v=>v.Key));
   foreach(var e in d.Objects)
   {
    if(!catalog.TryGetValue(e.ObjectId,out var item))throw new InvalidOperationException("布局包含不支持自动建造的设施："+e.ObjectId);
    if(!w.Chunks.Any(c=>c.Id==e.ChunkId))throw new InvalidOperationException("区域尚未解锁："+e.ChunkId);
    if(!current.Contains(e.Key)&&!item.Unlocked)throw new InvalidOperationException(item.Name+" 尚未解锁。");
   }
   var missing=d.Objects.Where(v=>!current.Contains(v.Key)).ToArray();
   var retained=new HashSet<string>(d.Objects.Select(v=>v.Key));var available=w.Objects.Where(v=>!retained.Contains(v.Key)).OrderBy(v=>v.Key,StringComparer.Ordinal).GroupBy(v=>v.ObjectId).ToDictionary(g=>g.Key,g=>new Queue<LayoutEntry>(g));
   var sources=missing.Select(v=>available.TryGetValue(v.ObjectId,out var queue)&&queue.Count>0?queue.Dequeue():null).ToArray();
   var purchases=missing.Where((v,i)=>sources[i]==null).ToArray();
   foreach(var g in purchases.GroupBy(v=>v.ObjectId))
   {var item=catalog[g.Key];if(item.MaxCount>0&&w.Objects.Count(v=>v.ObjectId==g.Key)+g.Count()>item.MaxCount)throw new InvalidOperationException(item.Name+" 超过建造上限；已优先复用可移动的同类设施。");}
   foreach(var g in purchases.Where(v=>catalog[v.ObjectId].LayerLimit>0).GroupBy(v=>catalog[v.ObjectId].Layer))
   {int limit=g.Min(v=>catalog[v.ObjectId].LayerLimit);if(w.Objects.Count(v=>catalog.ContainsKey(v.ObjectId)&&catalog[v.ObjectId].LayerLimit>0&&catalog[v.ObjectId].Layer==g.Key)+g.Count()>limit)throw new InvalidOperationException("装饰层 "+g.Key+" 超过当前领地容量。");}
   var costs=new Dictionary<string,LayoutCost>();
   foreach(var e in purchases)foreach(var cost in catalog[e.ObjectId].Costs)
   {
    if(cost.Count<0||cost.Owned<0)throw new InvalidOperationException("设施价格或库存无效。");
    if(!costs.TryGetValue(cost.Key,out var c))costs[cost.Key]=c=new LayoutCost{Type=cost.Type,Id=cost.Id,Name=cost.Name,Owned=cost.Owned};
    c.Count=checked(c.Count+cost.Count);
   }
   var q=new LayoutQuote{Existing=d.Objects.Length-missing.Length,Missing=missing,Sources=sources,Costs=costs.Values.OrderBy(v=>v.Key,StringComparer.Ordinal).ToArray()};
   string identity=w.Account+"/"+w.WorldId+"/"+string.Join(";",d.Objects.Select(v=>v.Key))+"/"+string.Join(";",w.Objects.Select(v=>v.Key).OrderBy(v=>v,StringComparer.Ordinal))+"/"+string.Join(";",q.Costs.Select(v=>v.Key+"="+v.Count));
   using(var sha=SHA256.Create())q.Signature=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","");
   return q;
  }
  public static LayoutDocument Template(LayoutWorld w,int chunkId,int function)
  {
   var item=w.Catalog.Where(v=>v.Function==function&&v.Unlocked).OrderBy(v=>v.Id).FirstOrDefault();if(item==null)throw new InvalidOperationException("当前尚无可用的此类设施。");
   var list=new List<LayoutEntry>();
   if(function==3){for(int y=0;y<10;y++)for(int x=0;x<10;x++)list.Add(new LayoutEntry{ObjectId=item.Id,ChunkId=chunkId,X=x,Y=y});}
   else list.Add(new LayoutEntry{ObjectId=item.Id,ChunkId=chunkId,X=5,Y=5});
   return new LayoutDocument{WorldId=w.WorldId,Name=function==3?"百格连片农田":function==2?"矿场区域":"林场区域",Objects=list.ToArray()};
  }
 }
}
