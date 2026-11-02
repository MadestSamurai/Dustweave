#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BD2ReplayCapture
{
    /// <summary>
    /// Resolves Inventory Battle runtime state from semantic member topology.
    /// Obfuscated identifiers are never persisted as the contract: the
    /// stable InventoryBattleSystem entry points, player initializer shape,
    /// health/stamina mutation topology and protobuf types are the evidence.
    /// </summary>
    internal static class InventoryBattleRuntimeShapeResolver
    {
        private const BindingFlags InstanceFlags =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance;
        private const BindingFlags StaticFlags =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static;

        internal static InventoryBattleRuntimeShape Resolve(Assembly assembly)
        {
            var blockers = new List<string>();
            if (assembly == null)
            {
                blockers.Add("game_assembly_missing");
                return new InventoryBattleRuntimeShape(blockers);
            }

            var systemType = assembly.GetType(
                "InventoryBattle.InventoryBattleSystem");
            if (systemType == null)
            {
                blockers.Add("runtime_system_missing");
                return new InventoryBattleRuntimeShape(blockers);
            }

            var tick = systemType.GetMethods(InstanceFlags)
                .SingleOrDefault(method =>
                    method.Name == "Tick" &&
                    method.ReturnType == typeof(void) &&
                    ParametersAre(method, typeof(float)));
            var activate = systemType.GetMethods(InstanceFlags)
                .SingleOrDefault(method =>
                    method.Name == "ActivateBattle" &&
                    method.ReturnType == typeof(void) &&
                    ParametersAre(method));
            if (tick == null)
            {
                blockers.Add("runtime_tick_missing");
            }
            if (activate == null)
            {
                blockers.Add("runtime_activate_missing");
            }

            var properties = systemType.GetProperties(
                    InstanceFlags | BindingFlags.DeclaredOnly)
                .Where(property =>
                    property.GetIndexParameters().Length == 0 &&
                    property.GetGetMethod(true) != null)
                .OrderBy(property => property.MetadataToken)
                .ToArray();
            var activeProperty = properties
                .SingleOrDefault(property =>
                    property.PropertyType == typeof(bool));
            var timeProperty = properties
                .SingleOrDefault(property =>
                    property.PropertyType == typeof(float));
            if (activeProperty == null)
            {
                blockers.Add("runtime_active_property_missing");
            }
            if (timeProperty == null)
            {
                blockers.Add("runtime_time_property_missing");
            }

            var playerGroup = properties
                .GroupBy(property => property.PropertyType)
                .Where(group => group.Count() == 2 &&
                    !group.Key.IsPrimitive && !group.Key.IsEnum)
                .Select(group => new
                {
                    Type = group.Key,
                    Properties = group.OrderBy(property =>
                        property.MetadataToken).ToArray(),
                    IntFields = group.Key.GetFields(
                            InstanceFlags | BindingFlags.DeclaredOnly)
                        .Count(field => field.FieldType == typeof(int)),
                    FloatFields = group.Key.GetFields(
                            InstanceFlags | BindingFlags.DeclaredOnly)
                        .Count(field => field.FieldType == typeof(float)),
                })
                .Where(group => group.IntFields >= 2 &&
                    group.FloatFields >= 2)
                .OrderByDescending(group =>
                    group.IntFields + group.FloatFields)
                .ToArray();
            if (playerGroup.Length != 1)
            {
                blockers.Add("runtime_player_properties_ambiguous");
                return new InventoryBattleRuntimeShape(blockers)
                {
                    SystemType = systemType,
                    TickMethod = tick,
                    ActivateMethod = activate,
                    ActiveProperty = activeProperty,
                    TimeProperty = timeProperty,
                };
            }

            var playerType = playerGroup[0].Type;
            var playerProperties = playerGroup[0].Properties;
            var health = ResolveMutableScalarPair(playerType, typeof(int));
            var stamina = ResolveMutableScalarPair(playerType, typeof(float));
            if (health == null)
            {
                blockers.Add("runtime_health_fields_ambiguous");
            }
            if (stamina == null)
            {
                blockers.Add("runtime_stamina_fields_ambiguous");
            }

            var damageCandidates = health == null
                ? new MethodInfo[0]
                : playerType.GetMethods(
                        InstanceFlags | BindingFlags.DeclaredOnly)
                    .Where(method =>
                        method.ReturnType == typeof(int) &&
                        ParametersAre(method, typeof(int), typeof(bool)) &&
                        RuntimeCharacterShapeResolver
                            .ReferencedFields(method)
                            .Contains(health.Current) &&
                        RuntimeCharacterShapeResolver
                            .WrittenFields(method)
                            .Contains(health.Current))
                    .OrderBy(method => method.MetadataToken)
                    .ToArray();
            var damageMethod = damageCandidates.Length == 1
                ? damageCandidates[0]
                : null;
            if (damageMethod == null)
            {
                blockers.Add("runtime_damage_method_ambiguous");
            }

            FieldInfo guard = null;
            if (health != null && damageMethod != null)
            {
                var written = RuntimeCharacterShapeResolver
                    .WrittenFields(damageMethod);
                var guardCandidates = RuntimeCharacterShapeResolver
                    .ReferencedFields(damageMethod)
                    .Where(field =>
                        field.DeclaringType == playerType &&
                        field.FieldType == typeof(int) &&
                        field != health.Current &&
                        field != health.Maximum &&
                        written.Contains(field))
                    .Distinct(FieldIdentityComparer.Instance)
                    .ToArray();
                if (guardCandidates.Length == 1)
                {
                    guard = guardCandidates[0];
                }
                else
                {
                    guard = guardCandidates.SingleOrDefault(field =>
                        field.MetadataToken ==
                        health.Maximum.MetadataToken + 1);
                }
            }
            if (guard == null)
            {
                blockers.Add("runtime_energy_guard_field_ambiguous");
            }

            var healingCandidates = health == null
                ? new MethodInfo[0]
                : playerType.GetMethods(
                        InstanceFlags | BindingFlags.DeclaredOnly)
                    .Where(method =>
                        method.ReturnType == typeof(void) &&
                        ParametersAre(method, typeof(int)) &&
                        RuntimeCharacterShapeResolver
                            .ReferencedFields(method)
                            .Contains(health.Maximum) &&
                        RuntimeCharacterShapeResolver
                            .WrittenFields(method)
                            .Contains(health.Current))
                    .OrderBy(method => method.MetadataToken)
                    .ToArray();
            var modifiedHealingCandidates = damageMethod == null
                ? new MethodInfo[0]
                : healingCandidates.Where(method => MethodUsesMetadataToken(
                        method, damageMethod.MetadataToken))
                    .ToArray();
            var directHealingCandidates = damageMethod == null
                ? new MethodInfo[0]
                : healingCandidates.Where(method => !MethodUsesMetadataToken(
                        method, damageMethod.MetadataToken))
                    .ToArray();
            var modifiedHealing = modifiedHealingCandidates.Length == 1
                ? modifiedHealingCandidates[0]
                : null;
            var directHealing = directHealingCandidates.Length == 1
                ? directHealingCandidates[0]
                : null;
            var healingMethods = modifiedHealing == null ||
                    directHealing == null ||
                    healingCandidates.Length != 2
                ? new MethodInfo[0]
                : new[] { modifiedHealing, directHealing };
            if (healingMethods.Length != 2)
            {
                blockers.Add("runtime_healing_methods_ambiguous");
            }

            var healthWriters = health == null
                ? new MethodInfo[0]
                : GetLoadableTypes(assembly)
                    .SelectMany(GetDeclaredMethods)
                    .Where(method => RuntimeCharacterShapeResolver
                        .WrittenFields(method)
                        .Contains(health.Current))
                    .OrderBy(method => method.MetadataToken)
                    .ToArray();
            var initializationMethods = health == null
                ? new MethodInfo[0]
                : healthWriters.Where(method =>
                        method.DeclaringType == playerType &&
                        RuntimeCharacterShapeResolver
                            .WrittenFields(method)
                            .Contains(health.Maximum))
                    .ToArray();
            var initializationMethod = initializationMethods.Length == 1
                ? initializationMethods[0]
                : null;
            var coveredHealthWriters = new[] { damageMethod }
                .Concat(healingMethods)
                .Concat(initializationMethods)
                .Where(method => method != null)
                .Distinct()
                .ToArray();
            if (initializationMethods.Length != 1 ||
                healthWriters.Any(method =>
                    !coveredHealthWriters.Contains(method)))
            {
                blockers.Add("runtime_health_writer_coverage_incomplete");
            }

            var effectSettlementCandidates = guard == null
                ? new MethodInfo[0]
                : GetLoadableTypes(assembly)
                    .SelectMany(GetDeclaredMethods)
                    .Where(method =>
                        method != damageMethod &&
                        method.DeclaringType != playerType &&
                        method.GetParameters().Any(parameter =>
                            parameter.ParameterType == playerType) &&
                        RuntimeCharacterShapeResolver
                            .WrittenFields(method)
                            .Contains(guard))
                    .OrderBy(method => method.MetadataToken)
                    .ToArray();
            var effectSettlement = effectSettlementCandidates.Length == 1
                ? effectSettlementCandidates[0]
                : null;
            var effectPlayerArgumentIndex = effectSettlement == null
                ? -1
                : Array.FindIndex(
                    effectSettlement.GetParameters(),
                    parameter => parameter.ParameterType == playerType);
            if (effectSettlement == null || effectPlayerArgumentIndex < 0)
            {
                blockers.Add("runtime_effect_settlement_method_ambiguous");
            }

            var maximumHealthWriters = health == null
                ? new MethodInfo[0]
                : GetLoadableTypes(assembly)
                    .SelectMany(GetDeclaredMethods)
                    .Where(method => RuntimeCharacterShapeResolver
                        .WrittenFields(method)
                        .Contains(health.Maximum))
                    .ToArray();
            var coveredMaximumHealthWriters = new[]
                {
                    initializationMethod,
                    effectSettlement,
                }
                .Where(method => method != null)
                .Distinct()
                .ToArray();
            if (maximumHealthWriters.Any(method =>
                !coveredMaximumHealthWriters.Contains(method)))
            {
                blockers.Add(
                    "runtime_maximum_health_writer_coverage_incomplete");
            }

            var guardWriters = guard == null
                ? new MethodInfo[0]
                : GetLoadableTypes(assembly)
                    .SelectMany(GetDeclaredMethods)
                    .Where(method => RuntimeCharacterShapeResolver
                        .WrittenFields(method)
                        .Contains(guard))
                    .ToArray();
            var coveredGuardWriters = new[]
                {
                    damageMethod,
                    effectSettlement,
                    initializationMethod,
                }
                .Where(method => method != null)
                .Distinct()
                .ToArray();
            if (guardWriters.Any(method =>
                !coveredGuardWriters.Contains(method)))
            {
                blockers.Add("runtime_energy_guard_writer_coverage_incomplete");
            }

            var rngCandidates = systemType.GetFields(
                    InstanceFlags | BindingFlags.DeclaredOnly)
                .Where(field =>
                    !field.FieldType.IsPrimitive &&
                    field.FieldType.GetConstructors(InstanceFlags)
                        .Any(constructor => constructor.GetParameters()
                            .Select(parameter => parameter.ParameterType)
                            .SequenceEqual(new[] { typeof(int) })) &&
                    field.FieldType.GetFields(
                            InstanceFlags | BindingFlags.DeclaredOnly)
                        .Any(candidate =>
                            candidate.FieldType == typeof(ulong) ||
                            candidate.FieldType == typeof(ulong[])))
                .OrderBy(field => field.MetadataToken)
                .ToArray();
            var rngField = rngCandidates.Length == 1
                ? rngCandidates[0]
                : null;
            var rngStateFields = rngField == null
                ? new FieldInfo[0]
                : rngField.FieldType.GetFields(
                        InstanceFlags | BindingFlags.DeclaredOnly)
                    .Where(field => field.FieldType == typeof(ulong))
                    .Concat(rngField.FieldType.GetFields(
                            InstanceFlags | BindingFlags.DeclaredOnly)
                        .Where(field => field.FieldType == typeof(ulong[])))
                    .OrderBy(field => field.MetadataToken)
                    .ToArray();
            if (rngField == null)
            {
                blockers.Add("runtime_rng_state_missing");
            }

            var attackCandidates = damageMethod == null
                ? new MethodInfo[0]
                : systemType.GetMethods(
                        InstanceFlags | BindingFlags.DeclaredOnly)
                    .Where(method =>
                    {
                        var parameters = method.GetParameters();
                        return method.ReturnType == typeof(bool) &&
                            parameters.Length == 5 &&
                            !parameters[0].ParameterType.IsPrimitive &&
                            parameters[1].ParameterType == playerType &&
                            parameters[2].ParameterType == playerType &&
                            parameters[3].ParameterType ==
                                parameters[4].ParameterType &&
                            !parameters[3].ParameterType.IsPrimitive &&
                            MethodUsesMetadataToken(
                                method, damageMethod.MetadataToken);
                    })
                    .OrderBy(method => method.MetadataToken)
                    .ToArray();
            var attackMethod = attackCandidates.Length == 1
                ? attackCandidates[0]
                : null;
            if (attackMethod == null)
            {
                blockers.Add("runtime_attack_method_ambiguous");
            }

            FieldInfo unitInventoryIndex = null;
            if (attackMethod != null)
            {
                var unitType = attackMethod.GetParameters()[0].ParameterType;
                var inventoryCandidates = GetInstanceFields(unitType)
                    .Where(field => field.FieldType == typeof(long))
                    .OrderBy(field => field.MetadataToken)
                    .ToArray();
                if (inventoryCandidates.Length == 1)
                {
                    unitInventoryIndex = inventoryCandidates[0];
                }
            }
            if (unitInventoryIndex == null)
            {
                blockers.Add("runtime_unit_inventory_index_ambiguous");
            }

            var animationField = systemType.GetFields(
                    InstanceFlags | BindingFlags.DeclaredOnly)
                .SingleOrDefault(field => IsAnimationAction(field.FieldType));
            var animationMethods = animationField == null
                ? new MethodInfo[0]
                : systemType.GetMethods(
                        InstanceFlags | BindingFlags.DeclaredOnly)
                    .Where(method =>
                        method.ReturnType == typeof(void) &&
                        ParametersAre(method, typeof(long), typeof(int)) &&
                        RuntimeCharacterShapeResolver
                            .ReferencedFields(method)
                            .Contains(animationField))
                    .OrderBy(method => method.MetadataToken)
                    .ToArray();
            if (animationMethods.Length != 2)
            {
                blockers.Add("runtime_animation_callbacks_ambiguous");
            }

            var endResponseType = assembly.GetType(
                "Proto.Net.IbStageEndResponse");
            var endRequestType = assembly.GetType(
                "Proto.Net.IbStageEndRequest");
            var endParser = endResponseType?.GetProperty(
                "Parser", StaticFlags)?.GetGetMethod(true);
            var resultSetter = endRequestType?.GetProperty(
                "BattleResult", InstanceFlags)?.GetSetMethod(true);
            var endResponseCallbacks = endParser == null
                ? new MethodInfo[0]
                : GetLoadableTypes(assembly)
                    .SelectMany(GetDeclaredMethods)
                    .Where(IsNetworkResponseCallback)
                    .Where(method => MethodUsesMetadataToken(
                        method, endParser.MetadataToken))
                    .OrderBy(method => method.MetadataToken)
                    .ToArray();
            var endRequestMethods = resultSetter == null ||
                    endResponseType == null
                ? new MethodInfo[0]
                : GetLoadableTypes(assembly)
                    .SelectMany(GetDeclaredMethods)
                    .Where(method =>
                    {
                        var parameters = method.GetParameters();
                        return method.ReturnType == typeof(void) &&
                            parameters.Length == 2 &&
                            parameters[0].ParameterType == typeof(int) &&
                            IsActionOf(
                                parameters[1].ParameterType,
                                endResponseType) &&
                            MethodUsesMetadataToken(
                                method, resultSetter.MetadataToken);
                    })
                    .OrderBy(method => method.MetadataToken)
                    .ToArray();
            if (endResponseCallbacks.Length != 1)
            {
                blockers.Add("runtime_end_response_callback_ambiguous");
            }
            if (endRequestMethods.Length != 1)
            {
                blockers.Add("runtime_end_request_method_ambiguous");
            }

            return new InventoryBattleRuntimeShape(blockers)
            {
                SystemType = systemType,
                TickMethod = tick,
                ActivateMethod = activate,
                ActiveProperty = activeProperty,
                TimeProperty = timeProperty,
                BluePlayerProperty = playerProperties[0],
                RedPlayerProperty = playerProperties[1],
                CurrentHealthField = health?.Current,
                MaximumHealthField = health?.Maximum,
                CurrentStaminaField = stamina?.Current,
                MaximumStaminaField = stamina?.Maximum,
                EnergyGuardField = guard,
                DamageMethod = damageMethod,
                HealingMethods = healingMethods,
                InitializationMethod = initializationMethod,
                EffectSettlementMethod = effectSettlement,
                EffectPlayerArgumentIndex = effectPlayerArgumentIndex,
                AttackMethod = attackMethod,
                UnitInventoryIndexField = unitInventoryIndex,
                RngField = rngField,
                RngStateFields = rngStateFields,
                BlueAnimationMethod = animationMethods.Length == 2
                    ? animationMethods[0]
                    : null,
                RedAnimationMethod = animationMethods.Length == 2
                    ? animationMethods[1]
                    : null,
                EndRequestMethod = endRequestMethods.Length == 1
                    ? endRequestMethods[0]
                    : null,
                EndResponseCallback = endResponseCallbacks.Length == 1
                    ? endResponseCallbacks[0]
                    : null,
            };
        }

        private static ScalarPair ResolveMutableScalarPair(
            Type owner,
            Type scalarType)
        {
            var scores = new Dictionary<string, PairScore>();
            foreach (var method in owner.GetMethods(
                InstanceFlags | BindingFlags.DeclaredOnly))
            {
                var referenced = RuntimeCharacterShapeResolver
                    .ReferencedFields(method)
                    .Where(field =>
                        field.DeclaringType == owner &&
                        field.FieldType == scalarType)
                    .ToArray();
                var written = RuntimeCharacterShapeResolver
                    .WrittenFields(method)
                    .Where(field =>
                        field.DeclaringType == owner &&
                        field.FieldType == scalarType)
                    .ToArray();
                foreach (var current in written)
                {
                    foreach (var maximum in referenced.Where(field =>
                        field != current && !written.Contains(field)))
                    {
                        var key = current.MetadataToken.ToString("X8") + ":" +
                            maximum.MetadataToken.ToString("X8");
                        PairScore score;
                        if (!scores.TryGetValue(key, out score))
                        {
                            score = new PairScore(current, maximum);
                            scores.Add(key, score);
                        }
                        score.Score++;
                    }
                }
            }
            var ranked = scores.Values
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Current.MetadataToken)
                .ThenBy(candidate => candidate.Maximum.MetadataToken)
                .ToArray();
            if (ranked.Length == 0 || ranked[0].Score < 2)
            {
                return null;
            }
            var top = ranked.Where(candidate =>
                    candidate.Score == ranked[0].Score)
                .ToArray();
            if (top.Length > 1)
            {
                top = top.Where(candidate =>
                        Math.Abs(candidate.Current.MetadataToken -
                            candidate.Maximum.MetadataToken) == 1)
                    .ToArray();
            }
            return top.Length == 1
                ? new ScalarPair(top[0].Current, top[0].Maximum)
                : null;
        }

        private static bool IsAnimationAction(Type type)
        {
            if (type == null || !type.IsGenericType ||
                type.GetGenericTypeDefinition() != typeof(Action<,,>))
            {
                return false;
            }
            var arguments = type.GetGenericArguments();
            return arguments.Length == 3 &&
                arguments[0] == typeof(long) &&
                arguments[1].IsEnum &&
                arguments[2] == typeof(int);
        }

        private static IEnumerable<FieldInfo> GetInstanceFields(Type type)
        {
            for (var current = type;
                current != null && current != typeof(object);
                current = current.BaseType)
            {
                foreach (var field in current.GetFields(
                    InstanceFlags | BindingFlags.DeclaredOnly))
                {
                    yield return field;
                }
            }
        }

        private static bool IsActionOf(Type type, Type argument)
        {
            return type != null && type.IsGenericType &&
                type.GetGenericTypeDefinition() == typeof(Action<>) &&
                type.GetGenericArguments()[0] == argument;
        }

        private static bool ParametersAre(
            MethodInfo method,
            params Type[] expected)
        {
            var parameters = method.GetParameters();
            return parameters.Length == expected.Length &&
                parameters.Select(parameter => parameter.ParameterType)
                    .SequenceEqual(expected);
        }

        private static bool IsNetworkResponseCallback(MethodInfo method)
        {
            var parameters = method.GetParameters();
            return method.ReturnType == typeof(bool) &&
                parameters.Length == 3 &&
                parameters[0].ParameterType == typeof(byte[]) &&
                parameters[1].ParameterType == typeof(int) &&
                parameters[2].ParameterType == typeof(int);
        }

        private static bool MethodUsesMetadataToken(
            MethodInfo method,
            int metadataToken)
        {
            try
            {
                var bytes = method.GetMethodBody()?.GetILAsByteArray();
                if (bytes == null || bytes.Length < 4)
                {
                    return false;
                }
                var token = BitConverter.GetBytes(metadataToken);
                for (var index = 0;
                    index <= bytes.Length - token.Length;
                    index++)
                {
                    if (bytes[index] == token[0] &&
                        bytes[index + 1] == token[1] &&
                        bytes[index + 2] == token[2] &&
                        bytes[index + 3] == token[3])
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null);
            }
        }

        private static IEnumerable<MethodInfo> GetDeclaredMethods(Type type)
        {
            try
            {
                return type.GetMethods(
                    InstanceFlags | StaticFlags |
                    BindingFlags.DeclaredOnly);
            }
            catch
            {
                return Enumerable.Empty<MethodInfo>();
            }
        }

        private sealed class PairScore
        {
            internal PairScore(FieldInfo current, FieldInfo maximum)
            {
                Current = current;
                Maximum = maximum;
            }

            internal FieldInfo Current { get; private set; }
            internal FieldInfo Maximum { get; private set; }
            internal int Score { get; set; }
        }

        private sealed class ScalarPair
        {
            internal ScalarPair(FieldInfo current, FieldInfo maximum)
            {
                Current = current;
                Maximum = maximum;
            }

            internal FieldInfo Current { get; private set; }
            internal FieldInfo Maximum { get; private set; }
        }

        private sealed class FieldIdentityComparer :
            IEqualityComparer<FieldInfo>
        {
            internal static readonly FieldIdentityComparer Instance =
                new FieldIdentityComparer();

            public bool Equals(FieldInfo left, FieldInfo right)
            {
                if (ReferenceEquals(left, right))
                {
                    return true;
                }
                return left != null && right != null &&
                    left.Module == right.Module &&
                    left.MetadataToken == right.MetadataToken;
            }

            public int GetHashCode(FieldInfo value)
            {
                return value == null
                    ? 0
                    : value.MetadataToken;
            }
        }
    }

    internal sealed class InventoryBattleRuntimeShape
    {
        internal InventoryBattleRuntimeShape(IEnumerable<string> blockers)
        {
            Blockers = (blockers ?? Enumerable.Empty<string>()).ToArray();
        }

        internal string[] Blockers { get; private set; }
        internal bool Resolved { get { return Blockers.Length == 0; } }
        internal Type SystemType { get; set; }
        internal MethodInfo TickMethod { get; set; }
        internal MethodInfo ActivateMethod { get; set; }
        internal PropertyInfo ActiveProperty { get; set; }
        internal PropertyInfo TimeProperty { get; set; }
        internal PropertyInfo BluePlayerProperty { get; set; }
        internal PropertyInfo RedPlayerProperty { get; set; }
        internal FieldInfo CurrentHealthField { get; set; }
        internal FieldInfo MaximumHealthField { get; set; }
        internal FieldInfo CurrentStaminaField { get; set; }
        internal FieldInfo MaximumStaminaField { get; set; }
        internal FieldInfo EnergyGuardField { get; set; }
        internal MethodInfo DamageMethod { get; set; }
        internal MethodInfo[] HealingMethods { get; set; } = new MethodInfo[0];
        internal MethodInfo InitializationMethod { get; set; }
        internal MethodInfo EffectSettlementMethod { get; set; }
        internal int EffectPlayerArgumentIndex { get; set; } = -1;
        internal MethodInfo AttackMethod { get; set; }
        internal FieldInfo UnitInventoryIndexField { get; set; }
        internal FieldInfo RngField { get; set; }
        internal FieldInfo[] RngStateFields { get; set; } = new FieldInfo[0];
        internal MethodInfo BlueAnimationMethod { get; set; }
        internal MethodInfo RedAnimationMethod { get; set; }
        internal MethodInfo EndRequestMethod { get; set; }
        internal MethodInfo EndResponseCallback { get; set; }
    }
}
