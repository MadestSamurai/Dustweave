using System.Windows.Controls;

namespace Dustweave.Desktop;

internal static class DailyUiText
{
    public static void Set(TextBlock target, DailyText text)
    {
        DailyLanguage.Current.Bind(target, TextBlock.TextProperty, () => DailyLanguage.Current.Describe(text));
        DailyLanguage.Current.Bind(target, System.Windows.FrameworkElement.ToolTipProperty,
            () => DailyLanguage.Current.Diagnostic(text.Fallback, target.Text));
    }
    public static void Set(TextBlock target, string raw)
    {
        DailyLanguage.Current.Bind(target, TextBlock.TextProperty, () => DailyLanguage.Current.Describe(raw));
        DailyLanguage.Current.Bind(target, System.Windows.FrameworkElement.ToolTipProperty, () => DailyLanguage.Current.Diagnostic(raw, target.Text));
    }
    public static void History(TextBlock target, string key, DateTime localTime, string raw, DailyText? display = null)
    {
        DailyLanguage.Current.Bind(target, TextBlock.TextProperty,
            () => DailyLanguage.Current.Get(key, localTime, display == null ? DailyLanguage.Current.Describe(raw) : DailyLanguage.Current.Describe(display)));
        DailyLanguage.Current.Bind(target, System.Windows.FrameworkElement.ToolTipProperty,
            () => DailyLanguage.Current.Diagnostic(raw, target.Text));
    }
    public static void Error(TextBlock target, Exception error, string prefix = "")
    {
        DailyLanguage.Current.Bind(target, TextBlock.TextProperty, () => DailyLanguage.Current.Translate(prefix) + DailyIssuePresentation.Describe(error));
        DailyLanguage.Current.Bind(target, System.Windows.FrameworkElement.ToolTipProperty, () => DailyLanguage.Current.Diagnostic(error.ToString(), target.Text));
    }
}
