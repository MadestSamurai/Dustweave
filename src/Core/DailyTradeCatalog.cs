using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.Json;
namespace BD2Daily;

/// <summary>Static trade formulas, provenance and compatibility are owned by .NET.</summary>
public static class DailyTradeCatalog
{
    public static readonly string[] Tables = ["CookingTable", "FoodTable", "ProductTable", "SellItemTable", "ShopTable", "TalentSkillTable", "TalentTable", "NameTextTable", "CharTable"];
    public static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
    }
    public static string Mvid(string assembly)
    {
        using var file = File.OpenRead(assembly);
        using var pe = new PEReader(file);
        var reader = pe.GetMetadataReader();
        return reader.GetGuid(reader.GetModuleDefinition().Mvid).ToString();
    }
    public static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path).TrimStart('\uFEFF'))?.AsObject() ?? throw new InvalidDataException("跑商目录不是对象。");
    private static int I(JsonNode row, string key) => row[key]?.GetValue<int>() ?? throw new InvalidDataException("跑商表缺少整数：" + key);
    private static JsonArray A(JsonNode row, string key) => row[key]?.AsArray() ?? throw new InvalidDataException("跑商表缺少列表：" + key);
    public static JsonObject Build(string folder, string client)
    {
        if (!Guid.TryParse(client, out _))
            throw new InvalidDataException("跑商客户端MVID无效。");
        var manifest = Read(Path.Combine(folder, "manifest.json"));
        var tables = Tables.ToDictionary(n => n, n => JsonNode.Parse(File.ReadAllText(Path.Combine(folder, n + ".json")).TrimStart('\uFEFF'))!.AsArray());
        foreach (var pair in tables)
            if (manifest["tables"]?[pair.Key]?.GetValue<int>() != pair.Value.Count)
                throw new InvalidDataException("跑商导出覆盖不完整：" + pair.Key);
        var names = tables["NameTextTable"].ToDictionary(r => I(r!, "id"), r => r!["textCn"]!.GetValue<string>());
        var food = tables["FoodTable"].ToDictionary(r => I(r!, "id"), r => r!);
        var shops = tables["ShopTable"].Select(r => I(r!, "id")).ToHashSet();
        var aliases = new Dictionary<int, string> { { 1003, "淡水虾" }, { 1055, "闪烁的粉末" }, { 3003, "闪耀西班牙蒜味虾" }, { 3024, "橄榄油意大利面" } };
        var items = new List<JsonObject>();
        foreach (var node in tables["SellItemTable"])
        {
            var r = node!;
            int id = I(r, "elementId");
            if (I(r, "elementType") != 5 || !food.ContainsKey(id) || I(r, "highPremiumDay") == 0)
                continue;
            if (I(r, "priceType") != 4 || I(r, "elementCount") != 1 || I(r, "highPremiumRate") != 20)
                throw new InvalidDataException("不支持的新售卖公式。");
            var f = food[id];
            items.Add(new()
            {
                ["id"] = id,
                ["name"] = aliases.GetValueOrDefault(id, names[I(f, "itemNameTextId")]),
                ["sale"] = checked(I(r, "priceCount") * 120) / 100,
                ["stack"] = I(f, "stackCount"),
                ["day"] = I(r, "highPremiumDay"),
                ["shop"] = I(r, "highPremiumshopId")
            });
        }
        var ids = items.Select(r => I(r, "id")).ToHashSet();
        if (ids.Count != items.Count || ids.Count == 0)
            throw new InvalidDataException("跑商物品重复或为空。");
        var offers = new List<JsonObject>();
        foreach (var node in tables["ProductTable"])
        {
            var p = node!;
            if (I(p, "elementType") != 5 || I(p, "priceType") != 4 || !shops.Contains(I(p, "groupId")))
                continue;
            if (!ids.Contains(I(p, "elementId")) || I(p, "elementCount") != 1 || new[] { "noBargain", "reputationType", "discountRate", "premiumRate" }.Any(k => I(p, k) != 0))
                throw new InvalidDataException("不支持的新食材报价公式。");
            offers.Add(new()
            {
                ["shop"] = I(p, "groupId"),
                ["product"] = I(p, "id"),
                ["item"] = I(p, "elementId"),
                ["base_price"] = I(p, "priceCount"),
                ["limit"] = I(p, "buyMaxCount")
            });
        }
        var potions = new Dictionary<int, int>();
        foreach (var node in tables["TalentSkillTable"])
        {
            var r = node!;
            if (I(r, "classType") != 7)
                continue;
            int id = I(r, "id"), cost = I(r, "catalystValue");
            if (potions.TryGetValue(id, out int old) && old != cost)
                throw new InvalidDataException("料理药耗存在歧义。");
            potions[id] = cost;
        }
        var recipes = new List<JsonObject>();
        foreach (var node in tables["CookingTable"])
        {
            var r = node!;
            if (!ids.Contains(I(r, "resultItemId")))
                continue;
            var materials = A(r, "materialItemId");
            var counts = A(r, "materialItemCount");
            if (materials.Count != counts.Count)
                throw new InvalidDataException("配方材料数量不完整。");
            var parts = new JsonObject();
            for (int i = 0; i < materials.Count; i++)
            {
                int id = materials[i]!.GetValue<int>();
                if (!ids.Contains(id) || parts.ContainsKey(id.ToString()) || counts[i]!.GetValue<int>() <= 0)
                    throw new InvalidDataException("配方材料不能安全估值。");
                parts[id.ToString()] = counts[i]!.DeepClone();
            }
            recipes.Add(new()
            {
                ["id"] = I(r, "id"),
                ["output"] = I(r, "resultItemId"),
                ["count"] = I(r, "resultItemCount"),
                ["potions"] = potions[I(r, "talentLevel")],
                ["talent_level"] = I(r, "talentLevel"),
                ["materials"] = parts
            });
        }
        var talents = tables["TalentTable"].ToDictionary(r => I(r!, "id"), r => r!);
        var characters = new JsonObject();
        foreach (var node in tables["CharTable"])
        {
            var c = node!;
            if (!talents.TryGetValue(I(c, "talentId"), out var t) || I(t, "classType") is not (7 or 16))
                continue;
            var skills = new JsonObject();
            foreach (var s in tables["TalentSkillTable"].Where(r => I(r!, "groupId") == I(t, "talentSkillGroupId")))
            {
                string id = I(s!, "id").ToString();
                if (skills.ContainsKey(id))
                    throw new InvalidDataException("天赋等级重复。");
                skills[id] = new JsonObject { ["potions"] = I(s!, "catalystValue"), ["values"] = A(s!, "valueList").DeepClone() };
            }
            characters[I(c, "id").ToString()] = new JsonObject { ["kind"] = I(t, "classType"), ["skills"] = skills };
        }
        if (characters.Count == 0 || offers.Count == 0 || recipes.Count == 0)
            throw new InvalidDataException("跑商目录不完整。");
        var hashes = new JsonObject();
        foreach (string name in Tables.Where(n => n != "CharTable"))
            hashes[name] = Hash(Path.Combine(folder, name + ".json"));
        return new()
        {
            ["schema"] = 1,
            ["client"] = client,
            ["characters"] = characters,
            ["character_sha256"] = Hash(Path.Combine(folder, "CharTable.json")),
            ["database_sha256"] = manifest["databaseSha256"]!.DeepClone(),
            ["assembly_sha256"] = manifest["assemblySha256"]!.DeepClone(),
            ["source_hashes"] = hashes,
            ["items"] = new JsonArray(items.OrderBy(r => I(r, "id")).Select(r => (JsonNode)r).ToArray()),
            ["offers"] = new JsonArray(offers.OrderBy(r => I(r, "shop")).ThenBy(r => I(r, "product")).Select(r => (JsonNode)r).ToArray()),
            ["recipes"] = new JsonArray(recipes.OrderBy(r => I(r, "id")).Select(r => (JsonNode)r).ToArray())
        };
    }
    public static string[] Changes(JsonObject old, JsonObject current) => new[] { "schema", "client", "assembly_sha256", "characters", "items", "offers", "recipes" }.Where(k => !JsonNode.DeepEquals(old[k], current[k])).ToArray();
    public static string Fingerprint(JsonNode node)
    {
        // Match the unchanged oracle's UTF-8 canonical JSON, including non-BMP
        // names. Framework encoders otherwise escape surrogate pairs.
        string Quote(string text)
        {
            var b = new System.Text.StringBuilder("\"");
            foreach (char c in text)
                b.Append(c switch
                {
                    '"' => "\\\"",
                    '\\' => "\\\\",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    < ' ' => "\\u" + ((int)c).ToString("x4"),
                    _ => c.ToString()
                });
            return b.Append('"').ToString();
        }
        string Encode(JsonNode? value) => value switch
        {
            null => "null",
            JsonObject obj => "{" + string.Join(',', obj.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Quote(p.Key) + ":" + Encode(p.Value))) + "}",
            JsonArray array => "[" + string.Join(',', array.Select(Encode)) + "]",
            JsonValue val when val.GetValueKind() == JsonValueKind.String => Quote(val.GetValue<string>()),
            _ => value.ToJsonString()
        };
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Encode(node)))).ToLowerInvariant();
    }
    public static void ValidateFiles(JsonObject catalog, string assembly, string database)
    {
        foreach (var pair in new[] { (assembly, "assembly_sha256"), (database, "database_sha256") })
            if (!string.Equals(Hash(pair.Item1), catalog[pair.Item2]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("游戏数据在跑商准备期间发生变化：" + pair.Item2);
        if (Mvid(assembly) != catalog["client"]?.GetValue<string>())
            throw new InvalidDataException("跑商程序集身份不一致。");
    }
}
