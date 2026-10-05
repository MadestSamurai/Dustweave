using System.Reflection;
using System.Reflection.Emit;

internal sealed class GameDatabaseApi
{
    private static readonly OpCode?[] SingleByteOpCodes = new OpCode?[0x100];
    private static readonly OpCode?[] MultiByteOpCodes = new OpCode?[0x100];

    static GameDatabaseApi()
    {
        foreach (var field in typeof(OpCodes).GetFields(
                     BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode)
            {
                continue;
            }

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

    private GameDatabaseApi(
        Type databaseType,
        Type queryType,
        MethodInfo openMethod,
        MethodInfo setKeyMethod,
        ConstructorInfo queryConstructor,
        MethodInfo stepMethod,
        MethodInfo columnCountMethod,
        MethodInfo columnNameMethod,
        MethodInfo blobMethod)
    {
        DatabaseType = databaseType;
        QueryType = queryType;
        OpenMethod = openMethod;
        SetKeyMethod = setKeyMethod;
        QueryConstructor = queryConstructor;
        StepMethod = stepMethod;
        ColumnCountMethod = columnCountMethod;
        ColumnNameMethod = columnNameMethod;
        BlobMethod = blobMethod;
    }

    public Type DatabaseType { get; }
    public Type QueryType { get; }
    public MethodInfo OpenMethod { get; }
    public MethodInfo SetKeyMethod { get; }
    public ConstructorInfo QueryConstructor { get; }
    public MethodInfo StepMethod { get; }
    public MethodInfo ColumnCountMethod { get; }
    public MethodInfo ColumnNameMethod { get; }
    public MethodInfo BlobMethod { get; }

    public static GameDatabaseApi Resolve(Assembly assembly)
    {
        var candidates = GetLoadableTypes(assembly)
            .Where(type =>
                type.IsClass &&
                !type.IsAbstract &&
                typeof(IDisposable).IsAssignableFrom(type))
            .SelectMany(BuildCandidates)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.QueryType.MetadataToken)
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new TypeLoadException(
                "Could not identify the game SQLite wrapper by its API shape.");
        }

        var best = candidates[0];
        if (best.Score < 60 ||
            candidates.Skip(1).Any(candidate => candidate.Score == best.Score))
        {
            throw new TypeLoadException(
                "The game SQLite wrapper shape was ambiguous. " +
                string.Join(
                    " | ",
                    candidates.Take(4).Select(candidate =>
                        $"{candidate.DatabaseType.FullName}/{candidate.QueryType.FullName} " +
                        $"score={candidate.Score}")));
        }

        return new GameDatabaseApi(
            best.DatabaseType,
            best.QueryType,
            best.OpenMethod,
            best.SetKeyMethod,
            best.QueryConstructor,
            best.StepMethod,
            best.ColumnCountMethod,
            best.ColumnNameMethod,
            best.BlobMethod);
    }

    private static IEnumerable<Candidate> BuildCandidates(Type queryType)
    {
        var methods = queryType.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var constructors = queryType.GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance);

        foreach (var constructor in constructors)
        {
            var parameters = constructor.GetParameters();
            if (parameters.Length < 2 ||
                parameters[1].ParameterType != typeof(string))
            {
                continue;
            }

            var databaseType = parameters[0].ParameterType;
            var databaseMethods = databaseType.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.DeclaredOnly);
            var openMethod = databaseMethods
                .Where(method =>
                    method.ReturnType == typeof(void) &&
                    ParametersAre(method, typeof(string), typeof(bool)))
                .OrderByDescending(method =>
                    ContainsString(method, "DB Open") ? 1 : 0)
                .ThenBy(method => method.MetadataToken)
                .FirstOrDefault();
            var setKeyMethod = databaseMethods
                .Where(method =>
                    method.ReturnType == typeof(void) &&
                    method.GetParameters().Length == 0 &&
                    ContainsString(method, "SQLite success set"))
                .OrderBy(method => method.MetadataToken)
                .FirstOrDefault();
            var stepMethod = methods
                .Where(method =>
                    method.ReturnType == typeof(bool) &&
                    method.GetParameters().Length == 0)
                .OrderByDescending(method =>
                    ContainsString(method, "SQLite step fail") ? 1 : 0)
                .ThenBy(method => method.MetadataToken)
                .FirstOrDefault();
            var columnCountMethod = methods
                .Where(method =>
                    method.ReturnType == typeof(int) &&
                    method.GetParameters().Length == 0)
                .OrderByDescending(method =>
                    ContainsString(method, "read column fail") ? 1 : 0)
                .ThenBy(method => method.MetadataToken)
                .FirstOrDefault();
            var columnNameMethod = methods
                .Where(method =>
                    method.ReturnType == typeof(string) &&
                    ParametersAre(method, typeof(int)))
                .OrderByDescending(method =>
                    ContainsString(method, "unknown field") ? 1 : 0)
                .ThenBy(method => method.MetadataToken)
                .FirstOrDefault();
            var blobMethod = methods
                .Where(method =>
                    method.ReturnType == typeof(byte[]) &&
                    ParametersAre(method, typeof(int)))
                .OrderBy(method => method.MetadataToken)
                .FirstOrDefault();

