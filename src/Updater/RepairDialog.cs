using System.Runtime.InteropServices;
using Dustweave;

internal static class RepairDialog
{
    internal static string Text(string simplified, string traditional, string english)
    {
        string culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        return culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? culture is "zh-TW" or "zh-HK" or "zh-MO" || culture.Contains("Hant") ? traditional : simplified
            : english;
    }
    internal static bool Confirm(string text) => MessageBox(0, text, "Dustweave", 0x24 | 0x100) == 6;
    internal static void Notify(string text, bool error = false) => MessageBox(0, text, "Dustweave", error ? 0x10u : 0x40u);
    internal static T Run<T>(string title, Func<IProgress<DailyRepairProgress>, CancellationToken, Task<T>> work)
    {
        using var cancellation = new CancellationTokenSource();
        var reporter = new Reporter();
        var task = Task.Run(() => work(reporter, cancellation.Token));
        Callback callback = (window, notification, wParam, lParam, data) =>
        {
            if (notification == 2) // TDN_BUTTON_CLICKED
            {
                if (task.IsCompleted) return 0;
                cancellation.Cancel();
                SetText(window, Text("正在取消…", "正在取消…", "Cancelling…"));
                return 1; // Keep the window alive until pending IO has stopped.
            }
            if (notification == 4) // TDN_TIMER
            {
                if (task.IsCompleted) { SendMessage(window, 1024 + 102, 2, 0); return 0; }
                var progress = reporter.Value;
                if (progress != null && !cancellation.IsCancellationRequested)
                {
                    string phase = progress.Stage == "checking" ? Text("检查文件", "檢查檔案", "Checking files") : Text("准备修复", "準備修復", "Preparing repairs");
                    SetText(window, $"{phase} · {progress.Completed}/{progress.Total}");
                    SendMessage(window, 1024 + 106, progress.Total == 0 ? 0 : progress.Completed * 100 / progress.Total, 0);
                }
            }
            return 0;
        };
        var config = new Config
        {
            Size = (uint)Marshal.SizeOf<Config>(), Flags = 8 | 0x200 | 0x800 | 0x8000,
            CommonButtons = 8, Title = "Dustweave", Instruction = title,
            Content = Text("正在准备…", "正在準備…", "Preparing…"),
            Footer = Text("准备完成后自动继续；可取消并在下次打开时重试。", "準備完成後自動繼續；可取消並在下次開啟時重試。", "Continues automatically when ready. Cancel to retry next time."),
            Callback = callback, Width = 320
        };
        int result = TaskDialogIndirect(ref config, out _, out _, out _);
        GC.KeepAlive(callback);
        if (result < 0) { cancellation.Cancel(); try { task.GetAwaiter().GetResult(); } catch { } Marshal.ThrowExceptionForHR(result); }
        return task.GetAwaiter().GetResult();
    }
    private sealed class Reporter : IProgress<DailyRepairProgress>
    {
        internal DailyRepairProgress? Value;
        public void Report(DailyRepairProgress value) => Volatile.Write(ref Value, value);
    }
    private static void SetText(nint window, string value)
    {
        nint text = Marshal.StringToHGlobalUni(value);
        try { SendMessage(window, 1024 + 108, 0, text); }
        finally { Marshal.FreeHGlobal(text); }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Callback(nint window, uint notification, nint wParam, nint lParam, nint data);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct Config
    {
        internal uint Size;
        internal nint Parent, Instance;
        internal uint Flags, CommonButtons;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Title;
        internal nint MainIcon;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Instruction;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Content;
        internal uint ButtonCount;
        internal nint Buttons;
        internal int DefaultButton;
        internal uint RadioCount;
        internal nint Radios;
        internal int DefaultRadio;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Verification;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Expanded;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? ExpandedControl;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? CollapsedControl;
        internal nint FooterIcon;
        [MarshalAs(UnmanagedType.LPWStr)] internal string? Footer;
        internal Callback? Callback;
        internal nint CallbackData;
        internal uint Width;
    }
    [DllImport("comctl32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int TaskDialogIndirect(ref Config config, out int button, out int radio, [MarshalAs(UnmanagedType.Bool)] out bool verification);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint window, string text, string title, uint type);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}
