namespace Dustweave;

public sealed record DailyToolPresentation(int SchemaVersion,string Language,string Appearance)
{
    public bool Valid => SchemaVersion == 1 && Language is "zh-CN" or "zh-TW" or "en-US" && Appearance is "system" or "light" or "dark";
}
