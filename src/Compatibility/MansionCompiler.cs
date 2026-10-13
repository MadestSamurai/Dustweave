using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Dustweave.Compatibility;
/// <summary>Resolve semantic anchors from the installed client, never a release-time symbol map.</summary>
public static class MansionCompiler
{
    public static string Fingerprint => MetadataIndex.Hash("mansion-v2|" + typeof(MansionCompiler).Module.ModuleVersionId);

    public static byte[] Prepare(string managed)
    {
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managed);
        using var module = ModuleDefinition.ReadModule(Path.Combine(managed, "Assembly-CSharp.dll"), new ReaderParameters { AssemblyResolver = resolver });
        TypeDefinition Type(string name) => module.Types.Single(t => t.FullName == name);
        var names = new Dictionary<string, string>();
        void Add(string key, string value) => names.Add(key, value);
        FieldDefinition Field(TypeDefinition t, Func<FieldDefinition, bool> test) => t.Fields.Single(test);
        bool EnumHas(TypeReference t, string value) => t.Resolve()is { IsEnum: true } e && e.Fields.Any(f => f.Name == value);
        FieldDefinition Stored(TypeDefinition t, string method)
        {
            var body = t.Methods.Single(m => m.Name == method).Body.Instructions;
            var writes = body.Where(i => i.OpCode == OpCodes.Stfld).Select(i => ((FieldReference)i.Operand).Resolve()).ToList();
            foreach (var call in body.Select(i => i.Operand).OfType<MethodReference>().Where(m => m.DeclaringType.FullName == t.FullName && m.Resolve().IsSetter))
                writes.AddRange(call.Resolve().Body.Instructions.Where(i => i.OpCode == OpCodes.Stfld).Select(i => ((FieldReference)i.Operand).Resolve()));
            return writes.Distinct().Single();
        }

