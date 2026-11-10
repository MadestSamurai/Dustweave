using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Dustweave;
using Dustweave.Bootstrap;

namespace Dustweave.RuntimeNotice;

internal static class Program
{
    internal const string Version = "1.6.10";
    internal const string Download = "https://github.com/MadestSamurai/Dustweave/releases/tag/v1.7.0";
    internal const string Mirror = "https://pan.quark.cn/s/0ee553c60c0d?pwd=5AgC";
    internal static string DataRoot => Environment.GetEnvironmentVariable("BD2_DAILY_DATA_ROOT") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BD2DailyAssistant");
    internal static string Text(string cn, string tw, string en)
    {
        string language = System.Globalization.CultureInfo.CurrentUICulture.Name;
        // Respect the application's language without initializing accounts, game
        // components, migrations or background services in this retirement build.
        try
        {
            string path = Path.Combine(DataRoot, "language.json");
            if (File.Exists(path) && new FileInfo(path).Length < 16384)
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                if (document.RootElement.TryGetProperty("Language", out var code)) language = code.GetString() ?? language;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? language is "zh-TW" or "zh-HK" or "zh-MO" || language.Contains("Hant") ? tw : cn : en;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args is ["--self-test"]) { NoticeTransaction.SelfTest(); return 0; }
            if (Recovery.TryRun(args, Environment.ProcessPath!, DataRoot)) return 0;
            // Keep the established single-instance boundary, including during an
            // actual old-helper upgrade. This build never resumes automation.
            using var mutex = new Mutex(false, DailyApplication.InstanceMutex);
            bool owned; try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
            if (!owned) return 0;
            try { Show(args); } finally { mutex.ReleaseMutex(); }
            return 0;
        }
        catch (Exception error)
        {
            try
            {
                Directory.CreateDirectory(DataRoot);
                File.WriteAllText(Path.Combine(DataRoot, "runtime-notice-error.txt"), DateTimeOffset.UtcNow + "\n" + error);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            MessageBox(0, Text("升级提示未能完成。请从官方页面手动下载 1.7.0；账号与设置未被删除。\n", "升級提示未能完成。請從官方頁面手動下載 1.7.0；帳號與設定未被刪除。\n", "The upgrade notice could not complete. Download 1.7.0 from the official page; accounts and settings have not been deleted.\n") + Download, "Dustweave", 0x10);
            return 1;
        }
    }
    private static void Show(string[] args)
    {
        Exception? failure = null;
        Callback callback = (window, notification, wParam, lParam, data) =>
        {
            if (notification == 0)
            {
                try
                {
                    if (args is ["--updated", var nonce]) NoticeTransaction.Acknowledge(nonce, DataRoot, Environment.ProcessPath!);
                    Directory.CreateDirectory(DataRoot);
                    using var stream = File.Create(Path.Combine(DataRoot, "startup-state.json"));
                    using var writer = new Utf8JsonWriter(stream);
                    writer.WriteStartObject(); writer.WriteString("stage", "bridge-window-visible");
                    writer.WriteString("version", Version); writer.WriteNumber("pid", Environment.ProcessId); writer.WriteEndObject();
                }
                catch (Exception error) { failure = error; PostMessage(window, 0x10, 0, 0); }
            }
            if (notification == 3)
            {
                string? url = Marshal.PtrToStringUni(lParam);
                if (url is Download or Mirror)
                {
                    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                    catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
                    { MessageBox(window, Text("无法打开浏览器，请复制此地址：\n", "無法開啟瀏覽器，請複製此網址：\n", "Could not open the browser. Use this address:\n") + url, "Dustweave", 0x40); }
                }
            }
            return 0;
        };
        var config = new Config
        {
            Size = (uint)Marshal.SizeOf<Config>(), Flags = 1 | 8 | 0x8000, CommonButtons = 0x20,
            Title = "Dustweave 1.6.10", MainIcon = new nint(ushort.MaxValue), Width = 380,
            Instruction = Text("请手动更新到 1.7.0", "請手動更新至 1.7.0", "Download Dustweave 1.7.0 manually"),
            Content = Text(
                "1.6.9 及之前版本可能使用存在 CVE-2026-111497 漏洞的旧 .NET 运行库。为避免大体积自动下载，1.6.10 仅提供升级提醒。\n\n请下载并解压完整的 1.7.0，再运行其中的 Dustweave.exe。账号、设置和插件仍保留在原位置。请不要删除用户数据，也不要在游戏中退出账号。\n\n推荐 Portable：已内置修复后的运行库。Lite 用户还需自行更新 .NET 8 桌面运行时。",
                "1.6.9 及之前版本可能使用存在 CVE-2026-111497 漏洞的舊 .NET 執行階段。為避免大容量自動下載，1.6.10 僅提供升級提醒。\n\n請下載並解壓縮完整的 1.7.0，再執行其中的 Dustweave.exe。帳號、設定與外掛仍保留在原位置。請勿刪除使用者資料，也不要在遊戲中登出帳號。\n\n建議 Portable：已內建修復後的執行階段。Lite 使用者還需自行更新 .NET 8 桌面執行階段。",
                "Versions up to 1.6.9 may use a .NET runtime affected by CVE-2026-111497. To avoid a large automatic download, 1.6.10 only provides this upgrade notice.\n\nDownload and extract the complete 1.7.0 package, then run its Dustweave.exe. Your accounts, settings and plugins remain in their existing locations. Do not delete user data or sign out of the game.\n\nPortable includes the patched runtime and is recommended. Lite users must also update their installed .NET 8 Desktop Runtime.") +
                $"\n\n<a href=\"{Download}\">GitHub · 1.7.0</a>     <a href=\"{Mirror}\">" + Text("夸克下载（提取码 5AgC）", "夸克下載（提取碼 5AgC）", "Quark download (code: 5AgC)") + "</a>",
            // Official distribution notice: preserve the free/refund wording.
            // It is informational and must never gate access to user data.
            Footer = Text("织尘是免费软件。如果你付费购买了本软件，请勿确认收货，并向商家申请退款。", "織塵是免費軟體。如果你付費購買了本軟體，請勿確認收貨，並向商家申請退款。", "Dustweave is free software. If you paid a seller for it, do not confirm receipt; request a refund."),
            Callback = callback
        };
        int result = TaskDialogIndirect(ref config, out _, out _, out _);
        GC.KeepAlive(callback);
        if (failure != null) throw new IOException("Notice startup acknowledgement failed.", failure);
        Marshal.ThrowExceptionForHR(result);
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Callback(nint window, uint notification, nint wParam, nint lParam, nint data);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct Config
    {
        internal uint Size; internal nint Parent, Instance; internal uint Flags, CommonButtons;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Title; internal nint MainIcon;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Instruction;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Content;
        internal uint ButtonCount; internal nint Buttons; internal int DefaultButton;
        internal uint RadioCount; internal nint Radios; internal int DefaultRadio;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Verification;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Expanded;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? ExpandedControl;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? CollapsedControl;
        internal nint FooterIcon;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Footer;
        internal Callback? Callback; internal nint CallbackData; internal uint Width;
    }
    [DllImport("comctl32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int TaskDialogIndirect(ref Config config, out int button, out int radio, [MarshalAs(UnmanagedType.Bool)] out bool verification);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBox(nint window, string text, string title, uint type);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
