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
    private readonly LocalizedTextCatalog catalog;
    private string root = "";
    public string Code { get; private set; } = "zh-CN";
    public int Revision { get; private set; }
    public string this[string key] => Get(key);
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? Changed;
    private DailyLanguage()
    {
        catalog = new(name => typeof(DailyLanguage).Assembly.GetManifestResourceStream("Dustweave.UI." + name + ".json")
            ?? throw new InvalidDataException("UI language resource is missing: " + name));
        strings = catalog.Entries;
        sourceKeys = catalog.LegacyInputs;
        LoadNotices();
        // Keep source matchers intact, then prepare each display template once.
        foreach (var entry in strings.Values)
            foreach (var code in Codes)
                if (entry.TryGetValue(code, out var template)) entry[code]=GameTerminology.Template(template,code);
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
        => catalog.Get(key, Code, args);
    internal string SearchText(string key) => catalog.SearchText(key);
    internal string MessageKey(string code)
    {
        string key = "message." + code;
        return strings.ContainsKey(key) ? key : sourceKeys.GetValueOrDefault(key, key);
    }
    public string Describe(DailyText text) => !string.IsNullOrEmpty(text.Key) && strings.ContainsKey(text.Key)
        ? Get(text.Key, NoticeArguments(text.Key, text.Arguments ?? [])) : Describe(text.Fallback ?? "");
    // Legacy runtime/evidence input only. New UI bindings must use Get/Text/Bind(key).
    public string Translate(string original) => strings.ContainsKey(original) ? Get(original) : sourceKeys.TryGetValue(original, out var key) ? Get(key)
        : TryFormatNotice(original, out var translated) ? translated : TranslateToolMessage(original);
    public string Stage(string id) => id == "event_battle" && DailyPlugin.Current.Supports(id) ? Get("prefs.event_battle")
        : strings.ContainsKey("stage." + id) ? Get("stage." + id) : Get("stage.unknown", id);
    public string WorkStep(string? step)
    {
        string key = "work." + step;
        if (strings.ContainsKey(key)) return Get(key);
        // Older progress records may contain the display text instead of its ID.
        // Unknown presentation metadata must not interrupt queue rendering.
        if (step != null && sourceKeys.TryGetValue(step, out var legacyKey) && legacyKey.StartsWith("work.", StringComparison.Ordinal))
            return Get(legacyKey);
        return Get("work.execute");
    }
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
        => catalog.Audit();
    internal DailyLocalizationDiagnostics Diagnostics() => catalog.Diagnostics(Code);
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