        var manager = Type("gamfs.PackMan.PackManManager");
        var player = Type("gamfs.PackMan.PackManCharPlayer");
        var chaser = Type("gamfs.PackMan.PackManCharChaser");
        var ruler = manager.Fields.Select(f => f.FieldType.Resolve()).Single(t => t != null && t.Fields.Any(f => f.FieldType.FullName == "System.Collections.Generic.List`1<gamfs.PackMan.PackManCharChaser>"));
        Add("ruler", Field(manager, f => f.FieldType.FullName == ruler.FullName).Name);
        Add("managerState", Field(manager, f => EnumHas(f.FieldType, "Entering")).Name);
        Add("playState", Field(ruler, f => EnumHas(f.FieldType, "Caught")).Name);
        Add("health", Stored(player, "ConsumeHealth").Name);
        Add("speedMultiplier", Stored(player, "SetSpeedMultiplier").Name);
        Add("invincible", Stored(player, "SetInvincible").Name);
        Add("chaserKind", Field(chaser, f => EnumHas(f.FieldType, "Stalking")).Name);
        var destinationClock = chaser.Methods.Single(m => m.HasBody && m.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference f && f.Name == "_destinationUpdateInterval"));
        Add("destinationElapsed", destinationClock.Body.Instructions.Where(i => i.OpCode == OpCodes.Stfld).Select(i => (FieldReference)i.Operand).DistinctBy(f => f.FullName).Single().Name);
        Add("chaserBait", chaser.Methods.Single(m => m.Name == "ClearItemChaseDestination").Body.Instructions.Where(i => i.OpCode == OpCodes.Stfld).Select(i => (FieldReference)i.Operand).Single(f => f.FieldType.FullName == "System.Boolean").Name);
        var behaviorField = Field(chaser, f => f.FieldType.Resolve()is { } t && t.Fields.Any(x => EnumHas(x.FieldType, "Suspicious")));
        Add("behavior", behaviorField.Name);
        var behavior = behaviorField.FieldType.Resolve();
        Add("behaviorState", Field(behavior, f => EnumHas(f.FieldType, "Suspicious")).Name);
        var elapsed = behavior.Methods.Single(m => m.HasBody && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Single");
        Add("behaviorElapsed", ((FieldReference)elapsed.Body.Instructions.First(i => i.OpCode == OpCodes.Stfld).Operand).Name);
        Add("coinCollected", Stored(Type("gamfs.PackMan.PackManObjectCoin"), "TryCollect").Name);
        var beginStage = ruler.Methods.Single(m => m.HasBody && m.Body.Instructions.Any(i => i.Operand is string text && text.Contains("started. coinCount:")));
        Add("ordinaryCoins", beginStage.Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().Single(f => f.FieldType.FullName == "System.Collections.Generic.Dictionary`2<UnityEngine.Vector2Int,gamfs.PackMan.PackManObjectCoin>").Name);
        Add("exitActive", Stored(Type("gamfs.PackMan.PackManObjectExit"), "Deactivate").Name);
        Add("itemCollected", Stored(Type("gamfs.PackMan.PackManObjectItem"), "TryCollect").Name);
        var itemEnum = Type("gamfs.PackMan.PackManManager").Methods.Single(m => m.Name == "NotifyItemInventoryChanged").Parameters[0].ParameterType as GenericInstanceType;
        string itemType = itemEnum!.GenericArguments[0].FullName;
        Add("itemKind", Field(Type("gamfs.PackMan.PackManObjectItem"), f => f.FieldType.FullName == itemType).Name);
        var itemRulerField = Field(ruler, f => f.FieldType.Resolve()is { } t && t.Fields.Any(x => x.FieldType.FullName == "System.Collections.Generic.List`1<gamfs.PackMan.PackManObjectItem>"));
        Add("items", itemRulerField.Name);
        var itemRuler = itemRulerField.FieldType.Resolve();
        var itemData = module.Types.SelectMany(t => t.NestedTypes).Single(t => t.Methods.Any(m => m.IsConstructor && m.Parameters.Any(p => p.ParameterType.Name == "PackManItemTable")));
        var itemConstructor = itemData.Methods.Single(m => m.IsConstructor && m.Parameters.Any(p => p.ParameterType.Name == "PackManItemTable"));
        foreach (var field in new[]
        {
            ("itemRange", "get_EffectRangeTile"),
            ("itemDuration", "get_EffectDurationMs"),
            ("itemValue", "get_EffectValue")
        }

        )
        {
            var read = itemConstructor.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == field.Item2);
            Add(field.Item1, ((FieldReference)itemConstructor.Body.Instructions.SkipWhile(i => i != read).First(i => i.OpCode == OpCodes.Stfld).Operand).Name);
        }

        Add("itemRules", Field(itemRuler, f => f.FieldType.FullName == itemData.DeclaringType.FullName).Name);
        Add("itemLookup", itemData.DeclaringType.Methods.Single(m => m.ReturnType.FullName == itemData.FullName && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == itemType).Name);
        var slots = Field(itemRuler, f => f.FieldType is ArrayType a && a.ElementType.Resolve().Fields.Any(x => x.FieldType.FullName == itemType));
        Add("slots", slots.Name);
        var slot = ((ArrayType)slots.FieldType).ElementType.Resolve();
        Add("slotKind", Field(slot, f => f.FieldType.FullName == itemType).Name);
        Add("slotActive", slot.Properties.Single(p => p.PropertyType.FullName == "System.Boolean").GetMethod.Name);
        var mode = Field(manager, f => f.FieldType.Resolve()is { } t && t.Fields.Any(x => EnumHas(x.FieldType, "Challenge")));
        Add("mode", mode.Name);
        Add("modeType", Field(mode.FieldType.Resolve(), f => EnumHas(f.FieldType, "Challenge")).Name);
        var hud = Type("MansionRunawayHUD");
        var description = hud.Methods.Single(m => m.Name == "OnStageDescription");
        var stageProperty = description.Body.Instructions.SkipWhile(i => i.Operand is not FieldReference f || f.Name != "_textStageCount").Select(i => i.Operand).OfType<MethodReference>().First(m => m.DeclaringType.FullName == description.Parameters[0].ParameterType.FullName).Resolve();
        string stageName = stageProperty.DeclaringType.Properties.Single(p => p.GetMethod == stageProperty).Name;
        var stageGetter = mode.FieldType.Resolve().Properties.Single(p => p.Name == stageName).GetMethod;
        Add("stage", stageGetter.Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().Single().Name);
        var unlimited = mode.FieldType.Resolve().Properties.Single(p => p.PropertyType.FullName == "System.Boolean" && p.GetMethod.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().Any(m => m.ReturnType.FullName == "System.Int32" && m.DeclaringType.FullName == mode.FieldType.FullName));
        Add("maxStage", unlimited.GetMethod.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().Single(m => m.ReturnType.FullName == "System.Int32").Resolve().Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().Single().Name);
        var timerCaller = hud.Methods.First(m => m.HasBody && m.Body.Instructions.Any(i => i.Operand is MethodReference f && f.DeclaringType.Name == "PackManTimerObject" && f.Name == "SetTime"));
        var timerCall = timerCaller.Body.Instructions.First(i => i.Operand is MethodReference f && f.DeclaringType.Name == "PackManTimerObject" && f.Name == "SetTime");
        var timeGetter = ((MethodReference)timerCall.Previous.Operand).Resolve();
        string timeName = timeGetter.DeclaringType.Properties.Single(p => p.GetMethod == timeGetter).Name;
        Add("timeRemaining", ruler.Properties.Single(p => p.Name == timeName).GetMethod.Name);
        var chainChange = ruler.Methods.Single(m => m.HasBody && m.Body.Instructions.Any(i => i.Operand is MethodReference f && f.Name == "RecordScore") && m.Body.Instructions.Any(i => i.Operand is MethodReference f && f.Name == "NotifyChainChanged"));
        Add("chain", chainChange.Body.Instructions.Where(i => i.OpCode == OpCodes.Stfld).Select(i => (FieldReference)i.Operand).Single(f => f.FieldType.FullName == "System.Int32").Name);
        Add("chainRemaining", chainChange.Body.Instructions.Where(i => i.OpCode == OpCodes.Stfld).Select(i => (FieldReference)i.Operand).Single(f => f.FieldType.FullName == "System.Single").Name);
        var chainTimerWrite = chainChange.Body.Instructions.Single(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.FieldType.FullName == "System.Single");
        var chainWindow = chainChange.Body.Instructions.TakeWhile(i => i != chainTimerWrite).Select(i => i.Operand).OfType<MethodReference>().Last(m => m.ReturnType.FullName == "System.Single" && m.Parameters.Count == 0);
        Add("chainRules", Field(ruler, f => f.FieldType.FullName == chainWindow.DeclaringType.FullName).Name);
        Add("chainWindow", chainWindow.Name);
        Add("rareRewardLookup", chainWindow.DeclaringType.Resolve().Methods.Single(m => m.ReturnType.FullName == "System.Boolean" && m.Parameters.Count == 2 && m.Parameters[0].ParameterType.FullName == "System.Int32" && m.Parameters[1].ParameterType.FullName == "System.Int32&").Name);
        var recordMethod = manager.Methods.Single(m => m.Name == "RecordScore");
        var scoreField = recordMethod.Body.Instructions.Select(i => i.Operand).OfType<FieldReference>().First();
        Add("scoreRecord", scoreField.Name);
        var recordType = scoreField.FieldType.Resolve();
        var scoreGetter = recordMethod.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().Single(m => m.DeclaringType.FullName == scoreField.FieldType.FullName && m.ReturnType.FullName == "System.Int64");
        Add("score", scoreGetter.Name);
        var exportRecord = recordType.Methods.Single(m => m.ReturnType.Name == "MiniGamePackManPlayRecordInfo");
        var maxChainSet = exportRecord.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "set_MaxChainCount");
        var chainGetter = exportRecord.Body.Instructions.TakeWhile(i => i != maxChainSet).Select(i => i.Operand).OfType<MethodReference>().Last(m => m.DeclaringType.FullName == recordType.FullName);
        Add("maxChain", chainGetter.Name);
        var sourceNames = DustweaveSharedResources.Names(typeof(MansionCompiler).Assembly).Where(n => n.StartsWith("Mansion.") && n.EndsWith(".cs") || n.StartsWith("Hook.Ipc.")).ToArray();
        var sources = sourceNames.Select(n => CSharpSyntaxTree.ParseText(Encoding.UTF8.GetString(DailyHookCompiler.Resource(n)), path: n)).ToList();
        sources.Add(CSharpSyntaxTree.ParseText(EntrySource(module), path: "Mansion.EntryNative.generated.cs"));
        sources.Add(CSharpSyntaxTree.ParseText("namespace Dustweave.Mansion.Runtime { static class Bindings { public static readonly System.Collections.Generic.Dictionary<string,string> Names = new System.Collections.Generic.Dictionary<string,string>{" + string.Join(",", names.Select(p => "{" + JsonSerializer.Serialize(p.Key) + "," + JsonSerializer.Serialize(p.Value) + "}")) + "}; } }"));
        sources.Add(CSharpSyntaxTree.ParseText("namespace BD2.LocalIpc { public static class Build { public const string Fingerprint=" + JsonSerializer.Serialize(Fingerprint) + "; } }"));
        var refs = new List<MetadataReference>();
        foreach (var file in Directory.EnumerateFiles(managed, "*.dll"))
            try
            {
                refs.Add(MetadataReference.CreateFromFile(file));
            }
            catch (BadImageFormatException)
            {
            }

        refs.Add(MetadataReference.CreateFromImage(DailyHookCompiler.Resource("BD2Daily.Harmony.dll")));
        var compilation = CSharpCompilation.Create("Dustweave.Mansion.Runtime." + Fingerprint[..12], sources, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, platform: Platform.X64, deterministic: true));
        using var output = new MemoryStream();
        var result = compilation.Emit(output, manifestResources: [new ResourceDescription("Mansion.Harmony.dll", () => new MemoryStream(DailyHookCompiler.Resource("BD2Daily.Harmony.dll")), true)]);
        if (!result.Success)
            throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(25)));
        return output.ToArray();
    }

    static string EntrySource(ModuleDefinition module)
    {
        // The native Start handler owns the schedule check. Resolve it from
        // semantic calls, not this client's obfuscated names or an event ID.
        var main = module.Types.Single(t => t.FullName == "MansionRunawayMainUI");
        var start = main.Methods.Single(m => m.HasBody && m.Body.Instructions.Any(i =>
            i.OpCode == OpCodes.Ldc_I4 && i.Operand is int n && n == 60110));
        var calls = start.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().Select(m => m.Resolve()).ToArray();
        var schedule = calls.Single(m => m.IsStatic && m.IsGetter && m.ReturnType.FullName == "System.Int32");
        var available = calls.Single(m => m.IsStatic && m.ReturnType.FullName == "System.Boolean" &&
            m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Int32");
        var context = schedule.DeclaringType;
        var scheduleProperty = context.Properties.Single(p => p.GetMethod == schedule);
        var game = context.Properties.Single(p => p != scheduleProperty && p.GetMethod.IsStatic && p.PropertyType.FullName == "System.Int32");
        var origin = context.Properties.Single(p => p.GetMethod.IsStatic && p.PropertyType.Resolve() is { IsEnum: true } e && e.Fields.Any(f => f.Name == "MiniGameHub"));
        var open = context.Methods.Single(m => m.IsPublic && m.IsStatic && m.Parameters.Count == 4 &&
            m.Parameters[0].ParameterType.FullName == origin.PropertyType.FullName &&
            m.Parameters[1].ParameterType.FullName == "System.Int32" && m.Parameters[2].ParameterType.FullName == "System.Int32");
        var table = open.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().Select(m => m.Resolve())
            .Single(m => m.IsStatic && m.ReturnType.FullName == "Proto.Design.common.PackEventMiniGameTable" && m.Parameters.Count == 1);
        var scheduleRow = available.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().Select(m => m.Resolve())
            .Single(m => m.IsStatic && m.ReturnType.FullName == "Proto.Net.EventScheduleDBInfo" && m.Parameters.Count == 1);
        var callback = (GenericInstanceType)open.Parameters[3].ParameterType;
        int packManKind = Convert.ToInt32(callback.GenericArguments[0].Resolve().Fields.Single(f => f.Name == "PackMan").Constant);
        string Qual(TypeReference t) => "global::" + t.FullName.Replace('/', '.');
        string Call(MethodDefinition m) => Qual(m.DeclaringType) + "." + m.Name;
        string owner = Qual(context);
        return $$"""
            namespace Dustweave.Mansion.Runtime {
                static class EntryNative {
                    public static EntryIdentity Read() { return new EntryIdentity(
                        {{owner}}.{{origin.Name}}.ToString(), {{owner}}.{{scheduleProperty.Name}}, {{owner}}.{{game.Name}}); }
                    public static bool Available(EntryIdentity entry) {
                        if (entry == null || !entry.Complete) return false;
                        var row = {{Call(scheduleRow)}}(entry.ScheduleId);
                        var game = {{Call(table)}}(entry.MiniGameId);
                        return row != null && row.EventId == entry.MiniGameId && game != null &&
                            (int)game.EventClearType == {{packManKind}} && {{Call(available)}}(entry.ScheduleId);
                    }
                    public static void Open(EntryIdentity entry) {
                        if (!Available(entry)) throw new System.InvalidOperationException("mansion-entry-unavailable");
                        {{Call(open)}}(({{Qual(origin.PropertyType)}})System.Enum.Parse(typeof({{Qual(origin.PropertyType)}}), entry.Origin),
                            entry.ScheduleId, entry.MiniGameId, null);
                    }
                }
            }
            """;
    }
}
