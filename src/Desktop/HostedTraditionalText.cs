using System.Runtime.InteropServices;

namespace Dustweave.Desktop;

// Windows-only presentation conversion. Never used for IDs, payloads or persisted settings.
internal sealed class HostedTraditionalText
{
    private readonly Dictionary<string,string> rendered=new(StringComparer.Ordinal), sources=new(StringComparer.Ordinal);
    public string Traditional(string text)
    {
        if(string.IsNullOrEmpty(text))return text;
        if(rendered.TryGetValue(text,out var found))return found;
        string value=GameTerminology.Template(Wording(Convert(text,0x04000000)),"zh-TW");
        if(rendered.Count>=4096){rendered.Clear();sources.Clear();}
        rendered[text]=value;sources.TryAdd(value,text);return value;
    }
    // Windows maps characters one by one. Fix the compounds it leaves ambiguous and use Taiwan interface wording.
    // Applied before game terminology, so official names (including 搜索) are inserted afterwards unchanged.
    private static readonly (string From,string To)[] Words=
    [
        ("游戲","遊戲"),("裏","裡"),("麽","麼"),("游玩","遊玩"),("旅游","旅遊"),("合并","合併"),("并","並"),("于","於"),("戰斗","戰鬥"),("決斗","決鬥"),("格斗","格鬥"),("打斗","打鬥"),("奮斗","奮鬥"),("斗爭","鬥爭"),
        ("爭斗","爭鬥"),("剩余","剩餘"),("其余","其餘"),("多余","多餘"),("余額","餘額"),("余量","餘量"),("余下","餘下"),("余數","餘數"),("結余","結餘"),("殘余","殘餘"),("每周","每週"),
        ("本周","本週"),("上周","上週"),("下周","下週"),("周期","週期"),("周末","週末"),("周年","週年"),("周一","週一"),("周二","週二"),("周三","週三"),("周四","週四"),("周五","週五"),
        ("周六","週六"),("周日","週日"),("這里","這裡"),("那里","那裡"),("哪里","哪裡"),("里面","裡面"),("里頭","裡頭"),("心里","心裡"),("什么","什麼"),("怎么","怎麼"),("那么","那麼"),
        ("這么","這麼"),("多么","多麼"),("要么","要麼"),("占領","佔領"),("占用","佔用"),("占據","佔據"),("占比","佔比"),("占位","佔位"),("制作","製作"),("制造","製造"),("繪制","繪製"),
        ("定制","訂製"),("自制","自製"),("錄制","錄製"),("制品","製品"),("制成","製成"),("研制","研製"),("復制","複製"),("重復","重複"),("復雜","複雜"),("復選","複選"),("復數","複數"),
        ("反復","反覆"),("答復","答覆"),("准備","準備"),("標准","標準"),("准確","準確"),("准時","準時"),("瞄准","瞄準"),("校准","校準"),("精准","精準"),("對准","對準"),("水准","水準"),
        ("准則","準則"),("沖突","衝突"),("冲突","衝突"),("沖刺","衝刺"),("冲刺","衝刺"),("緩沖","緩衝"),("沖擊","衝擊"),("沖鋒","衝鋒"),("關系","關係"),("聯系","聯繫"),("標簽","標籤"),
        ("日志","日誌"),("托管","託管"),("委托","委託"),("日歷","日曆"),("日历","日曆"),("历史","歷史"),("干淨","乾淨"),("干燥","乾燥"),("盡量","儘量"),("尽量","儘量"),("盡管","儘管"),("着","著"),
        ("賬戶","帳戶"),("賬","帳"),("登錄","登入"),("注銷","登出"),("設置","設定"),("當前","目前"),("運行","執行"),("信息","資訊"),("加載","載入"),("保存","儲存"),("數據","資料"),("組件","元件"),
        ("服務器","伺服器"),("隊列","佇列"),("默認","預設"),("鼠標","滑鼠"),("光標","游標"),("窗口","視窗"),("屏幕","螢幕"),("全屏","全螢幕"),("菜單","選單"),("網絡","網路"),("支持","支援"),
        ("文件夾","資料夾"),("文件","檔案"),("軟件","軟體"),("硬件","硬體"),("用戶","使用者"),("激活","啟用"),("視頻","影片"),("音頻","音訊"),("質量","品質"),("校驗","驗證"),("會話","工作階段"),
        ("線程","執行緒"),("緩存","快取"),("內存","記憶體"),("圖標","圖示"),("導出","匯出"),("導入","匯入"),("搜索","搜尋"),("獲取","取得"),("創建","建立"),("實時","即時"),("鏈接","連結"),
        ("模塊","模組"),("兼容","相容"),("調試","偵錯"),("接口","介面"),("插件","外掛"),("概率","機率"),("信號","訊號"),("在線","線上")
    ];
    private static readonly System.Text.RegularExpressions.Regex After=new("(?<![皇王天太影])后|(?<![風神文光豐])采",System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    internal static string Wording(string text)
    {
        foreach(var (from,to) in Words)if(text.Contains(from,StringComparison.Ordinal))text=text.Replace(from,to,StringComparison.Ordinal);
        return After.Replace(text,m=>m.Value=="后"?"後":"採");
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


