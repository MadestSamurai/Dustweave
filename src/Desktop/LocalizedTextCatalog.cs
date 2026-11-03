using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dustweave.Desktop;

// Presentation resources are indexed by stable keys. Legacy input is a separate,
// frozen protocol adapter: editing or reordering translations never changes it.
internal sealed class LocalizedTextCatalog
{
    internal static readonly string[] Codes = ["zh-CN", "zh-TW", "en-US"];
    internal Dictionary<string, Dictionary<string, string>> Entries { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, string> LegacyInputs { get; }
    private readonly HashSet<string> problems = new(StringComparer.Ordinal);
    internal string[] Problems { get { lock (problems) return problems.ToArray(); } }
    internal DailyLocalizationDiagnostics Diagnostics(string code) => new(code, Problems);

    internal LocalizedTextCatalog(Func<string, Stream> open)
    {
        foreach (string name in new[] { "strings", "plugin-strings", "runtime", "notices" })
        {
            using var stream = open(name);
            using var document = JsonDocument.Parse(stream);
            foreach (var item in document.RootElement.EnumerateObject())
                Entries.Add(item.Name, Codes.Where(code => item.Value.TryGetProperty(code, out _))
                    .ToDictionary(code => code, code => item.Value.GetProperty(code).GetString() ?? "", StringComparer.Ordinal));
        }
        using var legacy = open("legacy-inputs");
        using var sources = JsonDocument.Parse(legacy);
        LegacyInputs = new(StringComparer.Ordinal);
        foreach (var source in sources.RootElement.EnumerateObject()) LegacyInputs.Add(source.Name, source.Value.GetString()!);
    }

    internal string Get(string key, string code, params object?[] args)
    {
        if (!Entries.TryGetValue(key, out var entry))
        {
            Problem("Missing UI key: " + key + "/" + code);
            return "[" + key + "]";
        }
        string value;
        if (!entry.TryGetValue(code, out value!) || string.IsNullOrWhiteSpace(value))
        {
            Problem("Missing translation: " + key + "/" + code);
            value = entry.GetValueOrDefault("zh-CN", "[" + key + "]");
        }
        if (args.Length == 0) return value;
        try { return string.Format(CultureInfo.GetCultureInfo(Codes.Contains(code) ? code : "zh-CN"), value, args); }
        catch (FormatException)
        {
            // Text cannot interrupt execution/recovery. Diagnostics retain the raw
            // message; the development audit, not the user's queue, fails here.
            Problem("Invalid text arguments: " + key + "/" + code);
            return value;
        }
    }

    internal string SearchText(string key) => Entries.TryGetValue(key, out var entry)
        ? string.Join(" ", Codes.Select(code => entry.GetValueOrDefault(code, ""))) : "";

    private void Problem(string text)
    {
        lock (problems)
            if (problems.Count < DailyLocalizationDiagnostics.Limit)
                problems.Add(text[..Math.Min(text.Length, DailyLocalizationDiagnostics.MaxDetailLength)]);
    }

    internal void Audit()
    {
        foreach (var (key, entry) in Entries)
        {
            if (!Codes.All(code => entry.TryGetValue(code, out var text) && !string.IsNullOrWhiteSpace(text)))
                throw new InvalidDataException("Incomplete translation: " + key);
            var arguments = Arguments(entry["zh-CN"]);
            foreach (string code in Codes)
                if (!Arguments(entry[code]).SequenceEqual(arguments))
                    throw new InvalidDataException("Translation arguments differ: " + key);
        }
        foreach (var (source, key) in LegacyInputs)
            if (source.Length == 0 || !Entries.ContainsKey(key))
                throw new InvalidDataException("Invalid legacy text mapping: " + key);
        if (Problems.Length != 0) throw new InvalidDataException(string.Join("\n", Problems));
    }

    private static int[] Arguments(string text)
    {
        _ = System.Text.CompositeFormat.Parse(text);
        return Regex.Matches(text, @"(?<!\{)\{(\d+)(?:[^}]*)\}")
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().Order().ToArray();
    }
}