            if (openMethod is null ||
                setKeyMethod is null ||
                stepMethod is null ||
                columnCountMethod is null ||
                columnNameMethod is null ||
                blobMethod is null)
            {
                continue;
            }

            var score = 0;
            score += typeof(IDisposable).IsAssignableFrom(queryType) ? 10 : 0;
            score += parameters.Length == 2 ? 8 : 0;
            score += databaseType.GetConstructor(Type.EmptyTypes) is not null ? 8 : 0;
            score += ContainsString(openMethod, "DB Open") ? 12 : 0;
            score += ContainsString(setKeyMethod, "SQLite success set") ? 12 : 0;
            score += ContainsString(stepMethod, "SQLite step fail") ? 12 : 0;
            score += ContainsString(columnCountMethod, "read column fail") ? 12 : 0;
            score += ContainsString(columnNameMethod, "unknown field") ? 8 : 0;
            score += 8;

            yield return new Candidate(
                databaseType,
                queryType,
                openMethod,
                setKeyMethod,
                constructor,
                stepMethod,
                columnCountMethod,
                columnNameMethod,
                blobMethod,
                score);
        }
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types
                .Where(type => type is not null)
                .Cast<Type>()
                .ToArray();
        }
    }

    private static bool ParametersAre(MethodInfo method, params Type[] types)
    {
        var parameters = method.GetParameters();
        return parameters.Length == types.Length &&
            parameters.Select(parameter => parameter.ParameterType)
                .SequenceEqual(types);
    }

    private static bool ContainsString(MethodBase method, string fragment)
    {
        return GetLoadedStrings(method).Any(value =>
            value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static IEnumerable<string> GetLoadedStrings(MethodBase method)
    {
        var body = method.GetMethodBody();
        var bytes = body?.GetILAsByteArray();
        if (bytes is null)
        {
            yield break;
        }

        var offset = 0;
        while (offset < bytes.Length)
        {
            var maybeOpCode = ReadOpCode(bytes, ref offset);
            if (maybeOpCode is null)
            {
                yield break;
            }
            var opCode = maybeOpCode.Value;

            if (opCode.OperandType == OperandType.InlineString)
            {
                if (offset + 4 > bytes.Length)
                {
                    yield break;
                }
                var token = BitConverter.ToInt32(bytes, offset);
                string? value = null;
                try
                {
                    value = method.Module.ResolveString(token);
                }
                catch (ArgumentException)
                {
                }
                if (value is not null)
                {
                    yield return value;
                }
            }

            offset += OperandSize(opCode.OperandType, bytes, offset);
        }
    }

    private static OpCode? ReadOpCode(byte[] bytes, ref int offset)
    {
        if (offset >= bytes.Length)
        {
            return null;
        }

        var first = bytes[offset++];
        if (first != 0xfe)
        {
            return SingleByteOpCodes[first];
        }
        if (offset >= bytes.Length)
        {
            return null;
        }
        return MultiByteOpCodes[bytes[offset++]];
    }

    private static int OperandSize(
        OperandType operandType,
        byte[] bytes,
        int offset)
    {
        return operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget => 1,
            OperandType.ShortInlineI => 1,
            OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI => 4,
            OperandType.InlineBrTarget => 4,
            OperandType.InlineField => 4,
            OperandType.InlineMethod => 4,
            OperandType.InlineSig => 4,
            OperandType.InlineString => 4,
            OperandType.InlineTok => 4,
            OperandType.InlineType => 4,
            OperandType.ShortInlineR => 4,
            OperandType.InlineI8 => 8,
            OperandType.InlineR => 8,
            OperandType.InlineSwitch => SwitchOperandSize(bytes, offset),
            _ => throw new InvalidDataException(
                $"Unsupported IL operand type: {operandType}"),
        };
    }

    private static int SwitchOperandSize(byte[] bytes, int offset)
    {
        if (offset + 4 > bytes.Length)
        {
            return Math.Max(0, bytes.Length - offset);
        }
        var count = BitConverter.ToInt32(bytes, offset);
        return 4 + Math.Max(0, count) * 4;
    }

    private sealed record Candidate(
        Type DatabaseType,
        Type QueryType,
        MethodInfo OpenMethod,
        MethodInfo SetKeyMethod,
        ConstructorInfo QueryConstructor,
        MethodInfo StepMethod,
        MethodInfo ColumnCountMethod,
        MethodInfo ColumnNameMethod,
        MethodInfo BlobMethod,
        int Score);
}
