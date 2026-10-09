namespace Dustweave.Desktop;
internal static class DailyIssuePresentation
{
    private static DailyLanguage L => DailyLanguage.Current;
    public static string Describe(Exception error)
    {
        var issue = DailyIssues.Classify(error);
        string reason = issue.Code == "unknown" ? DailyUserText.Error(error, L.Translate) : L.Get("issue.reason." + issue.Code);
        return reason + "\n" + L.Get("issue.action." + issue.Code);
    }
    public static string WithAdvice(string raw, string translated)
    {
        var issue = DailyIssues.Classify(raw);
        return (issue.Code == "unknown" ? translated : L.Get("issue.reason." + issue.Code)) + "\n" + L.Get("issue.action." + issue.Code);
    }
}