using System.Runtime.InteropServices;

namespace BD2Daily.Desktop;

// Windows-only presentation conversion. Never used for IDs, payloads or persisted settings.
internal sealed class HostedTraditionalText
{
    private readonly Dictionary<string,string> rendered=new(StringComparer.Ordinal), sources=new(StringComparer.Ordinal);
    public string Traditional(string text)
    {
        if(string.IsNullOrEmpty(text))return text;
        if(rendered.TryGetValue(text,out var found))return found;
        string value=Convert(text,0x04000000).Replace("游戲","遊戲",StringComparison.Ordinal);
        if(rendered.Count>=4096){rendered.Clear();sources.Clear();}
        rendered[text]=value;sources.TryAdd(value,text);return value;
    }
    public string Source(string text)=>sources.TryGetValue(text,out var found)?found:Convert(text,0x02000000);
    private static string Convert(string text,uint flags)
    {
        if(text.Length==0)return text;
        int size=LCMapStringEx("zh-CN",flags,text,text.Length,null,0,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        if(size==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var value=new char[size];int written=LCMapStringEx("zh-CN",flags,text,text.Length,value,value.Length,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        if(written==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return new string(value,0,written);
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern int LCMapStringEx(string locale,uint flags,string source,int sourceLength,[Out] char[]? destination,int destinationLength,IntPtr version,IntPtr reserved,IntPtr sortHandle);
}


