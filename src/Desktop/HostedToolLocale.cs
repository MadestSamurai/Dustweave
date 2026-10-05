namespace BD2Daily.Desktop;

// Optional presentation-only contract. It is process-local and cannot alter connection or task settings.
internal static class HostedToolLocale
{
    internal const string Key = "BD2Daily.HostedLanguage";
    internal static string Supported(string language) => DailyLanguage.Codes.Contains(language) ? language : throw new ArgumentOutOfRangeException(nameof(language));
    internal static IDisposable Begin(string language)
    {
        var keys=new[]{Key,"BD2Daily.TraditionalText","BD2Daily.SourceText"};
        var original=keys.Select(k=>AppDomain.CurrentDomain.GetData(k)).ToArray();
        var converter=new HostedTraditionalText();
        AppDomain.CurrentDomain.SetData(Key,Supported(language));
        AppDomain.CurrentDomain.SetData(keys[1],(Func<string,string>)converter.Traditional);
        AppDomain.CurrentDomain.SetData(keys[2],(Func<string,string>)converter.Source);
        return new Scope(keys,original);
    }
    private sealed class Scope(string[] keys,object?[] original) : IDisposable
    {
        private bool disposed;
        public void Dispose() { if (!disposed) { disposed=true; for(int i=0;i<keys.Length;i++)AppDomain.CurrentDomain.SetData(keys[i],original[i]); } }
    }
}
