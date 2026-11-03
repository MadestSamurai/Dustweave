using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dustweave.Desktop;

public sealed partial class DailyLanguage
{
    private readonly List<NoticeFormat> noticeFormats = [];
    private int noticeDepth;
    private void LoadNotices()
    {
        using var stream = typeof(DailyLanguage).Assembly.GetManifestResourceStream("Dustweave.UI.notices.json")
            ?? throw new InvalidDataException("Notice translations are missing.");
        using var document = JsonDocument.Parse(stream);
        foreach (var entry in document.RootElement.EnumerateObject())
        {
            if (!entry.Value.TryGetProperty("parameters", out var parameters)) continue;
            var kinds = parameters.EnumerateArray().Select(p => p.GetString()!).ToArray();
            // New structured messages need argument kinds but no prose grammar.
            // Only the frozen compatibility templates opt into matching raw input.
            if (!entry.Value.TryGetProperty("source", out var legacySource))
            {
                noticeFormats.Add(new(entry.Name, kinds, null));
                continue;
            }
            string source = legacySource.GetString()!;
            var expression = new StringBuilder(@"\A");
            int start = 0;
            var found = new HashSet<int>();
            foreach (Match placeholder in Regex.Matches(source, @"\{(\d+)\}"))
            {
                int index = int.Parse(placeholder.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (index >= kinds.Length || !found.Add(index)) throw new InvalidDataException("Invalid notice arguments: " + entry.Name);
                expression.Append(Regex.Escape(source[start..placeholder.Index]));
                expression.Append("(?<arg").Append(index).Append('>')
                    .Append(kinds[index] == "number" ? @"[0-9]+" : @"[^\r\n]+?").Append(')');
                start = placeholder.Index + placeholder.Length;
            }
            if (found.Count != kinds.Length || kinds.Any(k => k is not ("number" or "value" or "message" or "stage")))
                throw new InvalidDataException("Invalid notice format: " + entry.Name);
            expression.Append(Regex.Escape(source[start..])).Append(@"\z");
            noticeFormats.Add(new(entry.Name, kinds, new Regex(expression.ToString(), RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromMilliseconds(50))));
        }
    }
    private object?[] NoticeArguments(string key, string[] arguments)
    {
        var format = noticeFormats.FirstOrDefault(f => f.Key == key);
        return arguments.Select((value, i) => (object?)(format != null && i < format.Kinds.Length ? format.Kinds[i] switch
        {
            "message" => Describe(value),
            "stage" => Stage(value),
            _ => value
        } : value)).ToArray();
    }
    private bool TryFormatNotice(string original, out string translated)
    {
        translated = original;
        if (original.Length > 8192 || noticeDepth >= 8) return false;
        foreach (var format in noticeFormats)
        {
            if (format.Pattern == null) continue;
            Match match;
            try { match = format.Pattern.Match(original); }
            catch (RegexMatchTimeoutException) { continue; }
            if (!match.Success) continue;
            noticeDepth++;
            try
            {
                var arguments = format.Kinds.Select((kind, i) => {
                    string value = match.Groups["arg" + i].Value;
                    return (object)(kind switch {
                        "message" => Describe(value),
                        "stage" => strings.ContainsKey("stage." + value) ? Stage(value) : Translate(value),
                        _ => value // Account names, paths, item names and identifiers are never translated.
                    });
                }).ToArray();
                translated = Get(format.Key, arguments);
                return true;
            }
            finally { noticeDepth--; }
        }
        return false;
    }
    private sealed record NoticeFormat(string Key, string[] Kinds, Regex? Pattern);
}
