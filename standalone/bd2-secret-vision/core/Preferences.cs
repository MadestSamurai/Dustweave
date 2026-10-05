namespace BD2SecretVision;
public sealed class UserPreferences
{
    public string Language { get; set; } = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh-CN" : "en-US";
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayLanguage => AppDomain.CurrentDomain.GetData("BD2Daily.HostedLanguage") is string hosted && hosted is "zh-CN" or "zh-TW" or "en-US" ? hosted : Language;
    public void SelectLanguage(string root, string language)
    {

        if(AppDomain.CurrentDomain.GetData("BD2Daily.HostedLanguage") is string hosted && hosted is "zh-CN" or "zh-TW" or "en-US")
        {return;}
        if(language is not ("zh-CN" or "en-US"))throw new ArgumentOutOfRangeException(nameof(language));
        Language=language;Save(root);
    }
    public Settings Settings { get; set; } = new();
    public static UserPreferences Load(string root)
    {
        var path=Path.Combine(root,"settings.json");
        if(!File.Exists(path))return new();
        var value=Files.Read<UserPreferences>(path)??throw new InvalidDataException("settings-unreadable");
        value.Settings.Validate();
        if(value.Language is not ("zh-CN" or "en-US"))value.Language="en-US";
        return value;
    }
    public void Save(string root){Settings.Validate();Files.Write(Path.Combine(root,"settings.json"),this);}
}
