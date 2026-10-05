using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace BD2Daily.Desktop;

public partial class MainWindow
{
    // Keep the native resize frame and caption behavior, drawing only our own controls.
    private void InitializeWindowChrome()
    {
        SourceInitialized += (_, _) => UpdateWindowInsets();
        StateChanged += (_, _) => UpdateWindowInsets();
        SizeChanged += (_, _) => UpdateWindowInsets();
    }

    private void UpdateWindowInsets()
    {
        if (WindowState != WindowState.Maximized) { WindowRoot.Margin = new Thickness(0); return; }
        var handle = new WindowInteropHelper(this).Handle;
        var info = new ChromeMonitorInfo { Size = Marshal.SizeOf<ChromeMonitorInfo>() };
        if (handle == 0 || !ChromeGetMonitorInfo(ChromeMonitorFromWindow(handle, 2), ref info) ||
            !ChromeGetClientRect(handle, out var client)) return;
        var origin = new ChromePoint();
        if (!ChromeClientToScreen(handle, ref origin)) return;
        // A maximized native frame extends a few physical pixels outside its monitor.
        // Inset the content, not the HWND; Windows retains snap and taskbar behavior.
        var dpi = VisualTreeHelper.GetDpi(this);
        WindowRoot.Margin = new Thickness(
            Math.Max(0, info.Work.Left - origin.X) / dpi.DpiScaleX,
            Math.Max(0, info.Work.Top - origin.Y) / dpi.DpiScaleY,
            Math.Max(0, origin.X + client.Right - info.Work.Right) / dpi.DpiScaleX,
            Math.Max(0, origin.Y + client.Bottom - info.Work.Bottom) / dpi.DpiScaleY);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    // Preserve the existing stop/save/cleanup path instead of terminating the process.
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    [StructLayout(LayoutKind.Sequential)]
    private struct ChromePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ChromeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ChromeMonitorInfo { public int Size; public ChromeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", EntryPoint = "MonitorFromWindow")]
    private static extern nint ChromeMonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChromeGetMonitorInfo(nint monitor, ref ChromeMonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "GetClientRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChromeGetClientRect(nint window, out ChromeRect rect);
    [DllImport("user32.dll", EntryPoint = "ClientToScreen")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChromeClientToScreen(nint window, ref ChromePoint point);
}
