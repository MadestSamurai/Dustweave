using System.Text.RegularExpressions;

namespace Dustweave;

/// <summary>Canonical release identity and SemVer precedence; metadata is excluded from package names.</summary>
public readonly record struct DailyVersion : IComparable<DailyVersion>
{
    private readonly Version core;
    private readonly string label;
    private DailyVersion(Version core, string label) { this.core = core; this.label = label; }
    public static DailyVersion Parse(string value)
    {
        if (value is null || value.Length > 100) throw new InvalidDataException("updates.invalid_feed");
        var match = Regex.Match(value, @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z", RegexOptions.CultureInvariant);
        if (!match.Success || !Version.TryParse(string.Join(".", match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value), out var core))
            throw new InvalidDataException("updates.invalid_feed");
        string label = match.Groups[4].Value;
        if (label.Split('.').Any(s => s.Length > 1 && s[0] == '0' && s.All(char.IsAsciiDigit)))
            throw new InvalidDataException("updates.invalid_feed");
        return new(core, label);
    }
    public int CompareTo(DailyVersion other)
    {
        int comparison = core.CompareTo(other.core);
        if (comparison != 0) return comparison;
        if (label.Length == 0 || other.label.Length == 0)
            return label.Length == other.label.Length ? 0 : label.Length == 0 ? 1 : -1;
        string[] left = label.Split('.'), right = other.label.Split('.');
        for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            bool ln = left[i].All(char.IsAsciiDigit), rn = right[i].All(char.IsAsciiDigit);
            comparison = ln && rn ? left[i].Length.CompareTo(right[i].Length) : ln != rn ? (ln ? -1 : 1) : 0;
            if (comparison == 0) comparison = string.CompareOrdinal(left[i], right[i]);
            if (comparison != 0) return comparison;
        }
        return left.Length.CompareTo(right.Length);
    }
    public override string ToString() => core + (label.Length == 0 ? "" : "-" + label);
    public static bool operator <(DailyVersion a, DailyVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(DailyVersion a, DailyVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(DailyVersion a, DailyVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(DailyVersion a, DailyVersion b) => a.CompareTo(b) >= 0;
}
