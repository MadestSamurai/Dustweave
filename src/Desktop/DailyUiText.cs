using System.Windows.Controls;

namespace Dustweave.Desktop;

internal static class DailyUiText
{
    public static void Set(TextBlock target, string raw)
    {
        DailyLanguage.Current.Bind(target, TextBlock.TextProperty, () => DailyLanguage.Current.Describe(raw));
        DailyLanguage.Current.Bind(target, System.Windows.FrameworkElement.ToolTipProperty, () => DailyLanguage.Current.Diagnostic(raw, target.Text));
    }
    public static void History(TextBlock target, string key, DateTime localTime, string raw)
    {
        DailyLanguage.Current.Bind(target, TextBlock.TextProperty,
            () => DailyLanguage.Current.Get(key, localTime, DailyLanguage.Current.Describe(raw)));
        DailyLanguage.Current.Bind(target, System.Windows.FrameworkElement.ToolTipProperty,
            () => DailyLanguage.Current.Diagnostic(raw, target.Text));
    }
    public static void Error(TextBlock target, Exception error, string prefix = "")
    {
        DailyLanguage.Current.Bind(target, TextBlock.TextProperty, () => DailyLanguage.Current.Translate(prefix) + DailyUserText.Error(error, DailyLanguage.Current.Translate));
        DailyLanguage.Current.Bind(target, System.Windows.FrameworkElement.ToolTipProperty, () => DailyLanguage.Current.Diagnostic(error.ToString(), target.Text));
    }
}
