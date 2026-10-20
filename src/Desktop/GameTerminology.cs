using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dustweave.Desktop;

// Reviewed game terms, applied to presentation templates before inserting user values.
// Never use this to rewrite task IDs, saved preferences, receipts or raw diagnostics.
internal static class GameTerminology
{
    private sealed record Rule(string Source, Dictionary<string, string[]> Aliases, Dictionary<string, string> Names);
    private sealed record Vocabulary(Dictionary<string, string> Words, Regex Pattern);
    private static readonly Dictionary<string, Vocabulary> Vocabularies = Load();

    private static Dictionary<string, Vocabulary> Load()
    {
        using var stream = typeof(GameTerminology).Assembly.GetManifestResourceStream("Dustweave.UI.game-terms.json")
            ?? throw new InvalidDataException("Game terminology is missing.");
        var rules = JsonSerializer.Deserialize<Rule[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return new[] { "zh-CN", "zh-TW", "en-US" }.ToDictionary(code => code, code =>
        {
            var words = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var rule in rules)
            {
                if (!rule.Names.TryGetValue(code, out var name)) continue;
                foreach (var alias in rule.Aliases.GetValueOrDefault(code, []).Append(name))
                {
                    if (words.TryGetValue(alias, out var prior) && prior != name)
                        throw new InvalidDataException("Conflicting game term: " + alias);
                    words[alias] = name;
                }
            }
            string pattern = string.Join("|", words.Keys.OrderByDescending(k => k.Length).ThenBy(k => k, StringComparer.Ordinal).Select(Regex.Escape));
            // Protect complete English words and canonical multiword terms from partial replacement.
            if (code == "en-US") pattern = @"(?<![\p{L}\p{N}_])(?:" + pattern + @")(?![\p{L}\p{N}_])";
            return new Vocabulary(words, new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
        });
    }

    internal static string Template(string text, string language)
    {
        if (string.IsNullOrEmpty(text) || !Vocabularies.TryGetValue(language, out var vocabulary)) return text;
        return vocabulary.Pattern.Replace(text, match => vocabulary.Words[match.Value]);
    }
}
