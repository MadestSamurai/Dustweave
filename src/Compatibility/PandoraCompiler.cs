using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Dustweave.Compatibility;

/// <summary>Resolve the installed client's UI/table contracts without shipping a client symbol map.</summary>
public static class PandoraCompiler
{
    public static string Fingerprint => MetadataIndex.Hash("pandora-public-v1|" + typeof(PandoraCompiler).Module.ModuleVersionId);
    public static byte[] Prepare(string managed)
    {
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managed);
        using var module = ModuleDefinition.ReadModule(Path.Combine(managed, "Assembly-CSharp.dll"), new ReaderParameters { AssemblyResolver = resolver });
        var names = new Dictionary<string, string>();
        TypeDefinition T(string name) => module.Types.Single(t => t.FullName == name);
        MethodDefinition M(TypeDefinition t, string name) => t.Methods.Single(m => m.Name == name);
        IEnumerable<MethodReference> Calls(MethodDefinition m) => m.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>();
        void Method(string key, MethodReference m) => names.Add(key, m.DeclaringType.FullName.Replace('/', '+') + "|" + m.Name + "|" + m.Parameters.Count);
        void Field(string key, FieldReference f) => names.Add(key, f.Name);
        FieldReference ArgumentField(MethodDefinition m, OpCode arg) => (FieldReference)m.Body.Instructions.Single(i => i.OpCode == OpCodes.Stfld && i.Previous.OpCode == arg).Operand;

