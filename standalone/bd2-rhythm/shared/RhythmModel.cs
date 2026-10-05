using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
namespace BD2Rhythm {
 public static class RhythmIdentity {
  public const string LiveEntries="runtime.json|control.json|latest.json";
  public const string RuntimeName="BD2Rhythm.Runtime3";
  public static string DataRoot=>System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2Rhythm");
  public static bool IsGameProcessName(string name)=>string.Equals(name,"BrownDust II",StringComparison.OrdinalIgnoreCase)||string.Equals(name,"BrownDust II.exe",StringComparison.OrdinalIgnoreCase);
 }
 public sealed class RhythmRuntimeStatus {public string State{get;set;}="";public string Error{get;set;}="";public string Runtime{get;set;}=RhythmIdentity.RuntimeName;public string AtUtc{get;set;}="";public int ProcessId{get;set;}}
 public sealed class RhythmControl {
  public string OwnerId{get;set;}="";public int ProcessId{get;set;}public long UntilUtcTicks{get;set;}public bool Enabled{get;set;}
  public int JitterMs{get;set;}=50;public int OffsetMs{get;set;}
  public bool Valid(long now,int pid)=>Enabled&&!string.IsNullOrEmpty(OwnerId)&&ProcessId==pid&&UntilUtcTicks>now&&UntilUtcTicks<=now+TimeSpan.FromSeconds(15).Ticks&&ValidSettings(JitterMs,OffsetMs);
  public static bool ValidSettings(int jitter,int offset)=>jitter>=0&&jitter<=200&&offset>=-500&&offset<=500;
 }
 public sealed class RhythmSnapshot {
  public int Schema{get;set;}=1;public string Runtime{get;set;}=RhythmIdentity.RuntimeName;public int ProcessId{get;set;}public long CapturedUtcTicks{get;set;}
  public string State{get;set;}="waiting";public string Reason{get;set;}="等待曲目开始";public string Song{get;set;}="";public string Error{get;set;}="";
  public string OwnerId{get;set;}="";public int ClockMs{get;set;}public int DurationMs{get;set;}public int Sent{get;set;}public int Skipped{get;set;}public int Total{get;set;}
  public int JitterMs{get;set;}=50;public int OffsetMs{get;set;}public int LastErrorMs{get;set;}public int CompletedSongs{get;set;}public bool Armed{get;set;}
 }
 public sealed class Coord {public int Lane{get;set;}public int Time{get;set;}}
 public sealed class Note {public int Id{get;set;}public int Type{get;set;}public int Lane{get;set;}public int Time{get;set;}public List<Coord> Extra{get;set;}=new List<Coord>();}
 public sealed class Chart {
  public string Name{get;set;}="";public int Difficulty{get;set;}public int Bpm{get;set;}public List<Note> Notes{get;set;}=new List<Note>();
  public string Fingerprint(){var b=new StringBuilder();b.Append(Difficulty).Append('/').Append(Bpm).Append('|');foreach(var n in Notes.OrderBy(n=>n.Id)){b.Append(n.Id).Append(':').Append(n.Type).Append(':').Append(n.Lane).Append(':').Append(n.Time);foreach(var e in n.Extra)b.Append(',').Append(e.Lane).Append(':').Append(e.Time);b.Append('|');}using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(b.ToString()))).Replace("-","");}
 }
 public enum InputKind {Release,Press,Slide}
 public sealed class InputEvent {public int At;public int SourceTime;public int Lane;public int Channel;public int Group;public InputKind Kind;}
 public sealed class InputPlan {
  public List<InputEvent> Events=new List<InputEvent>();public int Cursor;public int Sent;public int Skipped;public int LastError;
  private readonly Dictionary<int,int> held=new Dictionary<int,int>();
  public bool HasHeld=>held.Count>0;
  public static InputPlan Build(Chart chart,int jitter,int offset,int seed) {
   if(!RhythmControl.ValidSettings(jitter,offset))throw new ArgumentException("随机偏移为0–200ms，整体校准为-500–500ms。");
   if(chart==null||chart.Notes==null||chart.Notes.Count==0||chart.Notes.Count>20000||chart.Notes.Any(n=>n==null)||chart.Notes.Select(n=>n.Id).Distinct().Count()!=chart.Notes.Count)throw new ArgumentException("谱面音符为空、重复或超过上限。");
   var groups=new List<Tuple<int,int,int,int,bool>>();
   foreach(var n in chart.Notes){
    if((n.Lane!=1&&n.Lane!=2)||n.Time<0||n.Time>1800000||n.Extra==null||n.Extra.Any(e=>e==null||e.Lane!=n.Lane||e.Time<n.Time||e.Time>1800000))throw new ArgumentException("谱面坐标无效。");
    if(n.Type==1)groups.Add(Tuple.Create(n.Time,n.Time+5,n.Lane,0,false));
    else if(n.Type==20)groups.Add(Tuple.Create(n.Time,n.Time+12,n.Lane,0,true));
    else if(n.Type==10){if(n.Extra.Count==0||n.Extra.Last().Time<=n.Time)throw new ArgumentException("长按缺少终点。");groups.Add(Tuple.Create(n.Time,n.Extra.Max(e=>e.Time)+20,n.Lane,1,false));}
    else if(n.Type==30){foreach(var e in new[]{new Coord{Lane=n.Lane,Time=n.Time}}.Concat(n.Extra))groups.Add(Tuple.Create(e.Time,e.Time+5,e.Lane,0,false));}
    else throw new ArgumentException("不支持的音符类型："+n.Type);
   }
   var rng=new Random(seed);var p=new InputPlan();int groupId=0;
   foreach(var lane in groups.GroupBy(g=>Tuple.Create(g.Item3,g.Item4))){
    var sorted=lane.OrderBy(g=>g.Item1).ToArray();if(sorted.Select(g=>g.Item1).Distinct().Count()!=sorted.Length)throw new ArgumentException("同轨音符重叠，无法保证正常按下/松开顺序。");int previousEnd=int.MinValue;
    for(int i=0;i<sorted.Length;i++){
     var g=sorted[i];int low=Math.Max(g.Item1-jitter+offset,previousEnd+1);int high=g.Item1+jitter+offset;
     if(i+1<sorted.Length)high=Math.Min(high,sorted[i+1].Item1+offset-(g.Item4==1?1:g.Item2-g.Item1+1));
     if(low>high)throw new ArgumentException("同轨音符重叠，无法保证正常按下/松开顺序。");
     int at=Math.Max(low,Math.Min(high,g.Item1+offset+rng.Next(-jitter,jitter+1)));
     int end=g.Item4==1?Math.Max(g.Item2+offset,at+1):at+(g.Item2-g.Item1);
     if(i+1<sorted.Length&&end>=sorted[i+1].Item1+offset)throw new ArgumentException("同轨长按重叠。");
     int id=++groupId;p.Events.Add(new InputEvent{At=at,SourceTime=g.Item1,Lane=g.Item3,Channel=g.Item4,Group=id,Kind=InputKind.Press});
     if(g.Item5)p.Events.Add(new InputEvent{At=at+8,SourceTime=g.Item1,Lane=g.Item3,Channel=g.Item4,Group=id,Kind=InputKind.Slide});
     p.Events.Add(new InputEvent{At=end,SourceTime=g.Item2,Lane=g.Item3,Channel=g.Item4,Group=id,Kind=InputKind.Release});previousEnd=end;
    }
   }
   p.Events=p.Events.OrderBy(e=>e.At).ThenBy(e=>e.Kind).ToList();return p;
  }
  // Only due events are consumed. Pauses leave the cursor and sampled offsets unchanged.
  public void Advance(int now,Action<InputEvent> send){
   while(Cursor<Events.Count&&Events[Cursor].At<=now){var e=Events[Cursor++];int key=e.Lane*10+e.Channel;
    if(e.Kind==InputKind.Press){if(now-e.At>150){Skipped++;continue;}if(held.ContainsKey(key))throw new InvalidOperationException("未释放的同轨输入。");send(e);held[key]=e.Group;Sent++;LastError=now-e.SourceTime;}
    else if(held.TryGetValue(key,out var group)&&group==e.Group){send(e);if(e.Kind==InputKind.Release)held.Remove(key);}
   }
  }
  public void Release(Action<InputEvent> send){foreach(var key in held.Keys.ToArray()){send(new InputEvent{Lane=key/10,Channel=key%10,Kind=InputKind.Release});held.Remove(key);}}
  public void SkipBefore(int now){while(Cursor<Events.Count&&Events[Cursor].At<now){if(Events[Cursor].Kind==InputKind.Press)Skipped++;Cursor++;}}
 }
}
