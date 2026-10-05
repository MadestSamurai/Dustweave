using System.Text;
using System.Text.Json;

internal static class LegacyTableMerger
{
    private static readonly IReadOnlyDictionary<string, string[]> PrimaryKeys =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["BattleDefaultTable"] = ["id"],
            ["BattleFieldBuffTable"] = ["id"],
            ["BuffConditionTable"] = ["id"],
            ["BuffTable"] = ["id"],
            ["CharAwakeGrowthTable"] = ["id"],
            ["CharAwakeTable"] = ["id"],
            ["CharGrowthTable"] = ["id"],
            ["CharImprintTable"] = ["id"],
            ["CharLevelTable"] = ["groupId", "id"],
            ["CharTable"] = ["id"],
            ["CostumeBurstTable"] = ["groupId", "id"],
            ["CostumeGrowthTable"] = ["groupId", "id"],
            ["CostumeNodeGroupTable"] = ["id"],
            ["CostumeNodeTable"] = ["groupId", "id"],
            ["CostumeTable"] = ["id"],
            ["EquipmentGradeTable"] = ["id"],
            ["EquipmentGrowthTable"] = ["groupId", "id"],
            ["EquipmentOptionTable"] = ["groupId", "id"],
            ["EquipmentRankTable"] = ["groupId", "id"],
            ["EquipmentTable"] = ["id"],
            ["PvpDefaultTable"] = ["id"],
            ["SkillTable"] = ["groupId", "id"],
        };

    public static int MergeTable(
        string currentPath,
        string legacyPath,
        string tableName)
    {
        if (!PrimaryKeys.TryGetValue(tableName, out var primaryKey))
        {
            throw new InvalidOperationException(
                $"No legacy compatibility key is defined for {tableName}.");
        }

        using var current = JsonDocument.Parse(File.ReadAllText(currentPath));
        using var legacy = JsonDocument.Parse(File.ReadAllText(legacyPath));
        RequireArray(current.RootElement, currentPath);
        RequireArray(legacy.RootElement, legacyPath);

        var currentKeys = ReadUniqueKeys(
            current.RootElement,
            primaryKey,
            tableName,
            "current");
        var legacyKeys = new HashSet<string>(StringComparer.Ordinal);
        var retained = new List<string>();
        foreach (var row in legacy.RootElement.EnumerateArray())
        {
            var key = BuildKey(row, primaryKey, tableName);
            if (!legacyKeys.Add(key))
            {
                throw new InvalidDataException(
                    $"{tableName} legacy snapshot contains duplicate key {key}.");
            }
            if (!currentKeys.Contains(key))
            {
                retained.Add(row.GetRawText());
            }
        }

        if (retained.Count == 0)
        {
            return 0;
        }

        var currentText = File.ReadAllText(currentPath);
        var closingBracket = currentText.LastIndexOf(']');
        if (closingBracket < 0 ||
            currentText[(closingBracket + 1)..].Any(character =>
                !char.IsWhiteSpace(character)))
        {
            throw new InvalidDataException(
                $"{currentPath} is not a standalone JSON array.");
        }

        var prefix = currentText[..closingBracket].TrimEnd();
        var builder = new StringBuilder(prefix);
        if (current.RootElement.GetArrayLength() > 0)
        {
            builder.AppendLine(",");
        }
        else
        {
            builder.AppendLine();
        }

        for (var index = 0; index < retained.Count; index++)
        {
            builder.Append("  ");
            builder.Append(retained[index]);
            builder.AppendLine(index + 1 == retained.Count ? string.Empty : ",");
        }
        builder.AppendLine("]");
        File.WriteAllText(
            currentPath,
            builder.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return retained.Count;
    }

    private static HashSet<string> ReadUniqueKeys(
        JsonElement rows,
        IReadOnlyList<string> primaryKey,
        string tableName,
        string snapshot)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows.EnumerateArray())
        {
            var key = BuildKey(row, primaryKey, tableName);
            if (!keys.Add(key))
            {
                throw new InvalidDataException(
                    $"{tableName} {snapshot} snapshot contains duplicate key {key}.");
            }
        }
        return keys;
    }

    private static string BuildKey(
        JsonElement row,
        IReadOnlyList<string> primaryKey,
        string tableName)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"{tableName} contains a non-object row.");
        }

        return string.Join(
            ":",
            primaryKey.Select(key =>
            {
                if (!row.TryGetProperty(key, out var value))
                {
                    throw new InvalidDataException(
                        $"{tableName} row is missing primary-key field {key}.");
                }
                return value.GetRawText();
            }));
    }

    private static void RequireArray(JsonElement root, string path)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{path} is not a JSON array.");
        }
    }
}