        var manager = T("EventMiniGameSurvivalManager");
        Field("uiAnimation", M(T("UIBase"), "get_IsPlayingAnimation").Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().Single());
        Field("hubEntries", T("MiniGameHubUI").Fields.Single(f => f.FieldType is GenericInstanceType g && g.GenericArguments.Count == 1 && g.GenericArguments[0].Name == "EventScheduleDBInfo"));
        Method("hubTable", Calls(M(T("MiniGameHubScrollItem"), "UpdateData")).Single(m => m.ReturnType.Name == "PackEventMiniGameTable"));
        names.Add("survivalKind", Convert.ToInt32(module.Types.Single(t => t.IsEnum && t.Fields.Any(f => f.Name == "SichuanMahjong")).Fields.Single(f => f.Name == "SurvivorsLikes").Constant).ToString());
        var player = M(manager, "get_Player").ReturnType.Resolve();
        var provider = Calls(M(manager, "GetFieldMiniGameSurvivalTable")).Single(m => m.ReturnType.Name == "FieldMiniGameSurvivalTable").DeclaringType.Resolve();
        MethodDefinition Table(string result, int count) => provider.Methods.Single(m => m.IsStatic && m.ReturnType.Name == result && m.Parameters.Count == count);
        MethodDefinition List(string item, int count) => provider.Methods.Single(m => m.IsStatic && m.Parameters.Count == count && m.ReturnType is GenericInstanceType g && g.GenericArguments.Count == 1 && g.GenericArguments[0].Name == item);
        Method("groups", List("FieldMiniGameMapGroupTable", 1));
        Method("maps", List("FieldMiniGameMapTable", 1));
        Method("skillGroup", Table("FieldMiniGameSkillGroupTable", 1));
        Method("skill", Table("FieldMiniGameSkillTable", 2));
        Method("skillGroups", List("FieldMiniGameSkillGroupTable", 0));
        Method("item", Table("FieldMiniGameSurvivalItemTable", 1));
        // Observe applied effects, not removal from the item pool (which also means expiry).
        Method("itemApplied", player.Methods.Single(m => m.HasBody && m.ReturnType.Name == "Void" && m.Parameters.Count == 1 &&
            m.Parameters[0].ParameterType.Name == "Int32" && Calls(m).Any(c => c.Name == "get_ItemType") && Calls(m).Any(c => c.Name == "StartCoroutine")));
        Field("itemTarget", ArgumentField(M(T("SurvivalItem"), "SetTarget"), OpCodes.Ldarg_1));
        var ultimateButton = T("UltimateSkillButton");
        Field("ultimateEnd", M(ultimateButton, "OnClickUI").Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().Single(f => f.FieldType.Name == "Int64"));
        Method("ultimateInfo", Calls(M(ultimateButton, "Initialize")).Single(m => m.DeclaringType.FullName == player.FullName && m.Parameters.Count == 2 && m.Parameters.All(p => p.ParameterType is ByReferenceType)));
        foreach (var item in new[] { ("MgsiHp", 0), ("MgsiMagnet", 1), ("MgsiBomb", 2), ("MgsiExp", 3), ("MgsiCoin", 4), ("MgsiSkill", 5), ("MgsiEmp", 6) })
            if (Convert.ToInt32(T("Proto.Net.Define_MiniGameSurvivalItemType").Fields.Single(f => f.Name == item.Item1).Constant) != item.Item2)
                throw new InvalidOperationException("Pandora item meanings changed");
        var map = T("SurvivalMapSelectUI");
        var open = M(map, "Open");
        Field("mapIndex", open.Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().Last(f => f.FieldType.Name == "Int32"));
        var setMap = M(map, "SetMap");
        Field("mapGroup", (FieldReference)setMap.Body.Instructions.Single(i => i.OpCode == OpCodes.Stfld && i.Previous.Operand is MethodReference m && m.Name == "get_Id").Operand);
        Field("difficulty", map.Fields.Single(f => f.FieldType.Name == "SurvivalDifficultyButton" && !f.Name.StartsWith('_')));
        Field("difficultyId", ArgumentField(M(T("SurvivalDifficultyButton"), "Intialize"), OpCodes.Ldarg_1));
        var difficultySetup = map.Methods.Single(m => m.Parameters.Count == 2 && m.Parameters[1].ParameterType.FullName.Contains("FieldMiniGameMapTable"));
        Method("unlocked", Calls(difficultySetup).First(m => m.ReturnType.Name == "Boolean"));
        Method("cleared", Calls(M(map, "OnClickUI")).Single(m => m.ReturnType.Name == "Boolean" && m.Parameters.Count == 2 && m.Parameters.All(p => p.ParameterType.Name == "Int32")));
        Method("localText", Calls(setMap).First(m => m.IsStaticCall() && m.ReturnType.Name == "String" && m.Parameters.Count == 1));
        Field("panel", T("SurvivalMainUI").Fields.Single(f => f.FieldType.Resolve()?.IsInterface == true && f.FieldType.Resolve().Methods.Any(m => m.Name == "OnClickUI")));
        Field("skillId", ArgumentField(M(T("SurvivalCardItem"), "SetSkill"), OpCodes.Ldarg_1));
        Field("skillLevel", ArgumentField(M(T("SurvivalCardItem"), "SetSkill"), OpCodes.Ldarg_2));
        Field("skillBusy", T("SurvivalSelectUI").Fields.Single(f => f.FieldType.Name == "Boolean"));
        Method("skillApplied", T("SurvivalSelectUI").Methods.Single(m => m.Parameters.Count == 3 && m.Parameters[0].ParameterType is GenericInstanceType g && g.GenericArguments.Count == 1 && g.GenericArguments[0].Name == "MiniGameSurvivalSkillDBInfo"));
        var stat = player.Methods.Single(m => m.ReturnType.Name == "Single" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Resolve() is { IsEnum: true } e && e.Fields.Any(f => f.Name == "MoveSpeed") && m.HasBody && Calls(m).Any(c => c.Name == "get_MoveSpeed"));
        Method("stat", stat);
        var charLookup = stat.Body.Instructions.Single(i => i.Operand is MethodReference m && m.ReturnType.Name == "FieldMiniGameCharTable");
        Field("playerCharGroup", (FieldReference)charLookup.Previous.Previous.Previous.Operand);
        Method("upgradeStat", player.Methods.Single(m => m.ReturnType.Name == "Single" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Resolve() is { IsEnum: true } e && e.Fields.Any(f => f.Name == "ProjectileCount")));
        var hp = player.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).First(i => i.Operand is MethodReference m && m.Name == "get_MiniGameHP");
        Method("health", (MethodReference)hp.Previous.Operand);
        var result = M(T("SurvivalResultUI"), "Initalize");
        // Resolve callback ownership from native assignments, not obfuscated names.
        var retryAction = (FieldReference)result.Body.Instructions.Single(i => i.OpCode == OpCodes.Stfld && i.Previous.Operand == result.Parameters[5]).Operand;
        var pendingAction = M(T("SurvivalResultUI"), "OnRemovedCompletely").Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().Single(f => f.FieldType.FullName == "System.Action");
        Field("resultRetryAction", retryAction);
        Field("resultPendingAction", pendingAction);
        Field("popupOkAction", ArgumentField(M(T("SurvivalDefaultPopupUI"), "SetUI"), OpCodes.Ldarg_3));
        Method("resultRetryConfirmed", T("SurvivalResultUI").Methods.Single(m => m.HasBody && m.Parameters.Count == 0 &&
            m.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference f && f.FullName == retryAction.FullName) &&
            m.Body.Instructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.FullName == pendingAction.FullName) &&
            Calls(m).Any(c => c.Name == "CloseUI")));
        var activeMap = result.Body.Instructions.Single(i => i.Operand is MethodReference m && m.ReturnType.Name == "FieldMiniGameMapTable" && m.Parameters.Count == 2);
        Field("activeStage", (FieldReference)activeMap.Previous.Operand);
        Field("activeGroup", (FieldReference)activeMap.Previous.Previous.Previous.Previous.Operand);
        Method("level", Calls(result).First(m => m.DeclaringType.FullName == player.FullName && m.ReturnType.Name == "Int32"));
        var time = M(manager, "get_TimeManager").ReturnType;
        Method("time", Calls(result).Single(m => m.DeclaringType.FullName == time.FullName && m.ReturnType.Name == "Int64"));
        var owned = player.Methods.Where(m => m.Parameters.Count == 0 && m.ReturnType is GenericInstanceType g && g.ElementType.FullName == "System.Collections.Generic.IEnumerable`1").ToArray();
        if (owned.Length != 2 || ((GenericInstanceType)owned[0].ReturnType).GenericArguments[0].FullName != ((GenericInstanceType)owned[1].ReturnType).GenericArguments[0].FullName) throw new InvalidOperationException("Pandora skill inventory contract changed");
        Method("weapons", owned[0]); Method("passives", owned[1]);
        var ownedType = ((GenericInstanceType)owned[0].ReturnType).GenericArguments[0].Resolve();
        var ctor = ownedType.Methods.Single(m => m.IsConstructor && m.Parameters.Count == 3);
        Field("ownedId", ArgumentField(ctor, OpCodes.Ldarg_1));
        Field("ownedLevel", ArgumentField(ctor, OpCodes.Ldarg_2));
        var patterns = T("SurvivalBossMonster").Methods.Where(m => m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "FieldMiniGameBossPatternTable").ToArray();
        if (patterns.Length != 3) throw new InvalidOperationException("Pandora boss pattern contract changed");
        for (int i = 0; i < patterns.Length; i++) Method("bossPattern" + i, patterns[i]);
        var bossType = T("SurvivalBossMonster");
        var damaged = M(bossType, "Damaged");
        Field("bossHp", (FieldReference)damaged.Body.Instructions.Single(i => i.OpCode == OpCodes.Stfld && i.Previous.OpCode == OpCodes.Sub && ((FieldReference)i.Operand).FieldType.Name == "Single").Operand);
        var spawn = M(bossType, "Initialize"); Method("bossSpawn", spawn);
        // Initialize assigns the same speed argument to base/current speed. The jump
        // coroutine restores current speed from base speed; resolve that data flow.
        var speeds = spawn.Body.Instructions.Where(i => i.OpCode == OpCodes.Stfld && i.Previous.Operand == spawn.Parameters[4]).Select(i => (FieldReference)i.Operand).ToArray();
        if (speeds.Length != 2 || speeds.Any(f => f.FieldType.Name != "Single")) throw new InvalidOperationException("Pandora boss movement contract changed");
        var resets = bossType.NestedTypes.SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
            .Where(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference target && speeds.Any(s => s.FullName == target.FullName) &&
                i.Previous.OpCode == OpCodes.Ldfld && i.Previous.Operand is FieldReference source && speeds.Any(s => s.FullName == source.FullName) && source.FullName != target.FullName)
            .Select(i => (FieldReference)i.Previous.Operand).DistinctBy(f => f.FullName).Single();
        Field("bossMoveSpeed", resets);
        // Resolve native movement rather than writing transforms or combat attributes.
        var avatar = T("PlayerController");
        Field("move", avatar.Fields.Single(f => f.FieldType.Name == "PlayerMoveController"));
        var direction = M(T("MoveController"), "set_MoveDir");
        Method("direction", direction);
        Method("passable", module.Types.SelectMany(t => t.Methods).Single(m => m.IsStatic && m.ReturnType.Name == "Boolean" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "UnityEngine.Vector3" && m.HasBody && Calls(m).Any(c => c.DeclaringType.FullName == "UnityEngine.Bounds" && c.Name == "Contains")));
        // Reflection reads must be validated too: compilation alone cannot verify a property spelling.
        foreach (var contract in new Dictionary<string,string> {
            ["SurvivalMainUI"]="SurvivalTableid", ["EventMiniGameSurvivalManager"]="Player,TimeManager,IsInifityMode",
            ["EventMiniGameManagerBase"]="IsPlaying,IsPause", ["UIBase"]="IsPlayingAnimation,IsAvailableClick",
            ["GameFieldManager"]="Instance,PlayerController,EnterHiddenPackDataParam", ["SurvivalItem"]="FieldRewardObjectId",
            ["Proto.Net.CharDBInfo"]="MiniGameHP,MiniGameMaxHP",
            ["Proto.Net.EventScheduleDBInfo"]="Id,EventId",
            ["Proto.Design.common.PackEventMiniGameTable"]="EventClearType",
            ["UIAnimationController"]="IsPlaying",
            ["Proto.Design.common.FieldMiniGameMapGroupTable"]="Id,MapNameTextId",
            ["Proto.Design.common.FieldMiniGameMapTable"]="Id,StageDifficulty,StageType",
            ["Proto.Design.common.FieldMiniGameSurvivalItemTable"]="ItemType,ItemValue",
            ["Proto.Design.common.FieldMiniGameSkillGroupTable"]="Id,MaxLevel,SkillType,SkillSubType,UpgradeSynergyId,TargetType,UniqueCharGroupId",
            ["Proto.Design.common.FieldMiniGameSkillTable"]="StatType,SkillNameLocalTextId,SkillValue,Cooldown,Duration,AttackRange,AttackSize,AttackCount,Pierce,ProjectileTerm,ProjectileSpeed,KnockbackType",
            ["Proto.Design.common.FieldMiniGameBossPatternTable"]="AttackType,AttackRange,AttackSpeed"
        }) foreach (string property in contract.Value.Split(',')) _ = M(T(contract.Key), "get_" + property);
        foreach (var contract in new Dictionary<string,string> {
            ["SurvivalMainUI"]="_selectMapUI",["SurvivalMenuUI"]="_objEnterButton",["SurvivalMapSelectUI"]="_objNextButton,_objPrevButton,_buttonEnter",
            ["SurvivalCharSelectUI"]="_objEnterButton",["SurvivalSelectUI"]="_slots",["SurvivalDefaultPopupUI"]="_buttonOk",
            ["SurvivalResultUI"]="_objRetryButton,_objExitButton",["SurvivalBossMonster"]="_animator,_dashBox,_jumpCollider",["SurvivalUnit"]="_collider"
        }) foreach (string field in contract.Value.Split(',')) _ = T(contract.Key).Fields.Single(f => f.Name == field);

        var sources = DustweaveSharedResources.Names(typeof(PandoraCompiler).Assembly)
            .Where(n => n.StartsWith("Pandora.") && n.EndsWith(".cs") || n.StartsWith("Hook.Ipc."))
            .Select(n => CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(DailyHookCompiler.Resource(n)), path: n)).ToList();
        sources.Add(CSharpSyntaxTree.ParseText("namespace Dustweave.Pandora.Runtime { static class Bindings { public static readonly System.Collections.Generic.Dictionary<string,string> Names=new System.Collections.Generic.Dictionary<string,string>{" + string.Join(",", names.Select(p => "{" + JsonSerializer.Serialize(p.Key) + "," + JsonSerializer.Serialize(p.Value) + "}")) + "}; } }"));
        sources.Add(CSharpSyntaxTree.ParseText("namespace BD2.LocalIpc { public static class Build { public const string Fingerprint=" + JsonSerializer.Serialize(Fingerprint) + "; } }"));
        var references = new List<MetadataReference>();
        foreach (string file in Directory.EnumerateFiles(managed, "*.dll"))
            try { references.Add(MetadataReference.CreateFromFile(file)); } catch (BadImageFormatException) { }
        references.Add(MetadataReference.CreateFromImage(DailyHookCompiler.Resource("BD2Daily.Harmony.dll")));
        var compilation = CSharpCompilation.Create("Dustweave.Pandora.Runtime." + Fingerprint[..12], sources, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, platform: Platform.X64, deterministic: true));
        using var output = new MemoryStream();
        var emit = compilation.Emit(output, manifestResources: [new ResourceDescription("Pandora.Harmony.dll", () => new MemoryStream(DailyHookCompiler.Resource("BD2Daily.Harmony.dll")), true)]);
        if (!emit.Success) throw new InvalidOperationException(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(25)));
        return output.ToArray();
    }
    private static bool IsStaticCall(this MethodReference method) => !method.HasThis;
}
