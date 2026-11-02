using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
namespace Dustweave.Soul;

// Independent .NET table/board compiler for the calibrated C++ combat kernel.
// Offline only: no game connection, commands, account data or Python runtime.
internal sealed unsafe class NativeBattleKernel : IDisposable
{
    [InlineArray(4)] internal struct I4 { int value; }
    [InlineArray(9)] internal struct I9 { int value; }
    [InlineArray(20)] internal struct I20 { int value; }
    [InlineArray(2)] internal struct I2 { int value; }
    [StructLayout(LayoutKind.Sequential)] internal struct Config { public int Id, Type, Group, Count, Ratio, ResultType, Trigger, Reserved; }
    [StructLayout(LayoutKind.Sequential)] internal struct Effect { public int Group, Id, Count, SubType, Tag, Target, Type, Value, Value2, Reserved; }
    [StructLayout(LayoutKind.Sequential)] internal struct Trigger { public int Id, Timing, Logic, Count; public I2 Types, Values1, Values2; }
    [StructLayout(LayoutKind.Sequential)] internal struct TablesInput {
        public float Stamina, Recovery, FatigueStart, FatigueInterval; public int FatigueDamage; public float FatigueGrowth;
        public int ConfigCount, EffectCount, TriggerCount, Reserved; public Config* Configs; public Effect* Effects; public Trigger* Triggers;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Synergy { public int Type, Target, Notify, Reserved; }
    [StructLayout(LayoutKind.Sequential)] internal struct Reverse { public int Source, Type; }
    [InlineArray(20)] internal struct S20 { Synergy value; }
    [InlineArray(20)] internal struct R20 { Reverse value; }
    [StructLayout(LayoutKind.Sequential)] internal struct Item {
        public int Type, Inventory, Id, Position, ConfigCount; public I4 Configs;
        public int TagCount; public I20 Tags; public int CellCount; public I9 Cells;
        public int Minimum, Maximum, DamageType; public float HitRate, CritChance, CritMultiplier, Cooldown, Stamina;
        public int FloorCount; public I9 Floors; public int EquipmentCount; public I4 Equipment;
        public int SynergyCount; public S20 Synergies; public int ReverseCount; public R20 Reverses;
        public int Target, FloorUnitCount; public I9 FloorUnits; public I4 Reserved;
    }
    [InlineArray(128)] internal struct Item128 { Item value; }
    [StructLayout(LayoutKind.Sequential)] internal struct Team { public int Count, Reserved1, Reserved2, Reserved3; public Item128 Items; }
    [StructLayout(LayoutKind.Sequential)] internal struct Battle { public int BlueHp, RedHp; public float MaxSeconds; public int Reserved; public Team Blue, Red; }
    [StructLayout(LayoutKind.Sequential)] internal struct SideResult {
        public int Hp, MaxHp, Guard; public float Stamina; public int Attacks, Hits; public long Damage; public int StaminaMisses, Reserved;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Result {
        public int Status, Winner; public float Seconds; public int Reserved; public ulong RngDraws; public SideResult Blue, Red;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Abi();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate nint Create(TablesInput* tables);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Destroy(nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Simulate(nint context, Battle* battle, int* seeds, int count, Result* output);
    readonly nint library, context;
    readonly Destroy destroy;
    readonly Simulate simulate;
    readonly Dictionary<int,JsonNode> items, ground, decks;
    readonly Dictionary<(int,int),JsonNode> units, equipment, stages;
    readonly JsonNode defaults;
    internal readonly string KernelSha256;
    internal readonly string TableSha256;
    bool disposed;
    static int N(JsonNode? row,string key,int fallback=0)=>row?[key] is {} n?int.Parse(n.ToString()):fallback;
    static int[] A(JsonNode row,string key)=>(row[key]?.AsArray()??new JsonArray()).Select(n=>int.Parse(n!.ToString())).ToArray();
    static JsonNode[] Load(string root,string name)=>JsonNode.Parse(File.ReadAllText(Path.Combine(root,name+".json")))!.AsArray().Select(n=>n!).ToArray();
    static Dictionary<int,JsonNode> Index(JsonNode[] rows)=>rows.ToDictionary(r=>N(r,"id"));
    static Dictionary<(int,int),JsonNode> Group(JsonNode[] rows)=>rows.ToDictionary(r=>(N(r,"groupId"),N(r,"id")));
    static int Copy(Span<int> target,IEnumerable<int> values) {
        var array=values.ToArray(); if(array.Length>target.Length)throw new InvalidDataException("Native board capacity exceeded"); array.CopyTo(target);return array.Length;
    }
    internal NativeBattleKernel(string root,string dll) {
        items=Index(Load(root,"IBItemTable"));ground=Index(Load(root,"IBGroundItemTable"));decks=Index(Load(root,"IBDeckTable"));
        units=Group(Load(root,"IBUnitItemTable"));equipment=Group(Load(root,"IBEquipItemTable"));stages=Group(Load(root,"IBStageTable"));defaults=Load(root,"IBDefaultTable")[0];
        library=NativeLibrary.Load(Path.GetFullPath(dll));
        T Get<T>(string name) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library,name));
        try {
            destroy=Get<Destroy>("bd2_ib_context_destroy");simulate=Get<Simulate>("bd2_ib_simulate_batch");
            if(Get<Abi>("bd2_ib_native_abi_version")()!=1)throw new InvalidDataException("Unexpected native ABI");
            var cs=Load(root,"IBEffectConfigTable").OrderBy(r=>N(r,"id")).Select(r=>new Config {Id=N(r,"id"),Type=N(r,"effectType"),Group=N(r,"effectGroupId"),Count=N(r,"resultCount",999),Ratio=N(r,"resultRatio",10000),ResultType=N(r,"resultType"),Trigger=N(r,"triggerId")}).ToArray();
            var es=Load(root,"IBEffectTable").OrderBy(r=>N(r,"groupId")).ThenBy(r=>N(r,"id")).Select(r=>new Effect {Group=N(r,"groupId"),Id=N(r,"id"),Count=N(r,"effectCount"),SubType=N(r,"effectSubType"),Tag=N(r,"effectTargetTagId"),Target=N(r,"effectTargetType",1),Type=N(r,"effectType"),Value=N(r,"effectValue"),Value2=N(r,"effectValue2")}).ToArray();
            var ts=Load(root,"IBTriggerTable").OrderBy(r=>N(r,"id")).Select(r=>{
                var t=new Trigger{Id=N(r,"id"),Timing=N(r,"triggerTimingType"),Logic=N(r,"triggerLogicalType",2)};
                t.Count=Copy(t.Types,A(r,"triggerType"));Copy(t.Values1,A(r,"triggerValue1"));Copy(t.Values2,A(r,"triggerValue2"));return t;
            }).ToArray();
            fixed(Config* cp=cs)fixed(Effect* ep=es)fixed(Trigger* tp=ts){
                var input=new TablesInput {Stamina=(float)(N(defaults,"defaultStamina")*.001),Recovery=(float)(N(defaults,"defaultStaminaRecovery")*.001),FatigueStart=(float)(N(defaults,"fatigueStartTime")*.01),FatigueInterval=(float)(N(defaults,"fatigueIntervalTime")*.01),FatigueDamage=N(defaults,"fatigueStartDamage"),FatigueGrowth=(float)(1+N(defaults,"fatigueIntervalAddDamage")*.01),ConfigCount=cs.Length,EffectCount=es.Length,TriggerCount=ts.Length,Configs=cp,Effects=ep,Triggers=tp};
                context=Get<Create>("bd2_ib_context_create")(&input);
                if(context==0)throw new InvalidDataException("Native table context rejected");
            }
            KernelSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll)));
            TableSha256=SoulAssets.TableFingerprint(root);
        }catch {NativeLibrary.Free(library);throw;}
    }
    internal byte[] Compile(JsonNode input) {
        var stage=stages[(N(input["stage"],"groupId",N(input,"stageGroupId",1)),N(input["stage"],"id",N(input,"stageId",1)))];
        var bytes=new byte[sizeof(Battle)];
        fixed(byte* p=bytes){var b=(Battle*)p;b->BlueHp=N(input["blue"],"hp",N(stage,"healthPoint"));b->RedHp=N(input["red"],"hp",N(stage,"enemyHP"));b->MaxSeconds=input["maxSeconds"]?.GetValue<float>()??600;
            CompileTeam(input["blue"],N(defaults,"startDeckId"),ref b->Blue);CompileTeam(input["red"],N(stage,"enemyDeckId"),ref b->Red);}
        return bytes;
    }
    internal Result[] Run(byte[] battle,int[] seeds) {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(battle.Length!=sizeof(Battle)||seeds.Length==0)throw new InvalidDataException("Invalid batch");
        var result=new Result[seeds.Length];fixed(byte* b=battle)fixed(int* s=seeds)fixed(Result* r=result){int code=simulate(context,(Battle*)b,s,seeds.Length,r);if(code!=0||result.Any(x=>x.Status!=0))throw new InvalidOperationException("Native batch failed: "+code);}return result;
    }
    // For one fixed opponent and seed set only. Keep resolved actor order,
    // effects, sockets and graph edges; erase diagnostic IDs/coordinates.
    internal string CombatKey(byte[] battle) {
        if(battle.Length!=sizeof(Battle))throw new InvalidDataException("Invalid battle template");
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        fixed(byte* p=battle){var blue=&((Battle*)p)->Blue;int count=blue->Count;
            hash.AppendData(new ReadOnlySpan<byte>(&count,sizeof(int)));
            for(int n=0;n<count;n++){
                var item=blue->Items[n];item.Inventory=item.Id=item.Position=item.CellCount=0;item.Cells=default;
                hash.AppendData(new ReadOnlySpan<byte>(&item,sizeof(Item)));
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    internal byte[] CompileBlue(byte[] template,IReadOnlyList<Placement> placements) {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(template.Length!=sizeof(Battle))throw new InvalidDataException("Invalid battle template");
        var bytes=(byte[])template.Clone();
        RebuildBlue(bytes,placements);
        return bytes;
    }
    // A planner owns this workspace exclusively. Native simulation reads it
    // without retaining or changing it; every rebuild clears the entire side.
    internal void RebuildBlue(byte[] workspace,IReadOnlyList<Placement> placements) {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(workspace.Length!=sizeof(Battle))throw new InvalidDataException("Invalid battle workspace");
        fixed(byte* p=workspace){var b=(Battle*)p;b->Blue=default;CompileTeam(placements,ref b->Blue);}
    }
    internal static JsonObject Describe(Result result,int seed) {
        JsonObject Side(SideResult s)=>new(){["hp"]=s.Hp,["maxHp"]=s.MaxHp,["energyGuard"]=s.Guard,["stamina"]=s.Stamina,["attacks"]=s.Attacks,["hits"]=s.Hits,["damage"]=s.Damage,["staminaMisses"]=s.StaminaMisses};
        return new(){["seed"]=seed,["winner"]=result.Winner==1?"BLUE":result.Winner==2?"RED":"TIMEOUT",["seconds"]=result.Seconds,["rngDrawCount"]=result.RngDraws,["blue"]=Side(result.Blue),["red"]=Side(result.Red)};
    }
    public void Dispose(){if(disposed)return;disposed=true;destroy(context);NativeLibrary.Free(library);}

    internal record Placement(int Inventory,int Id,int Position,int Rotation,int Level,int Target=-1,int Socket=-1);
    sealed class Node(Placement p,JsonNode item,JsonNode data) {
        internal readonly Placement P=p;internal readonly JsonNode Root=item,Data=data;
        internal int Type=N(item,"itemType"),SortPosition=p.Position;internal int[] Tags=A(item,"itemTag").Distinct().Order().ToArray(),Configs=[];
        internal readonly List<(int,int)> Cells=[];
        internal readonly List<Node> Floors=[],FloorUnits=[],Equipment=[];
        internal readonly List<(int Type,Node Target,bool Notify,int X,int Y)> Synergies=[];
        internal readonly List<(Node Source,int Type)> Reverses=[];internal Node? Target;
    }
    static (int,int) Rotate(int x,int y,int rotation)=>(rotation%4) switch{1=>(-y,x),2=>(-x,-y),3=>(y,-x),_=>(x,y)};
    List<Placement> Placements(JsonNode? spec,int fallback) {
        if(spec?["items"] is JsonArray array)return array.Select((r,n)=>{
            int target=N(r,"targetUnitInventoryIndex",-1),socket=N(r,"targetSlotIndex",-1);
            if((target>=0)!=(socket>=0))throw new InvalidDataException("Incomplete equipment binding");
            int pos=target>=0?-1:N(r,"position",-1);if(target<0&&(pos<0||pos>=49))throw new InvalidDataException("Board position out of range");
            return new Placement(N(r,"inventoryIndex",n+1),N(r,"itemId"),pos,((N(r,"rotation")%4)+4)%4,N(r,"level"),target,socket);
        }).ToList();
        var deck=decks[N(spec,"deckId",fallback)];var ids=A(deck,"useItemId");var positions=A(deck,"invenPosition");var rotations=A(deck,"itemRotate");
        if(ids.Length!=positions.Length)throw new InvalidDataException("Invalid table deck");
        return ids.Select((id,n)=>new Placement(n+1,id,positions[n],n<rotations.Length?rotations[n]:0,0)).ToList();
    }
    void CompileTeam(JsonNode? spec,int fallback,ref Team target) {
        CompileTeam(Placements(spec,fallback),ref target);
    }
    void CompileTeam(IReadOnlyList<Placement> placements,ref Team target) {
        var nodes=placements.Select(p=>{
            var item=items[p.Id];int type=N(item,"itemType");var row=type switch{0=>ground[p.Id],1=>units[(p.Id,p.Level+1)],2=>equipment[(p.Id,p.Level+1)],_=>throw new InvalidDataException("Unknown item type")};
            return new Node(p,item,row);
        }).ToList();
        if(nodes.Select(n=>n.P.Inventory).Distinct().Count()!=nodes.Count)throw new InvalidDataException("Duplicate inventory identity");
        var floorGrid=new Dictionary<(int,int),Node>();var unitGrid=new Dictionary<(int,int),Node>();
        foreach(var node in nodes.Where(n=>n.Type!=2).OrderBy(n=>n.Type)){
            var xs=A(node.Root,"slotPositionX");var ys=A(node.Root,"slotPositionY");if(xs.Length!=ys.Length)throw new InvalidDataException("Invalid item shape");
            var grid=node.Type==0?floorGrid:unitGrid;
            for(int n=0;n<xs.Length;n++){
                var (dx,dy)=Rotate(xs[n],ys[n],node.P.Rotation);var cell=(node.P.Position%7+dx,node.P.Position/7+dy);
                if(cell.Item1<0||cell.Item1>=7||cell.Item2<0||cell.Item2>=7||!grid.TryAdd(cell,node))throw new InvalidDataException("Overlapping or out-of-board item");node.Cells.Add(cell);
                if(node.Type==1){if(!floorGrid.TryGetValue(cell,out var floor))throw new InvalidDataException("Unsupported unit");if(!node.Floors.Contains(floor))node.Floors.Add(floor);if(!floor.FloorUnits.Contains(node))floor.FloorUnits.Add(node);}
            }
            node.Configs=A(node.Data,"itemEffectId");
        }
        var owned=nodes.Where(n=>n.Type==1).ToDictionary(n=>n.P.Inventory);
        var socketPositions=new Dictionary<(int,int),int>();var socketTargets=new Dictionary<int,List<(Node,int)>>();
        foreach(var u in owned.Values){var xs=A(u.Data,"socketSlotPositionX");var ys=A(u.Data,"socketSlotPositionY");if(xs.Length!=ys.Length)throw new InvalidDataException("Invalid sockets");int slot=0;
            for(int n=0;n<xs.Length;n++){if(xs[n]==0&&ys[n]==0)continue;var (dx,dy)=Rotate(xs[n],ys[n],u.P.Rotation);int pos=(u.P.Position/7+dy)*7+u.P.Position%7+dx;socketPositions[(u.P.Inventory,slot)]=pos;if(!socketTargets.ContainsKey(pos))socketTargets[pos]=[];socketTargets[pos].Add((u,slot++));}}
        var usedSockets=new HashSet<(int,int)>();
        foreach(var e in nodes.Where(n=>n.Type==2)){
            Node unit;int slot;
            if(e.P.Target>=0){if(!owned.TryGetValue(e.P.Target,out unit!)||!socketPositions.ContainsKey((e.P.Target,e.P.Socket)))throw new InvalidDataException("Unknown equipment owner or socket");slot=e.P.Socket;}
            else {if(!socketTargets.TryGetValue(e.P.Position,out var matches)||matches.Count!=1)throw new InvalidDataException("Ambiguous native equipment position");(unit,slot)=matches[0];}
            if(!usedSockets.Add((unit.P.Inventory,slot)))throw new InvalidDataException("Equipment socket occupied");
            e.Target=unit;unit.Equipment.Add(e);e.SortPosition=socketPositions[(unit.P.Inventory,slot)];e.Configs=A(e.Data,unit.Tags.Contains(N(e.Data,"targetTagId"))?"matchEffectId":"defaultEffectId");
        }
        nodes=nodes.OrderBy(n=>n.Type).ThenBy(n=>n.SortPosition).ToList();var indices=nodes.Select((n,i)=>(n,i)).ToDictionary(p=>p.n,p=>p.i);
        foreach(var u in nodes.Where(n=>n.Type==1)){
            var xs=A(u.Data,"slotPositionX");var ys=A(u.Data,"slotPositionY");var types=A(u.Data,"slotType");
            var chosen=new Dictionary<Node,(int Type,Node Target,bool Notify,int X,int Y)>();
            for(int n=0;n<Math.Min(types.Length,Math.Min(xs.Length,ys.Length));n++){
                int type=types[n];if(type<1||type>3)continue;var (dx,dy)=Rotate(xs[n],ys[n],u.P.Rotation);var cell=(u.P.Position%7+dx,u.P.Position/7+dy);
                if(u.Cells.Contains(cell))continue;var other=unitGrid.GetValueOrDefault(cell)??floorGrid.GetValueOrDefault(cell);if(other is null)continue;
                var tags=A(u.Data,$"slotType{type}ActiveTag");int mode=N(u.Data,$"slotType{type}ActiveTagType");bool empty=tags.Contains(999);
                bool matches=empty?floorGrid.ContainsKey(cell)&&!unitGrid.ContainsKey(cell):tags.Length>0&&(mode==1?tags.All(other.Tags.Contains):mode==2&&tags.Any(other.Tags.Contains));if(!matches)continue;
                var link=(type,other,!empty,dx,dy);
                if(empty)u.Synergies.Add(link);else if(!chosen.TryGetValue(other,out var old)||(dy,dx).CompareTo((old.Y,old.X))<0)chosen[other]=link;
            }
            u.Synergies.AddRange(chosen.Values);
            foreach(var s in u.Synergies)if(s.Target.Type==1)s.Target.Reverses.Add((u,s.Type));
        }
        if(nodes.Count>128)throw new InvalidDataException("Team exceeds native capacity");target.Count=nodes.Count;
        for(int n=0;n<nodes.Count;n++){
            var src=nodes[n];ref var row=ref target.Items[n];row.Type=src.Type;row.Inventory=src.P.Inventory;row.Id=src.P.Id;row.Position=src.P.Position;row.Target=-1;
            row.ConfigCount=Copy(row.Configs,src.Configs);row.TagCount=Copy(row.Tags,src.Tags);row.CellCount=Copy(row.Cells,src.Cells.Select(c=>c.Item2*7+c.Item1));
            if(src.Type==0)row.FloorUnitCount=Copy(row.FloorUnits,src.FloorUnits.Select(u=>indices[u]));
            else if(src.Type==2)row.Target=indices[src.Target!];
            else {
                row.Minimum=N(src.Data,"minDamage");row.Maximum=N(src.Data,"maxDamage");row.DamageType=N(src.Data,"damageType");row.HitRate=(float)(N(src.Data,"hitRate")/100.0);row.CritMultiplier=1.5f;row.Cooldown=N(src.Data,"coolTime")/1000f;row.Stamina=N(src.Data,"useStamina")*.001f;
                row.FloorCount=Copy(row.Floors,src.Floors.Select(u=>indices[u]));row.EquipmentCount=Copy(row.Equipment,src.Equipment.Select(u=>indices[u]));
                if(src.Synergies.Count>20||src.Reverses.Count>20)throw new InvalidDataException("Synergy capacity exceeded");
                row.SynergyCount=src.Synergies.Count;for(int j=0;j<src.Synergies.Count;j++){var s=src.Synergies[j];row.Synergies[j]=new Synergy{Type=s.Type,Target=indices[s.Target],Notify=s.Notify?1:0};}
                row.ReverseCount=src.Reverses.Count;for(int j=0;j<src.Reverses.Count;j++){var s=src.Reverses[j];row.Reverses[j]=new Reverse{Source=indices[s.Source],Type=s.Type};}
            }
        }
    }
}
