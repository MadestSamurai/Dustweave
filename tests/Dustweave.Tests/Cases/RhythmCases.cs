extern alias rhythm;
using System.Text.Json;
using R = rhythm::BD2Rhythm;
using Dustweave;
static class RhythmCases {
 public static void Run(string output,List<string> cases){
  void Check(bool value,string name){if(!value)throw new Exception(name);cases.Add(name);}
  string root=Path.Combine(output,"rhythm-storage"),legacy=Path.Combine(root,"legacy"),data=Path.Combine(root,"tools","rhythm");Directory.CreateDirectory(legacy);
  File.WriteAllText(Path.Combine(legacy,"settings.json"),JsonSerializer.Serialize(new R.RhythmSettings{JitterMs=25,OffsetMs=-10}));
  File.WriteAllText(Path.Combine(legacy,"control.json"),"old control");File.WriteAllText(Path.Combine(legacy,"unknown.json"),"preserve");
  var settings=R.RhythmStorage.LoadSettings(data,legacy);
  Check(settings.JitterMs==25&&settings.OffsetMs==-10&&File.Exists(Path.Combine(data,"settings.json")),"rhythm imports valid timing preferences once");
  File.WriteAllText(Path.Combine(data,"settings.json"),JsonSerializer.Serialize(new R.RhythmSettings{JitterMs=75,OffsetMs=100}));
  Check(R.RhythmStorage.LoadSettings(data,legacy).JitterMs==75,"rhythm keeps newer host preferences");
  File.WriteAllText(Path.Combine(legacy,"settings.json"),"{\"JitterMs\":900}");
  Check(R.RhythmStorage.LoadSettings(Path.Combine(root,"invalid"),legacy).JitterMs==50,"rhythm rejects invalid legacy settings");
  DateTime old=DateTime.UtcNow.AddDays(-8);var log=Path.Combine(legacy,"runtime.log");File.WriteAllText(log,"synthetic");File.SetLastWriteTimeUtc(log,old);
  string diagnostics=Path.Combine(data,"diagnostics");R.RhythmStorage.ImportLegacyLogs(legacy,diagnostics);
  var moved=Directory.GetFiles(diagnostics).Single();
  Check(!File.Exists(log)&&File.GetLastWriteTimeUtc(moved)==old&&File.ReadAllText(moved)=="synthetic","rhythm log migration preserves contents and retention age");
  R.RhythmStorage.ImportLegacyLogs(legacy,diagnostics);
  Check(Directory.GetFiles(diagnostics).Length==1&&File.Exists(Path.Combine(legacy,"control.json"))&&File.Exists(Path.Combine(legacy,"unknown.json")),"rhythm log migration is idempotent and leaves transport and unknown files alone");
  var plan=DailyLogCleanup.Scan(root,new DailyLogPolicy(),DateTime.UtcNow);
  Check(plan.Units.Any(u=>u.Files.Any(f=>f.Path==moved)),"host log cleanup includes imported rhythm diagnostics");
  Check(DailyToolCatalog.Find("rhythm").Repository=="Dustweave"&&DailySuite.PreparesInWindow("rhythm"),"rhythm is an owned window-first tool");
  var chart=new R.Chart{Name="synthetic",Notes=[new(){Id=1,Type=1,Lane=1,Time=100},new(){Id=2,Type=10,Lane=2,Time=200,Extra=[new(){Lane=2,Time=400}]}]};
  var input=R.InputPlan.Build(chart,0,0,1);var sent=new List<R.InputEvent>();
  input.Advance(100,sent.Add);Check(input.Sent==1&&input.HasHeld,"rhythm dispatches due notes only");
  input.Advance(200,sent.Add);int cursor=input.Cursor;input.Advance(200,sent.Add);Check(input.Cursor==cursor&&input.Sent==2,"rhythm pause retains timeline and avoids replay");
  input.Release(sent.Add);Check(!input.HasHeld&&sent.Last().Kind==R.InputKind.Release,"rhythm stop releases held input");
 }
}
