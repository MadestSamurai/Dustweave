using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Dustweave.Desktop;

internal sealed class DesktopTray : IDisposable
{
    private readonly Window owner;
    private readonly Action exit;
    private HwndSource? source;
    private IconData icon;
    private bool added, ownsIcon;
    private Action? notificationClick;
    private const int Callback = 0x8000 + 171;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    public DesktopTray(Window owner, Action exit) { this.owner = owner; this.exit = exit; }
    private void EnsureIcon()
    {
        var handle = new WindowInteropHelper(owner).EnsureHandle();
        if (source == null)
        {
            source = HwndSource.FromHwnd(handle); source.AddHook(Message);
            var small = new IntPtr[1]; ExtractIconEx(Environment.ProcessPath!, 0, null, small, 1);
            ownsIcon = small[0] != IntPtr.Zero;
            icon = new() { Size = (uint)Marshal.SizeOf<IconData>(), Window = handle, Id = 171, Flags = 1 | 2 | 4,
                CallbackMessage = Callback, Icon = ownsIcon ? small[0] : LoadIcon(IntPtr.Zero, new(32512)), Tip = "Dustweave", Info = "", Title = "" };
        }
        if (!added && !ShellNotifyIcon(0, ref icon)) throw new InvalidOperationException("desktop.tray_failed");
        added = true;
    }
    public void Hide() { EnsureIcon(); owner.Hide(); }
    public void Notify(string title, string message, Action clicked)
    {
        EnsureIcon(); notificationClick = clicked;
        var notification = icon; notification.Flags = 0x10;
        notification.Title = title.Length > 63 ? title[..63] : title;
        notification.Info = message.Length > 255 ? message[..255] : message;
        notification.InfoFlags = 1 | 0x10 | 0x80; // Silent, respect quiet hours; sound is a separate choice.
        if (!ShellNotifyIcon(1, ref notification)) throw new InvalidOperationException("completion.notification_failed");
    }
    public void Restore()
    {
        owner.Show(); if (owner.WindowState == WindowState.Minimized) owner.WindowState = WindowState.Normal;
        owner.Activate(); if (added) ShellNotifyIcon(2, ref icon); added = false;
    }
    private IntPtr Message(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == taskbarCreated && added)
        {
            if (!ShellNotifyIcon(0, ref icon)) { added = false; owner.Dispatcher.BeginInvoke(Restore); }
        }
        if (message == Callback)
        {
            handled = true;
            if (lParam.ToInt32() == 0x405) { Restore(); notificationClick?.Invoke(); }
            else if (lParam.ToInt32() is 0x403 or 0x404) { if (owner.IsVisible && added) { ShellNotifyIcon(2, ref icon); added = false; } }
            else if (lParam.ToInt32() is 0x202 or 0x203) Restore();
            else if (lParam.ToInt32() == 0x205)
            {
                SetForegroundWindow(window);
                var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
                var show = new MenuItem { Header = DailyLanguage.Current.Get("desktop.show") };
                var quit = new MenuItem { Header = DailyLanguage.Current.Get("desktop.exit") };
                show.Click += (_, _) => Restore(); quit.Click += (_, _) => { Restore(); exit(); };
                menu.Items.Add(show); menu.Items.Add(quit); menu.IsOpen = true;
            }
        }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (added) ShellNotifyIcon(2, ref icon); added = false;
        source?.RemoveHook(Message); source = null;
        if (ownsIcon) DestroyIcon(icon.Icon); ownsIcon = false;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IconData
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
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint message, ref IconData data);
    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[] small, uint count);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string text);
}
