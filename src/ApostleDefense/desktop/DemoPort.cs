namespace BD2ApostleDefense.Desktop;
public sealed class DemoPort:IClientPort
{
 public static Snapshot Demo()
 {
  var s=new Snapshot{ProcessId=42,ProcessStart=123,Account=new('a',64),Name="演示账号",At=DateTime.UtcNow.Ticks,Ready=true,MyView=true,Stage="playing",Wave=19,Gold=186,EnemyCount=27,SecondsLeft=24,AchievementsKnown=true,ClearTarget=1,RareProgress=1,RareTarget=3,Room="demo"};
  s.Catalog=new(){GameOverCount=100,SummonCost=25,MaxGrade=8,MaxUpgrade=99,Advantage=1,Penalty=.5,Key="demo",Units=new[]{new UnitDef{Id=1,Name="水属性使徒",Grade=5,Element=0,Attack=55,UpAttack=55,Range=7.5,Interval=.8,Weight=144,Sell=50},new UnitDef{Id=2,Name="火属性使徒",Grade=4,Element=1,Attack=75,UpAttack=75,Range=2,Interval=2,Weight=510,Sell=25},new UnitDef{Id=3,Name="暗属性使徒",Grade=6,Element=4,Attack=70,UpAttack=70,Range=4.75,Interval=.5,Weight=75,Sell=100}}};
  s.Boards=Enumerable.Range(0,9).Select(i=>new Board{Id=i,X=(i%3-1)*3.2,Z=(i/3-1)*2.7}).ToArray();s.Path=new[]{new Point{Id=0,X=-5.5,Z=-5},new Point{Id=1,X=5.5,Z=-5},new Point{Id=2,X=5.5,Z=5},new Point{Id=3,X=-5.5,Z=5}};
  s.Units=Enumerable.Range(0,7).Select(i=>new Unit{Index=i+1,Id=i%3+1,Grid=i,Ready=true}).ToArray();s.Levels=new[]{4,2,0,1,6};s.BoardKey=Guards.BoardKey(s);return s;
 }
 public static Snapshot BoardDemo()
 {
  var s=Demo();var boards=new List<Board>();
  for(int column=0;column<10;column++)
  {
   int inset=column is 0 or 9?2:column is 1 or 8?1:0;
   for(int row=inset;row<8-inset;row++)boards.Add(new(){Id=boards.Count+1,X=(column-4.5)*1.084638,Z=(3.5-row)*1.084638});
  }
  s.Boards=boards.ToArray();s.Path=new[]{new Point{X=-3.79632,Z=4.88086},new Point{X=-5.96551,Z=2.71159},new Point{X=-5.96551,Z=-2.71158},new Point{X=-3.79624,Z=-4.88084},new Point{X=3.79623,Z=-4.88085},new Point{X=5.96551,Z=-2.71159},new Point{X=5.96551,Z=2.71159},new Point{X=3.79623,Z=4.88086}};
  var catalog=new List<UnitDef>();
  for(int element=0;element<5;element++)for(int grade=1;grade<=8;grade++)catalog.Add(new(){Id=element*10+grade,Name="",Element=element,Grade=grade,Attack=grade*12,UpAttack=grade*12,Range=grade<5?2:5,Interval=1.5,Sell=25});
  s.Catalog.Units=catalog.ToArray();s.Catalog.MaxGrade=8;
  s.Units=s.Boards.Select((b,i)=>new Unit{Grid=b.Id,Index=i+1,Ready=true,Id=(i%5)*10+(i%6+1)}).ToArray();
  s.Enemies=Enumerable.Range(0,21).Select(i=>new Enemy{Id=i,Hp=100,X=-3.5+i*.34,Z=4.88086}).ToArray();s.EnemyCount=s.Enemies.Length;
  s.Wave=27;s.Gold=37;s.Levels=new[]{6,6,6,9,11};s.BoardKey=Guards.BoardKey(s);return s;
 }
 private readonly Snapshot? fixture;
 public DemoPort(Snapshot? fixture=null){this.fixture=fixture;}
 public GameProcess? Find()=>new(42,123,"demo");public Snapshot? Read()
 {var s=fixture==null?BoardDemo():JsonFiles.Clone(fixture);s.ProcessId=42;s.ProcessStart=123;s.At=DateTime.UtcNow.Ticks;s.Account=new('a',64);s.Name="演示账号";return s;}public Control? LastControl {get;private set;} public void Write(Control c){LastControl=JsonFiles.Clone(c);}
 public Task ConnectAsync(Action<string> progress,CancellationToken cancellation){progress("演示");return Task.CompletedTask;}
}
