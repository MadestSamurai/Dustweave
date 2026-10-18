using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace BD2InfiniteGacha.Desktop;

internal sealed class CompletionNotifications(Window owner) : INotificationSink, IDisposable
{
    private HwndSource? source;
    private Window? popup;
    private NotifyIconData icon;
    private bool added, disposed;
    private const int Callback = 0x8000 + 71;

    public void Send(NotificationChannel channel, string title, string message)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        switch (channel)
        {
            case NotificationChannel.Windows: ShowWindows(title, message); break;
            case NotificationChannel.Sound:
                // Asynchronous system sound, independent from silent Windows and WPF notifications.
                if (!PlaySound("SystemAsterisk", IntPtr.Zero, 0x00010000 | 0x0001 | 0x0002))
                    throw new InvalidOperationException("无法播放系统提示音，请检查系统声音设置。");
                break;
            case NotificationChannel.Popup: ShowPopup(title, message); break;
        }
    }

    private void RestoreOwner()
    {
        if (owner.WindowState == WindowState.Minimized) owner.WindowState = WindowState.Normal;
        owner.Show(); owner.Activate();
    }

    private void ShowPopup(string title, string message)
    {
        popup?.Close();
        RestoreOwner();
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) };
        var button = new Button { Content = Ui.Text("查看结果"), IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 100 };
        var body = new StackPanel { Margin = new Thickness(22) };
        body.Children.Add(text); body.Children.Add(button);
        var current = new Window { Owner = owner, Title = title, Width = 420, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (Brush)owner.FindResource("Surface"), Content = body, ShowInTaskbar = false };
        button.Click += (_, _) => { current.Close(); RestoreOwner(); };
        current.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) current.Close(); };
        current.Closed += (_, _) => { if (popup == current) popup = null; };
        popup = current;
        // A modeless WPF window is deliberately silent and does not block the polling/stop dispatcher.
        current.Show(); current.Activate();
    }

    private void ShowWindows(string title, string message)
    {
        IntPtr handle = new WindowInteropHelper(owner).EnsureHandle();
        if (source == null) { source = HwndSource.FromHwnd(handle); source?.AddHook(WindowMessage); }
        RemoveIcon();
        icon = new NotifyIconData { Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = handle, Id = 1,
            Flags = 1 | 2 | 4, CallbackMessage = Callback, Icon = LoadIcon(IntPtr.Zero, new IntPtr(32516)),
            Tip = "BD2 Infinite Gacha", Info = "", Title = "" };
        if (!ShellNotifyIcon(0, ref icon)) throw new InvalidOperationException("Windows 通知未能发送，请检查系统通知设置。");
        added = true;
        icon.Flags = 0x10;
        icon.Info = message.Length > 255 ? message[..255] : message;
        icon.Title = title.Length > 63 ? title[..63] : title;
        // NIIF_INFO | NIIF_NOSOUND | NIIF_RESPECT_QUIET_TIME. Sound is a separate user option.
        // https://learn.microsoft.com/windows/win32/api/shellapi/ns-shellapi-notifyicondataw
        icon.InfoFlags = 1 | 0x10 | 0x80;
        if (!ShellNotifyIcon(1, ref icon)) { RemoveIcon(); throw new InvalidOperationException("Windows 通知未能发送，请检查系统通知设置。"); }
    }

    private IntPtr WindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == Callback)
        {
            int action = lParam.ToInt32();
            if (action is 0x405 or 0x202) { RestoreOwner(); RemoveIcon(); }
            else if (action is 0x403 or 0x404) RemoveIcon(); // dismissed or timed out
            handled = true;
        }
        return IntPtr.Zero;
    }

    internal Window? ActivePopup => popup;
    private void RemoveIcon() { if (added) { ShellNotifyIcon(2, ref icon); added = false; } }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; popup?.Close(); RemoveIcon(); source?.RemoveHook(WindowMessage); source = null;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, CallbackMessage; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PlaySound(string sound, IntPtr module, uint flags);
}
