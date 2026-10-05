using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;

namespace BD2Daily.Desktop;

public enum DailyAppearance { System, Light, Dark }

// One shared, observable palette also reaches controls constructed in code.
// Bound brushes cannot be frozen by WPF's style cache; their identity never changes.
public sealed class DailyTheme : INotifyPropertyChanged, IDisposable
{
    private readonly string file;
    private readonly Dictionary<string, Color> colors = new();
    private static readonly Dictionary<string, (string Light, string Dark)> palette = new()
    {
        ["AppBackground"] = ("#F4F6F8", "#151C23"),
        ["Surface"] = ("#FFFFFF", "#1C252E"),
        ["SurfaceMuted"] = ("#EAF0F2", "#25313A"),
        ["Sidebar"] = ("#EDF1F3", "#172029"),
        ["Line"] = ("#DCE3E7", "#33414B"),
        ["LineStrong"] = ("#8B969F", "#707D89"),
        ["Ink"] = ("#27333B", "#F0F3F5"),
        ["MutedInk"] = ("#596975", "#A9B8C4"),
        ["Primary"] = ("#176B57", "#86D3B6"),
        ["PrimaryInk"] = ("#FFFFFF", "#102D23"),
        ["PrimaryHover"] = ("#115B49", "#A3E3CA"),
        ["PrimarySoft"] = ("#E5F1EB", "#253B36"),
        ["Focus"] = ("#266DAD", "#81BDF0"),
        ["Success"] = ("#19724F", "#8ED7B1"),
        ["Warning"] = ("#845700", "#EBC37A"),
        ["WarningSoft"] = ("#FFF5DF", "#40382B"),
        ["Error"] = ("#A53535", "#F29B9B"),
        ["ErrorSoft"] = ("#FCEDED", "#432D32"),
    };
    public DailyAppearance Preference { get; private set; }
    public bool IsDark { get; private set; }
    public Color this[string key] => colors.GetValueOrDefault(key, Colors.Transparent);
    public event PropertyChangedEventHandler? PropertyChanged;

    public DailyTheme(string root)
    {
        file = Path.Combine(root, "appearance.json");
        Preference = ReadPreference(root);
        Refresh();
        foreach (var key in palette.Keys)
        {
            var brush = new SolidColorBrush();
            BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty,
                new Binding($"[{key}]") { Source = this, Mode = BindingMode.OneWay });
            Application.Current.Resources[key] = brush;
        }
        SystemEvents.UserPreferenceChanged += SystemChanged;
        SystemParameters.StaticPropertyChanged += ParametersChanged;
    }

    public static DailyAppearance ReadPreference(string root)
    {
        var value = DailyJson.TryRead<AppearanceSetting>(Path.Combine(root, "appearance.json"))?.Mode;
        return Enum.TryParse<DailyAppearance>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed : DailyAppearance.System;
    }
    public void Select(DailyAppearance value)
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
        // Persist only the appearance; account and automation settings are untouched.
        DailyJson.Write(file, new AppearanceSetting(value.ToString()));
        Preference = value;
        Refresh();
    }
    private void SystemChanged(object sender, UserPreferenceChangedEventArgs args) => ScheduleRefresh();
    private void ParametersChanged(object? sender, PropertyChangedEventArgs args) => ScheduleRefresh();
    private void ScheduleRefresh()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted)
            dispatcher.BeginInvoke(Refresh);
    }
    private void Refresh()
    {
        bool systemDark = false;
        try { systemDark = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int n && n == 0; }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        IsDark = Preference == DailyAppearance.Dark || Preference == DailyAppearance.System && systemDark;
        foreach (var (key, value) in palette)
            colors[key] = (Color)ColorConverter.ConvertFromString(IsDark ? value.Dark : value.Light);
        if (SystemParameters.HighContrast)
        {
            foreach (var key in new[] { "AppBackground", "Surface", "SurfaceMuted", "Sidebar", "PrimarySoft", "WarningSoft", "ErrorSoft" })
                colors[key] = SystemColors.WindowColor;
            foreach (var key in new[] { "Ink", "MutedInk", "Line", "LineStrong", "Success", "Warning", "Error" })
                colors[key] = SystemColors.WindowTextColor;
            colors["Primary"] = colors["PrimaryHover"] = colors["Focus"] = SystemColors.HighlightColor;
            colors["PrimaryInk"] = SystemColors.HighlightTextColor;
        }
        PropertyChanged?.Invoke(this, new("Item[]"));
        PropertyChanged?.Invoke(this, new(nameof(IsDark)));
    }
    internal static void CheckContrastForSmoke()
    {
        static double Luminance(string hex) {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            double Channel(byte n) { double v = n / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        }
        foreach (bool dark in new[] { false, true })
        foreach (var pair in new[] { ("Ink","Surface"), ("MutedInk","Surface"), ("MutedInk","SurfaceMuted"), ("MutedInk","Sidebar"),
                     ("Ink","PrimarySoft"), ("PrimaryInk","Primary"), ("Error","ErrorSoft"), ("Warning","WarningSoft"), ("Success","Surface") })
        {
            var a = palette[pair.Item1]; var b = palette[pair.Item2];
            double l1 = Luminance(dark ? a.Dark : a.Light), l2 = Luminance(dark ? b.Dark : b.Light);
            double contrast = (Math.Max(l1,l2) + .05) / (Math.Min(l1,l2) + .05);
            if (contrast < 4.5) throw new Exception($"Insufficient text contrast: {pair}, dark={dark}: {contrast:F2}");
        }
    }
    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= SystemChanged;
        SystemParameters.StaticPropertyChanged -= ParametersChanged;
    }
    public sealed record AppearanceSetting(string Mode);
}
