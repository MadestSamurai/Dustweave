using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Dustweave.Desktop;

// UI locale only. Never changes process culture, protocol values, account keys or receipts.
public sealed partial class DailyLanguage : INotifyPropertyChanged
{
    public static readonly string[] Codes = ["zh-CN", "zh-TW", "en-US"];
    public static DailyLanguage Current { get; } = new();
    private readonly Dictionary<string, Dictionary<string,string>> strings;
    private readonly Dictionary<string,string> sourceKeys;
    private string root = "";
    public string Code { get; private set; } = "zh-CN";
    public int Revision { get; private set; }
    public string this[string key] => Get(key);
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Changed;
    private DailyLanguage()
    {
        using var stream = typeof(DailyLanguage).Assembly.GetManifestResourceStream("Dustweave.UI.strings.json")
            ?? throw new InvalidDataException("UI language resources are missing.");
        strings = JsonSerializer.Deserialize<Dictionary<string,Dictionary<string,string>>>(stream)!;
        using var runtimeStream = typeof(DailyLanguage).Assembly.GetManifestResourceStream("Dustweave.UI.runtime.json")
            ?? throw new InvalidDataException("Runtime language resources are missing.");
        foreach (var pair in JsonSerializer.Deserialize<Dictionary<string,Dictionary<string,string>>>(runtimeStream)!)
            strings.Add(pair.Key, pair.Value);
        LoadNotices();
        sourceKeys = strings.Where(p => !p.Value["zh-CN"].Contains('{'))
            .GroupBy(p => p.Value["zh-CN"], StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);
    }
    public void Initialize(string dataRoot)
    {
        root = dataRoot;
        var saved = DailyJson.TryRead<LanguageSetting>(Path.Combine(root, "language.json"))?.Language;
        Apply(Codes.Contains(saved) ? saved! : "zh-CN");
    }
    public void RefreshFromDisk()
    {
        if(root.Length==0 || !File.Exists(Path.Combine(root,"language.json")))return;
        var saved=DailyJson.TryRead<LanguageSetting>(Path.Combine(root,"language.json"))?.Language;
        if(saved!=Code && Codes.Contains(saved))Apply(saved!);
    }
    public void Select(string code)
    {
        if (!Codes.Contains(code)) throw new ArgumentOutOfRangeException(nameof(code));
        DailyJson.Write(Path.Combine(root, "language.json"), new LanguageSetting(code));
        Apply(code);
    }
    private void Apply(string code)
    {
        Code = code; Revision++;
        PropertyChanged?.Invoke(this, new("Item[]"));
        PropertyChanged?.Invoke(this, new(nameof(Revision)));
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public string Get(string key, params object?[] args)
    {
        if (!strings.TryGetValue(key, out var entry)) throw new KeyNotFoundException("Missing UI text: " + key);
        var value = entry[Code];
        return args.Length == 0 ? value : string.Format(CultureInfo.GetCultureInfo(Code), value, args);
    }
    public string Translate(string original) => strings.ContainsKey(original) ? Get(original) : sourceKeys.TryGetValue(original, out var key) ? Get(key)
        : TryFormatNotice(original, out var translated) ? translated : TranslateToolMessage(original);
    public string Stage(string id) => id == "event_battle" && DailyPlugin.Current.Supports(id) ? Get("prefs.event_battle")
        : strings.ContainsKey("stage." + id) ? Get("stage." + id) : Get("stage.unknown", id);
    public string State(string code) => strings.ContainsKey("state." + code) ? Get("state." + code) : Translate(DailyUserText.State(code));
    public string Describe(string raw)
    {
        string known = Translate(raw);
        return known != raw ? known : DailyUserText.Describe(raw, Translate);
    }
    public string Diagnostic(string raw, string displayed) => displayed + "\n\n" + Get("diagnostics.original") + "\n" + raw;
    public void Bind(DependencyObject target, DependencyProperty property, string key, params object?[] args) => Bind(target, property, () => Get(key, args));
    public void Bind(DependencyObject target, DependencyProperty property, Func<string> value)
        => BindingOperations.SetBinding(target, property, new Binding(nameof(Revision)) { Source = this, Mode = BindingMode.OneWay, Converter = new TextConverter(value) });
    public void Text(System.Windows.Controls.TextBlock target, string key, params object?[] args) => Bind(target, System.Windows.Controls.TextBlock.TextProperty, key, args);
    public void Literal(System.Windows.Controls.TextBlock target, string source) => Bind(target, System.Windows.Controls.TextBlock.TextProperty, () => Translate(source));
    public void Audit()
    {
        foreach (var (key, translations) in strings)
        {
            if (!Codes.All(c => translations.TryGetValue(c, out var text) && !string.IsNullOrWhiteSpace(text)))
                throw new InvalidDataException("Incomplete translation: " + key);
            var baseline = CompositeFormat(translations["zh-CN"]);
            foreach (var value in translations.Values)
                if (!CompositeFormat(value).SequenceEqual(baseline)) throw new InvalidDataException("Translation arguments differ: " + key);
        }
    }
    private static int[] CompositeFormat(string text)
    {
        _ = System.Text.CompositeFormat.Parse(text);
        return System.Text.RegularExpressions.Regex.Matches(text, @"(?<!\{)\{(\d+)(?:[^}]*)\}")
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().Order().ToArray();
    }
    public int Count => strings.Count;
    public sealed record LanguageSetting(string Language);
    private sealed class TextConverter(Func<string> value) : IValueConverter
    {
        public object Convert(object input, Type type, object parameter, CultureInfo culture) => value();
        public object ConvertBack(object input, Type type, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding($"[{key}]") { Source = DailyLanguage.Current, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
