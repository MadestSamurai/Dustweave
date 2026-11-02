#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace BD2ReplayCapture
{
    /// <summary>
    /// Resolves the critical live-battle members by their source-level shape.
    /// Brown Dust 2 rotates obfuscated identifiers between client builds, but
    /// the team-list call graph and health/death relationships remain stable.
    /// Named mappings in ReplayCaptureLoader are retained only as fallbacks.
    /// </summary>
    internal static class RuntimeCharacterShapeResolver
    {
        private static readonly OpCode[] SingleByteOpCodes = new OpCode[0x100];
        private static readonly OpCode[] MultiByteOpCodes = new OpCode[0x100];

        static RuntimeCharacterShapeResolver()
        {
            foreach (var field in typeof(OpCodes).GetFields(
                         BindingFlags.Public | BindingFlags.Static))
            {
                var raw = field.GetValue(null);
                if (!(raw is OpCode))
                {
                    continue;
                }
                var opCode = (OpCode)raw;
                var value = unchecked((ushort)opCode.Value);
                if (value < 0x100)
                {
                    SingleByteOpCodes[value] = opCode;
                }
                else if ((value & 0xff00) == 0xfe00)
                {
                    MultiByteOpCodes[value & 0xff] = opCode;
                }
            }
        }

        public static MethodInfo FindTeamCharactersMethod(Type modelType)
        {
            if (modelType == null)
            {
                return null;
            }

            var candidates = modelType.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .Where(method =>
                {
                    var parameters = method.GetParameters();
                    return method.ReturnType.IsGenericType &&
                        method.ReturnType.GetGenericTypeDefinition() ==
                            typeof(List<>) &&
                        parameters.Length == 1 &&
                        parameters[0].ParameterType.IsEnum;
                })
                .Select(method =>
                {
                    var characterType =
                        method.ReturnType.GetGenericArguments()[0];
                    var healthShape = characterType.GetMethods(
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance)
                        .Count(candidate =>
                            candidate.ReturnType == typeof(long) &&
                            candidate.GetParameters().Length == 0);
                    var callSites = CountCallSites(modelType, method);
                    return new MethodScore(
                        method,
                        callSites * 20 + Math.Min(healthShape, 4) * 5);
                })
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Method.MetadataToken)
                .ToArray();

            return SelectUnambiguous(candidates, minimumScore: 25);
        }

        // BattleProcessManager owns a single native serializer that projects
        // one live runtime character into the complete BattleCharDBInfo used
        // by BattleStartRequest.  Using it at a natural action boundary keeps
        // HP, cooldowns, buffs, dodge and transformation state in one atomic
        // protobuf instead of rebuilding the state from scalar getters.
        public static MethodInfo FindBattleCharacterSnapshotMethod(
            Type modelType,
            Type characterType,
            Type battleCharacterProtobufType)
        {
            if (modelType == null || characterType == null ||
                battleCharacterProtobufType == null)
            {
                return null;
            }

            var candidates = modelType.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method =>
                {
                    var parameters = method.GetParameters();
                    return method.ReturnType == battleCharacterProtobufType &&
                        parameters.Length == 1 &&
                        parameters[0].ParameterType == characterType;
                })
                .OrderBy(method => method.MetadataToken)
                .ToArray();
            return candidates.Length == 1 ? candidates[0] : null;
        }

        // Guild Raid has a mode-specific BattleUI override. BattleCharTurn
        // calls the virtual DoEndAction once for every completed action and
        // only afterwards advances the turn, which makes this an exact
        // post-action/pre-turn-transition boundary.
        public static MethodInfo FindGuildRaidActionEndMethod(
            Assembly assembly,
            Type characterType)
        {
            if (assembly == null || characterType == null)
            {
                return null;
            }
            var owner = assembly.GetType(
                "BattleUI_GuildRaidBoss",
                throwOnError: false);
            if (owner == null)
            {
                return null;
            }
            var candidates = owner.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method =>
                {
                    var parameters = method.GetParameters();
                    var baseDefinition = method.GetBaseDefinition();
                    return string.Equals(
                            method.Name,
                            "DoEndAction",
                            StringComparison.Ordinal) &&
                        method.ReturnType == typeof(void) &&
                        parameters.Length == 1 &&
                        parameters[0].ParameterType == characterType &&
                        baseDefinition != null &&
                        baseDefinition.DeclaringType != owner;
                })
                .OrderBy(method => method.MetadataToken)
                .ToArray();
            return candidates.Length == 1 ? candidates[0] : null;
        }

        public static GuildRaidGolemRuntimeShape FindGuildRaidGolemShape(
            Type battleManagerType,
            Type battleModelType,
            Type characterType)
        {
            var result = new GuildRaidGolemRuntimeShape();
            if (battleManagerType == null || battleModelType == null ||
                characterType == null)
            {
                return result;
            }
            var controllerType = battleManagerType.Assembly.GetType(
                "BattleGuildRaidGolemController",
                throwOnError: false);
            if (controllerType == null)
            {
                return result;
            }

            var controllerProperties = battleManagerType.GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .Where(property =>
                    property.PropertyType == controllerType &&
                    property.GetIndexParameters().Length == 0 &&
                    property.GetGetMethod(true) != null)
                .OrderBy(property => property.MetadataToken)
                .ToArray();
            var characterProperties = controllerType.GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .Where(property =>
                    property.PropertyType == characterType &&
                    property.GetIndexParameters().Length == 0 &&
                    property.GetGetMethod(true) != null)
                .OrderBy(property => property.MetadataToken)
                .ToArray();
            var gaugeMethods = controllerType.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method =>
                    string.Equals(
                        method.Name,
                        "GetGolemGaugeValue",
                        StringComparison.Ordinal) &&
                    method.ReturnType == typeof(double) &&
                    method.GetParameters().Length == 0)
                .OrderBy(method => method.MetadataToken)
                .ToArray();
            var boolGetters = controllerType.GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .Where(property =>
                    property.PropertyType == typeof(bool) &&
                    property.GetIndexParameters().Length == 0 &&
                    property.GetGetMethod(true) != null)
                .Select(property => property.GetGetMethod(true))
                .ToArray();
            var remainProperties = controllerType.GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .Where(property =>
                    property.PropertyType == typeof(int) &&
                    property.GetIndexParameters().Length == 0 &&
                    property.GetGetMethod(true) != null &&
                    property.GetSetMethod(true) != null &&
                    boolGetters.Count(getter =>
                        Calls(getter, property.GetGetMethod(true))) >= 2)
                .OrderBy(property => property.MetadataToken)
                .ToArray();

            if (controllerProperties.Length == 1)
            {
                result.ControllerProperty = controllerProperties[0];
            }
            if (characterProperties.Length == 1)
            {
                result.CharacterProperty = characterProperties[0];
            }
            if (gaugeMethods.Length == 1)
            {
                result.GaugeMethod = gaugeMethods[0];
            }
            if (remainProperties.Length == 1)
            {
                result.RemainTurnProperty = remainProperties[0];
            }

            // BattleStartResponse has no Golem payload. The current client
            // stores the authoritative Golem level on the battle model and
            // writes it from BattleGolemDBInfo.Level in the retry/restore
            // path immediately before refreshing the live controller gauge.
            // Resolve that exact source flow instead of selecting an
            // obfuscated int property by name or current value.
            var golemInfoType = battleManagerType.Assembly.GetType(
                "Proto.Net.BattleGolemDBInfo",
                throwOnError: false);
            var levelGetter = golemInfoType == null
                ? null
                : golemInfoType.GetProperty(
                    "Level",
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance)?.GetGetMethod(true);
            var gaugeSetters = controllerType.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method =>
                    string.Equals(
                        method.Name,
                        "SetGolemGaugeValue",
                        StringComparison.Ordinal) &&
                    method.ReturnType == typeof(void) &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType == typeof(double))
                .ToArray();
            if (levelGetter != null && gaugeSetters.Length == 1)
            {
                var restoreMethods = battleManagerType.GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(method =>
                        Calls(method, levelGetter) &&
                        Calls(method, gaugeSetters[0]))
                    .ToArray();
                var levelProperties = battleModelType.GetProperties(
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance)
                    .Where(property =>
                        property.PropertyType == typeof(int) &&
                        property.GetIndexParameters().Length == 0 &&
                        property.GetGetMethod(true) != null &&
                        property.GetSetMethod(true) != null &&
                        restoreMethods.Any(method =>
                            Calls(method, property.GetSetMethod(true))))
                    .Distinct(PropertyIdentityComparer.Instance)
                    .OrderBy(property => property.MetadataToken)
                    .ToArray();
                if (levelProperties.Length == 1)
                {
                    result.LevelProperty = levelProperties[0];
                }
            }
            return result;
        }

        // Opcode-aware metadata only; callers use this for ABI receipts and
        // never to invoke the method or select a list by content.
        public static FieldInfo[] ReferencedFields(MethodInfo method)
        {
            return method == null
                ? new FieldInfo[0]
                : GetReferencedFields(method)
                    .Distinct(FieldIdentityComparer.Instance)
                    .OrderBy(field => field.MetadataToken)
                    .ToArray();
        }

        // Select the one static array accessor whose backing dictionary is
        // populated by code that consumes the corresponding protobuf response
        // getter. This proves source flow and avoids choosing a cache by
        // current contents, list size, or obfuscated member order.
        public static MethodInfo FindResponsePopulatedStaticArrayAccessor(
            Assembly assembly,
            Type valueType,
            MethodInfo responseGetter)
        {
            if (assembly == null || valueType == null ||
                responseGetter == null)
            {
                return null;
            }
            var dictionaryDefinition = typeof(Dictionary<,>);
            var arrayType = valueType.MakeArrayType();
            var flags = BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.DeclaredOnly;
            var candidates = new List<MethodInfo>();
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(type => type != null).ToArray();
            }
            foreach (var type in types)
            {
                FieldInfo[] fields;
                MethodInfo[] methods;
                try
                {
                    fields = type.GetFields(flags)
                        .Where(field =>
                            field.FieldType.IsGenericType &&
                            field.FieldType.GetGenericTypeDefinition() ==
                                dictionaryDefinition &&
                            field.FieldType.GetGenericArguments()[1] ==
                                valueType)
                        .ToArray();
                    methods = type.GetMethods(flags);
                }
                catch
                {
                    continue;
                }
                foreach (var field in fields)
                {
                    var accessors = methods.Where(method =>
                            method.ReturnType == arrayType &&
                            method.GetParameters().Length == 0 &&
                            GetReferencedFields(method).Any(referenced =>
                                SameField(referenced, field)))
                        .ToArray();
                    var responseConsumer = methods.Any(method =>
                        GetReferencedFields(method).Any(referenced =>
                            SameField(referenced, field)) &&
                        GetCalledMethods(method).Any(called =>
                            SameMethod(called, responseGetter)));
                    if (!responseConsumer)
                    {
                        continue;
                    }
                    foreach (var accessor in accessors)
                    {
                        if (!candidates.Any(candidate =>
                                SameMethod(candidate, accessor)))
                        {
                            candidates.Add(accessor);
                        }
                    }
                }
            }
            return candidates.Count == 1 ? candidates[0] : null;
        }

        // Account character and awake stores are private static dictionaries.
        // Their response callbacks mutate those dictionaries through helper
        // calls, so select the field from the response getter's call graph
        // rather than from obfuscated names, field order, or live contents.
        public static FieldInfo FindResponsePopulatedStaticDictionaryField(
            Assembly assembly,
            Type valueType,
            MethodInfo responseGetter)
        {
            if (assembly == null || valueType == null ||
                responseGetter == null)
            {
                return null;
            }

            var memberFlags = BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.DeclaredOnly;
            var fieldFlags = BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.DeclaredOnly;
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(type => type != null).ToArray();
            }

            var responseCallbacks = new List<MethodInfo>();
            foreach (var type in types)
            {
                try
                {
                    responseCallbacks.AddRange(type.GetMethods(memberFlags)
                        .Where(method =>
                        {
                            var parameters = method.GetParameters();
                            return method.ReturnType == typeof(bool) &&
                                parameters.Length == 3 &&
                                string.Equals(
                                    parameters[0].ParameterType.FullName,
                                    "Google.Protobuf.IMessage",
                                    StringComparison.Ordinal) &&
                                parameters[1].ParameterType == typeof(int) &&
                                parameters[2].ParameterType == typeof(int);
                        }));
                }
                catch
                {
                }
            }
            var producers = responseCallbacks.Where(method =>
                    GetCalledMethods(method).Any(called =>
                        SameMethod(called, responseGetter)))
                .ToArray();
            if (producers.Length == 0)
            {
                return null;
            }

            var candidates = new List<FieldInfo>();
            foreach (var type in types)
            {
                FieldInfo[] fields;
                try
                {
                    fields = type.GetFields(fieldFlags)
                        .Where(field =>
                            field.FieldType.IsGenericType &&
                            field.FieldType.GetGenericTypeDefinition() ==
                                typeof(Dictionary<,>) &&
                            field.FieldType.GetGenericArguments()[1] ==
                                valueType)
                        .ToArray();
                }
                catch
                {
                    continue;
                }
                foreach (var field in fields)
                {
                    if (producers.Any(producer =>
                            CanReachFieldReference(producer, field, 4)))
                    {
                        candidates.Add(field);
                    }
                }
            }

            var distinct = candidates
                .Distinct(FieldIdentityComparer.Instance)
                .OrderBy(field => field.MetadataToken)
                .ToArray();
            return distinct.Length == 1 ? distinct[0] : null;
        }

        private static bool CanReachFieldReference(
            MethodBase root,
            FieldInfo field,
            int maximumDepth)
        {
            var queue = new Queue<Tuple<MethodBase, int>>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            queue.Enqueue(Tuple.Create(root, 0));
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var method = current.Item1;
                var identity = method.Module.ModuleVersionId.ToString("D") +
                    ":" + method.MetadataToken.ToString("X8");
                if (!visited.Add(identity))
                {
                    continue;
                }
                if (GetReferencedFields(method).Any(referenced =>
                        SameField(referenced, field)))
                {
                    return true;
                }
                if (current.Item2 >= maximumDepth)
                {
                    continue;
                }
                foreach (var called in GetCalledMethods(method))
                {
                    if (called.Module == root.Module)
                    {
                        queue.Enqueue(Tuple.Create(
                            called,
                            current.Item2 + 1));
                    }
                }
            }
            return false;
        }

        // Opcode-aware write set used by the Inventory Battle resolver. This
        // deliberately exposes field topology rather than obfuscated names.
        public static FieldInfo[] WrittenFields(MethodInfo method)
        {
            return method == null
                ? new FieldInfo[0]
                : GetWrittenFields(method)
                    .Distinct(FieldIdentityComparer.Instance)
                    .OrderBy(field => field.MetadataToken)
                    .ToArray();
        }

        public static MethodInfo FindCurrentHealthMethod(Type characterType)
        {
            if (characterType == null)
            {
                return null;
            }

            var longMethods = NoArgumentMethods(characterType, typeof(long));
            var boolMethods = NoArgumentMethods(characterType, typeof(bool));
            var candidates = longMethods
                .Select(method =>
                {
                    var deathShapeCallers = boolMethods.Count(candidate =>
                        Calls(candidate, method));
                    return new MethodScore(
                        method,
                        deathShapeCallers * 200 +
                        CountCallSites(characterType, method) * 5);
                })
                .Where(candidate => candidate.Score >= 200)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Method.MetadataToken)
                .ToArray();

            return SelectUnambiguous(candidates, minimumScore: 200);
        }

        public static MethodInfo FindMaxHealthMethod(
            Type characterType,
            MethodInfo currentHealthMethod)
        {
            if (characterType == null)
            {
                return null;
            }

            var longMethods = NoArgumentMethods(characterType, typeof(long))
                .Where(method => !SameMethod(method, currentHealthMethod))
                .ToArray();
            var diagnosticMethods = GetTypeGraphMethods(characterType)
                .Where(method =>
                    ContainsLoadedString(method, "maxHealth") ||
                    ContainsLoadedString(method, "[Heal]"))
                .ToArray();
            var candidates = longMethods
                .Select(method =>
                {
                    var diagnosticCalls = diagnosticMethods.Count(candidate =>
                        Calls(candidate, method));
                    return new MethodScore(
                        method,
                        diagnosticCalls * 200 +
                        CountCallSites(characterType, method) * 5);
                })
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Method.MetadataToken)
                .ToArray();

            if (candidates.Any(candidate => candidate.Score >= 200))
            {
                return SelectUnambiguous(
                    candidates.Where(candidate => candidate.Score >= 200)
                        .ToArray(),
                    minimumScore: 200);
            }

            return SelectUnambiguous(candidates, minimumScore: 5);
        }

        public static MethodInfo FindIsDeadMethod(
            Type characterType,
            MethodInfo currentHealthMethod)
        {
            if (characterType == null || currentHealthMethod == null)
            {
                return null;
            }

            var candidates = NoArgumentMethods(characterType, typeof(bool))
                .Where(method => Calls(method, currentHealthMethod))
                .Select(method => new MethodScore(
                    method,
                    200 + CountCallSites(characterType, method) * 5))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Method.MetadataToken)
                .ToArray();
            return SelectUnambiguous(candidates, minimumScore: 200);
        }

        public static PropertyInfo FindTeamProperty(
            Type characterType,
            Type teamType)
        {
            if (characterType == null || teamType == null || !teamType.IsEnum)
            {
                return null;
            }

            var candidates = characterType.GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .Where(property =>
                    property.PropertyType == teamType &&
                    property.GetIndexParameters().Length == 0 &&
                    property.GetGetMethod(true) != null)
                .Select(property => new PropertyScore(
                    property,
                    (property.GetSetMethod(true) != null ? 100 : 0) +
                    CountCallSites(
                        characterType,
                        property.GetGetMethod(true)) * 5))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Property.MetadataToken)
                .ToArray();
            return SelectUnambiguous(candidates, minimumScore: 100);
        }

        public static PropertyInfo FindActiveBuffsProperty(Type characterType)
        {
            if (characterType == null)
            {
                return null;
            }

            var candidates = characterType.GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .Where(property =>
                    property.GetIndexParameters().Length == 0 &&
                    property.GetGetMethod(true) != null &&
                    property.PropertyType.IsGenericType &&
                    property.PropertyType.GetGenericTypeDefinition() ==
                        typeof(List<>))
                .Select(property => new
                {
                    Property = property,
                    ItemType = property.PropertyType.GetGenericArguments()[0],
                })
                .Where(candidate =>
                    candidate.ItemType.GetProperties(
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance)
                        .Any(property =>
                            string.Equals(
                                property.PropertyType.Name,
                                "BuffTable",
                                StringComparison.Ordinal)) &&
                    NoArgumentMethods(candidate.ItemType, typeof(int)).Length > 0)
                .ToArray();

            var scored = candidates
                .Select(candidate => new PropertyScore(
                    candidate.Property,
                    candidates.Count(other =>
                        candidate.ItemType.IsAssignableFrom(other.ItemType)) *
                        100 +
                    CountCallSites(
                        characterType,
                        candidate.Property.GetGetMethod(true))))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Property.MetadataToken)
                .ToArray();
            return SelectUnambiguous(scored, minimumScore: 100);
        }

        public static MethodInfo FindBuffIdMethod(Type buffType)
        {
            if (buffType == null)
            {
                return null;
            }

            var idFields = buffType.GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .Where(property =>
                    string.Equals(
                        property.PropertyType.Name,
                        "BuffTable",
                        StringComparison.Ordinal) &&
                    property.GetGetMethod(true) != null)
                .SelectMany(property =>
                    GetReferencedFields(property.GetGetMethod(true)))
                .Where(field => field.FieldType == typeof(int))
                .Distinct(FieldIdentityComparer.Instance)
                .ToArray();
            var candidates = NoArgumentMethods(buffType, typeof(int))
                .Select(method => new MethodScore(
                    method,
                    GetReferencedFields(method)
                        .Count(field => idFields.Any(idField =>
                            SameField(field, idField))) * 100 +
                    CountCallSites(buffType, method)))
                .Where(candidate => candidate.Score >= 100)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Method.MetadataToken)
                .ToArray();
            return SelectUnambiguous(candidates, minimumScore: 100);
        }

        public static FieldInfo FindCurrentSkillPointField(
            Type battleManagerType)
        {
            if (battleManagerType == null)
            {
                return null;
            }
            var getPlayData = battleManagerType.GetMethod(
                "GetPlayData",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance);
            var setSkillBubble = battleManagerType.GetMethod(
                "SetSkillBubble",
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance);
            if (getPlayData == null ||
                setSkillBubble == null ||
                getPlayData.ReturnType == typeof(void))
            {
                return null;
            }
            var fields = GetReferencedFields(setSkillBubble)
                .Where(field =>
                    field.DeclaringType == getPlayData.ReturnType &&
                    field.FieldType == typeof(int))
                .Distinct(FieldIdentityComparer.Instance)
                .ToArray();
            if (fields.Length == 1)
            {
                return fields[0];
            }

            // Newer clients access the skill-point storage through an
            // auto-property instead of emitting ldfld/stfld directly inside
            // SetSkillBubble. Resolve the single int property whose getter and
            // setter are both called by that method, then follow its trivial
            // backing field. This remains name-independent and rejects an
            // ambiguous shape.
            var calledMethods = GetCalledMethods(setSkillBubble).ToArray();
            var propertyFields = getPlayData.ReturnType.GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .Where(property =>
                    property.PropertyType == typeof(int) &&
                    property.GetIndexParameters().Length == 0 &&
                    property.GetGetMethod(true) != null &&
                    property.GetSetMethod(true) != null &&
                    calledMethods.Any(method => SameMethod(
                        method,
                        property.GetGetMethod(true))) &&
                    calledMethods.Any(method => SameMethod(
                        method,
                        property.GetSetMethod(true))))
                .Select(FindTrivialPropertyBackingField)
                .Where(field => field != null)
                .Distinct(FieldIdentityComparer.Instance)
                .ToArray();
            return propertyFields.Length == 1 ? propertyFields[0] : null;
        }

        // Stage 5 passive SP ABI: no names are used. A candidate play-data
        // accessor is proved only when a bounded scalar-update caller takes
        // the same team enum, calls it, and calls both getter and setter of
        // exactly one trivial int property on its return type.
        public static PassiveSkillPointAbiResolution FindPassiveSkillPointAbi(
            Type battleManagerType)
        {
            var resolution = new PassiveSkillPointAbiResolution();
            if (battleManagerType == null) { resolution.Blockers.Add("battle_manager_missing"); return resolution; }
            var candidates = battleManagerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.ReturnType != typeof(void) && !method.ReturnType.IsValueType && method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType.IsEnum)
                .OrderBy(method => method.MetadataToken).ToArray();
            foreach (var candidate in candidates)
            {
                var matched = new List<PassiveSkillPointAbi>();
                var callers = battleManagerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(caller => caller.ReturnType == typeof(void) && ScalarUpdateShape(caller.GetParameters(), candidate.GetParameters()[0].ParameterType) && Calls(caller, candidate)).OrderBy(caller => caller.MetadataToken).ToArray();
                foreach (var caller in callers)
                {
                    var calls = GetCalledMethods(caller).ToArray();
                    var propertyMatches = new List<PassiveSkillPointAbi>();
                    foreach (var property in candidate.ReturnType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(property => property.PropertyType == typeof(int) && property.GetIndexParameters().Length == 0 && property.GetGetMethod(true) != null && property.GetSetMethod(true) != null)
                        .OrderBy(property => property.MetadataToken))
                    {
                        var getter = property.GetGetMethod(true); var setter = property.GetSetMethod(true); var backing = FindTrivialPropertyBackingField(property);
                        if (backing != null && calls.Any(method => SameMethod(method, getter)) && calls.Any(method => SameMethod(method, setter))) propertyMatches.Add(new PassiveSkillPointAbi(candidate, caller, property, getter, setter, backing));
                    }
                    // A scalar update caller must identify exactly one SP
                    // property; callers mutating several play-data integers
                    // are deliberately diagnostic-only and cannot select it.
                    if (propertyMatches.Count == 1) matched.Add(propertyMatches[0]);
                }
                resolution.Candidates.Add(new PassiveSkillPointAbiCandidate(candidate, callers.Select(item => item.MetadataToken).ToArray(), matched.Select(item => item.Property.MetadataToken).Distinct().OrderBy(item => item).ToArray(), matched.Count));
                resolution.Matches.AddRange(matched);
            }
            if (resolution.Matches.Count == 1)
            {
                var selected = resolution.Matches[0];
                if (TryBindSkillPointAccounting(battleManagerType, selected))
                    resolution.Selected = selected;
                else
                    resolution.Blockers.Add("passive_skill_point_accounting_dataflow_missing");
            }
            else resolution.Blockers.Add(resolution.Matches.Count == 0 ? "passive_skill_point_dataflow_missing" : "passive_skill_point_ambiguous");
            return resolution;
        }

        // Current-client source contract:
        //   SetReserveSkill(team, normalDelta, burstDelta)
        // updates exactly two other trivial int properties in call order, and
        // the effective-SP getter consumes raw, normal reserve, and burst
        // reserve together. Requiring both shapes prevents metadata-token
        // drift from silently relabelling an unrelated play-data integer.
        private static bool TryBindSkillPointAccounting(
            Type battleManagerType,
            PassiveSkillPointAbi raw)
        {
            var playDataType = raw.Method.ReturnType;
            var teamType = raw.Method.GetParameters()[0].ParameterType;
            var properties = playDataType.GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .Where(property => property.PropertyType == typeof(int) &&
                    property.GetIndexParameters().Length == 0 &&
                    property.GetGetMethod(true) != null &&
                    property.GetSetMethod(true) != null)
                .OrderBy(property => property.MetadataToken)
                .ToArray();
            var reserveCandidates = new List<Tuple<MethodInfo, PropertyInfo, FieldInfo, PropertyInfo, FieldInfo>>();
            foreach (var caller in battleManagerType.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.ReturnType == typeof(void) &&
                    ReserveUpdateShape(method.GetParameters(), teamType) &&
                    Calls(method, raw.Method))
                .OrderBy(method => method.MetadataToken))
            {
                var calls = GetCalledMethods(caller).ToArray();
                var mutated = properties.Where(property =>
                        !SameProperty(property, raw.Property) &&
                        calls.Any(method => SameMethod(method, property.GetGetMethod(true))) &&
                        calls.Any(method => SameMethod(method, property.GetSetMethod(true))))
                    .Select(property => new
                    {
                        Property = property,
                        Backing = FindTrivialPropertyBackingField(property),
                        Order = Array.FindIndex(calls, method =>
                            SameMethod(method, property.GetGetMethod(true))),
                    })
                    .Where(item => item.Backing != null && item.Order >= 0)
                    .OrderBy(item => item.Order)
                    .ThenBy(item => item.Property.MetadataToken)
                    .ToArray();
                if (mutated.Length == 2 && mutated[0].Order != mutated[1].Order)
                    reserveCandidates.Add(Tuple.Create(
                        caller,
                        mutated[0].Property,
                        mutated[0].Backing,
                        mutated[1].Property,
                        mutated[1].Backing));
            }
            if (reserveCandidates.Count != 1) return false;
            var reserve = reserveCandidates[0];

            var effectiveCandidates = playDataType.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.ReturnType == typeof(int) &&
                    method.GetParameters().Length == 0 &&
                    UsesProperty(method, raw.Property, raw.BackingField) &&
                    UsesProperty(method, reserve.Item2, reserve.Item3) &&
                    UsesProperty(method, reserve.Item4, reserve.Item5))
                .OrderBy(method => method.MetadataToken)
                .ToArray();
            if (effectiveCandidates.Length != 1) return false;
            raw.BindAccounting(
                reserve.Item1,
                reserve.Item2,
                reserve.Item3,
                reserve.Item4,
                reserve.Item5,
                effectiveCandidates[0]);
            return true;
        }

        private static bool ReserveUpdateShape(ParameterInfo[] parameters, Type teamType) =>
            parameters != null && parameters.Length == 3 &&
            parameters[0].ParameterType == teamType &&
            parameters[1].ParameterType == typeof(int) &&
            parameters[2].ParameterType == typeof(int);

        private static bool UsesProperty(
            MethodInfo method,
            PropertyInfo property,
            FieldInfo backing)
        {
            return GetCalledMethods(method).Any(called =>
                    SameMethod(called, property.GetGetMethod(true))) ||
                GetReferencedFields(method).Any(field => SameField(field, backing));
        }

        private static bool ScalarUpdateShape(ParameterInfo[] parameters, Type teamType)
        {
            if (parameters == null || parameters.Length < 2 || parameters.Length > 4 || parameters.Count(parameter => parameter.ParameterType == teamType) != 1 || !parameters.Any(parameter => parameter.ParameterType == typeof(int) || parameter.ParameterType == typeof(long))) return false;
            return parameters.All(parameter => parameter.ParameterType == teamType || parameter.ParameterType == typeof(int) || parameter.ParameterType == typeof(long) || parameter.ParameterType == typeof(bool));
        }

        public static RuntimeCharacterIdentityProperties
            FindIdentityProperties(
                Type characterType,
                PropertyInfo teamProperty)
        {
            var result = new RuntimeCharacterIdentityProperties();
            if (characterType == null || teamProperty == null)
            {
                return result;
            }

            var deckOutMethods = GetTypeGraphMethods(characterType)
                .Where(method =>
                    ContainsLoadedString(method, "DeckOutSpawn, team:"))
                .ToArray();
            foreach (var method in deckOutMethods)
            {
                var properties = GetCalledCharacterProperties(
                    characterType,
                    method);
                var teamIndex = FindPropertyIndex(properties, teamProperty, 0);
                if (teamIndex < 0)
                {
                    continue;
                }
                var integers = properties
                    .Skip(teamIndex + 1)
                    .Where(property => property.PropertyType == typeof(int))
                    .Distinct(PropertyIdentityComparer.Instance)
                    .ToArray();
                if (integers.Length >= 2)
                {
                    result.CharacterId = integers[0];
                    result.UniqueIndex = integers[1];
                    break;
                }
            }

            if (result.CharacterId != null && result.UniqueIndex != null)
            {
                var damageMethods = GetTypeGraphMethods(characterType)
                    .Where(method =>
                        ContainsLoadedString(method, "UniqueId") &&
                        ContainsLoadedString(method, "GridIndex"))
                    .ToArray();
                foreach (var method in damageMethods)
                {
                    var properties = GetCalledCharacterProperties(
                        characterType,
                        method);
                    var charIndex = FindPropertyIndex(
                        properties,
                        result.CharacterId,
                        0);
                    var uniqueIndex = charIndex < 0
                        ? -1
                        : FindPropertyIndex(
                            properties,
                            result.UniqueIndex,
                            charIndex + 1);
                    if (uniqueIndex < 0)
                    {
                        continue;
                    }
                    var trailing = properties
                        .Skip(uniqueIndex + 1)
                        .Where(property =>
                            property.PropertyType == typeof(int) &&
                            !SameProperty(property, result.CharacterId) &&
                            !SameProperty(property, result.UniqueIndex))
                        .Distinct(PropertyIdentityComparer.Instance)
                        .Take(2)
                        .ToArray();
                    if (trailing.Length == 2)
                    {
                        result.Sequence = trailing[0];
                        result.Grid = trailing[1];
                        break;
                    }
                }

                var actionMethods = GetTypeGraphMethods(characterType)
                    .Where(method =>
                        ContainsLoadedString(method, "Attacker CharID:") &&
                        ContainsLoadedString(method, "CostumeId:"))
                    .ToArray();
                foreach (var method in actionMethods)
                {
                    var properties = GetCalledCharacterProperties(
                        characterType,
                        method);
                    var charIndex = FindPropertyIndex(
                        properties,
                        result.CharacterId,
                        0);
                    if (charIndex < 0)
                    {
                        continue;
                    }
                    result.CostumeId = properties
                        .Skip(charIndex + 1)
                        .FirstOrDefault(property =>
                            property.PropertyType == typeof(int) &&
                            !SameProperty(property, result.CharacterId) &&
                            !SameProperty(property, result.UniqueIndex));
                    if (result.CostumeId != null)
                    {
                        break;
                    }
                }
            }

            result.CharacterInventory = FindCharacterInventoryProperty(
                characterType);
            result.CostumeDesignId = FindCostumeDesignIdProperty(
                characterType);
            result.ChainCount = FindChainCountProperty(characterType);
            return result;
        }

        public static MethodInfo[] FindDictionaryMutationMethods(
            Type ownerType,
            FieldInfo dictionaryField)
        {
            if (ownerType == null || dictionaryField == null)
            {
                return new MethodInfo[0];
            }

            return GetTypeGraphMethods(ownerType)
                .OfType<MethodInfo>()
                .Where(method =>
                    !method.IsStatic &&
                    GetReferencedFields(method).Any(field =>
                        SameField(field, dictionaryField)) &&
                    GetCalledMethods(method).Any(called =>
                        called.DeclaringType != null &&
                        called.DeclaringType.IsGenericType &&
                        called.DeclaringType.GetGenericTypeDefinition() ==
                            typeof(Dictionary<,>) &&
                        (string.Equals(
                             called.Name,
                             "Add",
                             StringComparison.Ordinal) ||
                         string.Equals(
                             called.Name,
                             "set_Item",
                             StringComparison.Ordinal))))
                .OrderBy(method => method.MetadataToken)
                .ToArray();
        }

        public static FieldInfo[] FindCharacterListFields(
            Type modelType,
            Type characterType)
        {
            if (modelType == null || characterType == null)
            {
                return new FieldInfo[0];
            }

            return modelType.GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .Where(field =>
                    field.FieldType.IsGenericType &&
                    field.FieldType.GetGenericTypeDefinition() ==
                        typeof(List<>) &&
                    field.FieldType.GetGenericArguments()[0] ==
                        characterType)
                .OrderBy(field => field.MetadataToken)
                .ToArray();
        }

        public static FieldInfo FindTrivialPropertyBackingField(
            PropertyInfo property)
        {
            if (property == null)
            {
                return null;
            }

            var getter = property.GetGetMethod(true);
            if (getter == null ||
                getter.IsStatic ||
                getter.GetParameters().Length != 0)
            {
                return null;
            }

            var fields = GetReferencedFields(getter)
                .Where(field =>
                    field != null &&
                    field.FieldType == property.PropertyType &&
                    field.DeclaringType != null &&
                    field.DeclaringType.IsAssignableFrom(
                        property.DeclaringType))
                .Distinct(FieldIdentityComparer.Instance)
                .ToArray();
            if (fields.Length != 1 ||
                GetCalledMethods(getter).Any())
            {
                return null;
            }
            return fields[0];
        }

        private static PropertyInfo FindCharacterInventoryProperty(
            Type characterType)
        {
            var constructors = characterType.GetConstructors(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .Where(constructor => GetCalledMethods(constructor).Any(method =>
                    string.Equals(
                        method.Name,
                        "get_InvenIndex",
                        StringComparison.Ordinal) ||
                    string.Equals(
                        method.Name,
                        "get_CharInvenIndex",
                        StringComparison.Ordinal)))
                .ToArray();
            var candidates = characterType.GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .Where(property =>
                    property.PropertyType == typeof(long) &&
                    property.GetIndexParameters().Length == 0 &&
                    property.GetSetMethod(true) != null)
                .Select(property => new PropertyScore(
                    property,
                    constructors.Count(constructor =>
                        Calls(constructor, property.GetSetMethod(true))) * 100))
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Property.MetadataToken)
                .ToArray();
            return SelectUnambiguous(candidates, minimumScore: 100);
        }

        private static PropertyInfo FindCostumeDesignIdProperty(
            Type characterType)
        {
            var candidates = characterType.GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .Where(property =>
                    property.GetIndexParameters().Length == 0 &&
                    string.Equals(
                        property.PropertyType.Name,
                        "CostumeDesignTable",
                        StringComparison.Ordinal) &&
                    property.GetGetMethod(true) != null)
                .SelectMany(property => GetCalledCharacterProperties(
                    characterType,
                    property.GetGetMethod(true)))
                .Where(property => property.PropertyType == typeof(int))
                .GroupBy(property => new
                {
                    property.Module,
                    property.MetadataToken,
                })
                .Select(group => new PropertyScore(
                    group.First(),
                    group.Count() * 100))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Property.MetadataToken)
                .ToArray();
            return SelectUnambiguous(candidates, minimumScore: 100);
        }

        private static PropertyInfo FindChainCountProperty(Type characterType)
        {
            var candidates = characterType.GetProperties(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .Where(property =>
                    property.PropertyType == typeof(int) &&
                    property.GetIndexParameters().Length == 0 &&
                    property.GetGetMethod(true) != null &&
                    property.GetSetMethod(true) != null &&
                    GetCalledMethods(property.GetSetMethod(true)).Any(method =>
                        method.DeclaringType == typeof(Math) &&
                        string.Equals(
                            method.Name,
                            "Min",
                            StringComparison.Ordinal)))
                .Select(property => new PropertyScore(
                    property,
                    100 + CountCallSites(
                        characterType,
                        property.GetGetMethod(true))))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Property.MetadataToken)
                .ToArray();
            return SelectUnambiguous(candidates, minimumScore: 100);
        }

        private static MethodInfo[] NoArgumentMethods(
            Type type,
            Type returnType)
        {
            return type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance)
                .Where(method =>
                    method.ReturnType == returnType &&
                    method.GetParameters().Length == 0 &&
                    !method.IsSpecialName)
                .ToArray();
        }

        private static MethodInfo SelectUnambiguous(
            MethodScore[] candidates,
            int minimumScore)
        {
            if (candidates.Length == 0 ||
                candidates[0].Score < minimumScore)
            {
                return null;
            }
            if (candidates.Length > 1 &&
                candidates[0].Score == candidates[1].Score)
            {
                return null;
            }
            return candidates[0].Method;
        }

        private static PropertyInfo SelectUnambiguous(
            PropertyScore[] candidates,
            int minimumScore)
        {
            if (candidates.Length == 0 ||
                candidates[0].Score < minimumScore)
            {
                return null;
            }
            if (candidates.Length > 1 &&
                candidates[0].Score == candidates[1].Score)
            {
                return null;
            }
            return candidates[0].Property;
        }

        private static int CountCallSites(Type rootType, MethodInfo target)
        {
            if (rootType == null || target == null)
            {
                return 0;
            }
            return GetTypeGraphMethods(rootType)
                .Sum(method => GetCalledMethods(method)
                    .Count(called => SameMethod(called, target)));
        }

        private static IEnumerable<MethodBase> GetTypeGraphMethods(Type rootType)
        {
            foreach (var method in rootType.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.Static | BindingFlags.Instance |
                         BindingFlags.DeclaredOnly))
            {
                yield return method;
            }
            foreach (var constructor in rootType.GetConstructors(
                         BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.Static | BindingFlags.Instance))
            {
                yield return constructor;
            }
            foreach (var nestedType in rootType.GetNestedTypes(
                         BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (var method in GetTypeGraphMethods(nestedType))
                {
                    yield return method;
                }
            }
        }

        private static bool Calls(MethodBase caller, MethodInfo target)
        {
            return GetCalledMethods(caller).Any(method =>
                SameMethod(method, target));
        }

        private static bool SameMethod(MethodBase left, MethodBase right)
        {
            return left != null &&
                right != null &&
                left.Module == right.Module &&
                left.MetadataToken == right.MetadataToken;
        }

        private static PropertyInfo[] GetCalledCharacterProperties(
            Type characterType,
            MethodBase method)
        {
            var properties = characterType.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance);
            var byGetter = properties
                .Where(property => property.GetGetMethod(true) != null)
                .GroupBy(property =>
                    property.GetGetMethod(true).MetadataToken)
                .ToDictionary(
                    group => group.Key,
                    group => group.First());
            return GetCalledMethods(method)
                .Where(called =>
                    called.Module == characterType.Module &&
                    byGetter.ContainsKey(called.MetadataToken))
                .Select(called => byGetter[called.MetadataToken])
                .ToArray();
        }

        private static int FindPropertyIndex(
            PropertyInfo[] properties,
            PropertyInfo target,
            int startIndex)
        {
            for (var index = Math.Max(0, startIndex);
                 index < properties.Length;
                 index++)
            {
                if (SameProperty(properties[index], target))
                {
                    return index;
                }
            }
            return -1;
        }

        private static bool SameProperty(
            PropertyInfo left,
            PropertyInfo right)
        {
            return left != null &&
                right != null &&
                left.Module == right.Module &&
                left.MetadataToken == right.MetadataToken;
        }

        private static bool SameField(FieldInfo left, FieldInfo right)
        {
            return left != null &&
                right != null &&
                left.Module == right.Module &&
                left.MetadataToken == right.MetadataToken;
        }

        private static IEnumerable<MethodBase> GetCalledMethods(
            MethodBase method)
        {
            foreach (var instruction in ReadInstructions(method))
            {
                if (instruction.OpCode.OperandType != OperandType.InlineMethod)
                {
                    continue;
                }
                MethodBase called = null;
                try
                {
                    called = method.Module.ResolveMethod(
                        instruction.Token,
                        method.DeclaringType != null &&
                            method.DeclaringType.IsGenericType
                            ? method.DeclaringType.GetGenericArguments()
                            : null,
                        method.IsGenericMethod
                            ? method.GetGenericArguments()
                            : null);
                }
                catch
                {
                }
                if (called != null)
                {
                    yield return called;
                }
            }
        }

        private static IEnumerable<FieldInfo> GetReferencedFields(
            MethodBase method)
        {
            foreach (var instruction in ReadInstructions(method))
            {
                if (instruction.OpCode.OperandType != OperandType.InlineField)
                {
                    continue;
                }
                FieldInfo field = null;
                try
                {
                    field = method.Module.ResolveField(
                        instruction.Token,
                        method.DeclaringType != null &&
                            method.DeclaringType.IsGenericType
                            ? method.DeclaringType.GetGenericArguments()
                            : null,
                        method.IsGenericMethod
                            ? method.GetGenericArguments()
                            : null);
                }
                catch
                {
                }
                if (field != null)
                {
                    yield return field;
                }
            }
        }

        private static IEnumerable<FieldInfo> GetWrittenFields(
            MethodBase method)
        {
            foreach (var instruction in ReadInstructions(method))
            {
                if (instruction.OpCode != OpCodes.Stfld &&
                    instruction.OpCode != OpCodes.Stsfld)
                {
                    continue;
                }
                FieldInfo field = null;
                try
                {
                    field = method.Module.ResolveField(
                        instruction.Token,
                        method.DeclaringType != null &&
                            method.DeclaringType.IsGenericType
                            ? method.DeclaringType.GetGenericArguments()
                            : null,
                        method.IsGenericMethod
                            ? method.GetGenericArguments()
                            : null);
                }
                catch
                {
                }
                if (field != null)
                {
                    yield return field;
                }
            }
        }

        private static bool ContainsLoadedString(
            MethodBase method,
            string fragment)
        {
            foreach (var instruction in ReadInstructions(method))
            {
                if (instruction.OpCode.OperandType != OperandType.InlineString)
                {
                    continue;
                }
                try
                {
                    var value = method.Module.ResolveString(instruction.Token);
                    if (value != null &&
                        value.IndexOf(
                            fragment,
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }
            return false;
        }

        private static IEnumerable<Instruction> ReadInstructions(
            MethodBase method)
        {
            MethodBody body;
            try
            {
                body = method.GetMethodBody();
            }
            catch
            {
                yield break;
            }
            var bytes = body == null ? null : body.GetILAsByteArray();
            if (bytes == null)
            {
                yield break;
            }

            var offset = 0;
            while (offset < bytes.Length)
            {
                var opCode = ReadOpCode(bytes, ref offset);
                if (opCode.Size == 0)
                {
                    yield break;
                }

                var token = 0;
                if (opCode.OperandType == OperandType.InlineMethod ||
                    opCode.OperandType == OperandType.InlineString ||
                    opCode.OperandType == OperandType.InlineField)
                {
                    if (offset + 4 > bytes.Length)
                    {
                        yield break;
                    }
                    token = BitConverter.ToInt32(bytes, offset);
                }
                yield return new Instruction(opCode, token);

                int operandSize;
                try
                {
                    operandSize = OperandSize(
                        opCode.OperandType,
                        bytes,
                        offset);
                }
                catch (InvalidDataException)
                {
                    yield break;
                }
                if (operandSize < 0 || offset + operandSize > bytes.Length)
                {
                    yield break;
                }
                offset += operandSize;
            }
        }

        private static OpCode ReadOpCode(byte[] bytes, ref int offset)
        {
            if (offset >= bytes.Length)
            {
                return default(OpCode);
            }
            var first = bytes[offset++];
            if (first != 0xfe)
            {
                return SingleByteOpCodes[first];
            }
            if (offset >= bytes.Length)
            {
                return default(OpCode);
            }
            return MultiByteOpCodes[bytes[offset++]];
        }

        private static int OperandSize(
            OperandType operandType,
            byte[] bytes,
            int offset)
        {
            switch (operandType)
            {
                case OperandType.InlineNone:
                    return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    return 1;
                case OperandType.InlineVar:
                    return 2;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR:
                    return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    return 8;
                case OperandType.InlineSwitch:
                    if (offset + 4 > bytes.Length)
                    {
                        return bytes.Length - offset;
                    }
                    return 4 + Math.Max(
                        0,
                        BitConverter.ToInt32(bytes, offset)) * 4;
                default:
                    throw new InvalidDataException(
                        "Unsupported IL operand type: " + operandType);
            }
        }

        private sealed class MethodScore
        {
            public MethodScore(MethodInfo method, int score)
            {
                Method = method;
                Score = score;
            }

            public MethodInfo Method { get; private set; }
            public int Score { get; private set; }
        }

        private sealed class PropertyScore
        {
            public PropertyScore(PropertyInfo property, int score)
            {
                Property = property;
                Score = score;
            }

            public PropertyInfo Property { get; private set; }
            public int Score { get; private set; }
        }

        internal sealed class RuntimeCharacterIdentityProperties
        {
            public PropertyInfo UniqueIndex { get; set; }
            public PropertyInfo CharacterInventory { get; set; }
            public PropertyInfo CharacterId { get; set; }
            public PropertyInfo Grid { get; set; }
            public PropertyInfo Sequence { get; set; }
            public PropertyInfo CostumeId { get; set; }
            public PropertyInfo CostumeDesignId { get; set; }
            public PropertyInfo ChainCount { get; set; }
        }

        internal sealed class PassiveSkillPointAbi
        {
            public PassiveSkillPointAbi(MethodInfo method, MethodInfo caller, PropertyInfo property, MethodInfo getter, MethodInfo setter, FieldInfo backing)
            { Method = method; Caller = caller; Property = property; Getter = getter; Setter = setter; BackingField = backing; }
            public MethodInfo Method { get; private set; }
            public MethodInfo Caller { get; private set; }
            public PropertyInfo Property { get; private set; }
            public MethodInfo Getter { get; private set; }
            public MethodInfo Setter { get; private set; }
            public FieldInfo BackingField { get; private set; }
            public MethodInfo ReservationCaller { get; private set; }
            public PropertyInfo ReservedProperty { get; private set; }
            public FieldInfo ReservedBackingField { get; private set; }
            public PropertyInfo ReservedBurstProperty { get; private set; }
            public FieldInfo ReservedBurstBackingField { get; private set; }
            public MethodInfo EffectiveGetter { get; private set; }

            public void BindAccounting(
                MethodInfo reservationCaller,
                PropertyInfo reservedProperty,
                FieldInfo reservedBackingField,
                PropertyInfo reservedBurstProperty,
                FieldInfo reservedBurstBackingField,
                MethodInfo effectiveGetter)
            {
                ReservationCaller = reservationCaller;
                ReservedProperty = reservedProperty;
                ReservedBackingField = reservedBackingField;
                ReservedBurstProperty = reservedBurstProperty;
                ReservedBurstBackingField = reservedBurstBackingField;
                EffectiveGetter = effectiveGetter;
            }
        }

        internal sealed class PassiveSkillPointAbiCandidate
        {
            public PassiveSkillPointAbiCandidate(MethodInfo method, int[] callers, int[] properties, int matches)
            { Method = method; CallerTokens = callers; PropertyTokens = properties; MatchCount = matches; }
            public MethodInfo Method { get; private set; }
            public int[] CallerTokens { get; private set; }
            public int[] PropertyTokens { get; private set; }
            public int MatchCount { get; private set; }
        }

        internal sealed class PassiveSkillPointAbiResolution
        {
            public List<PassiveSkillPointAbiCandidate> Candidates { get; } = new List<PassiveSkillPointAbiCandidate>();
            public List<PassiveSkillPointAbi> Matches { get; } = new List<PassiveSkillPointAbi>();
            public List<string> Blockers { get; } = new List<string>();
            public PassiveSkillPointAbi Selected { get; set; }
        }

        internal sealed class GuildRaidGolemRuntimeShape
        {
            public PropertyInfo ControllerProperty { get; set; }
            public PropertyInfo CharacterProperty { get; set; }
            public MethodInfo GaugeMethod { get; set; }
            public PropertyInfo RemainTurnProperty { get; set; }
            public PropertyInfo LevelProperty { get; set; }

            public bool Complete
            {
                get
                {
                    return ControllerProperty != null &&
                        CharacterProperty != null &&
                        GaugeMethod != null &&
                        RemainTurnProperty != null &&
                        LevelProperty != null;
                }
            }
        }

        private sealed class PropertyIdentityComparer :
            IEqualityComparer<PropertyInfo>
        {
            public static readonly PropertyIdentityComparer Instance =
                new PropertyIdentityComparer();

            public bool Equals(PropertyInfo left, PropertyInfo right)
            {
                return SameProperty(left, right);
            }

            public int GetHashCode(PropertyInfo value)
            {
                unchecked
                {
                    return (value.Module.GetHashCode() * 397) ^
                        value.MetadataToken;
                }
            }
        }

        private sealed class FieldIdentityComparer :
            IEqualityComparer<FieldInfo>
        {
            public static readonly FieldIdentityComparer Instance =
                new FieldIdentityComparer();

            public bool Equals(FieldInfo left, FieldInfo right)
            {
                return SameField(left, right);
            }

            public int GetHashCode(FieldInfo value)
            {
                unchecked
                {
                    return (value.Module.GetHashCode() * 397) ^
                        value.MetadataToken;
                }
            }
        }

        private struct Instruction
        {
            public Instruction(OpCode opCode, int token)
            {
                OpCode = opCode;
                Token = token;
            }

            public OpCode OpCode;
            public int Token;
        }
    }
}
