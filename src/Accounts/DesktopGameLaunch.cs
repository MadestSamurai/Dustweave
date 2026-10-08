using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Dustweave.Accounts;

// Ask the existing, unelevated desktop to perform the launch. Creating a new
// Shell.Application in an elevated process is not sufficient to drop privileges.
// Windows shell route: https://devblogs.microsoft.com/oldnewthing/20131118-00/?p=2643
internal static class DesktopGameLaunch
{
    internal sealed record TokenState(bool Elevated, string User, int Session);
    internal sealed record Context(int ShellProcessId, TokenState Caller, TokenState Shell);

    internal static string? Reject(TokenState caller, TokenState shell) =>
        caller.User != shell.User ? "账号工具与 Windows 桌面不属于同一用户，请以当前桌面用户打开工具后重试。" :
        caller.Session != shell.Session ? "账号工具与 Windows 桌面不在同一登录会话，无法确认启动身份。" :
        shell.Elevated ? "Windows 桌面本身正以管理员权限运行，无法从它启动普通权限游戏。请恢复普通权限桌面后重试。" : null;

    internal static Context ValidateContext()
    {
        if (SandboxProcessScope.CurrentBox.Length > 0) { var self = ReadToken(Environment.ProcessId); return new(Environment.ProcessId, self, self); }
        nint window = GetShellWindow();
        if (window == 0 || GetWindowThreadProcessId(window, out uint shellPid) == 0)
            throw new SessionManagerException("没有找到 Windows 桌面，请先启动资源管理器后重试。");
        var context = new Context(checked((int)shellPid), ReadToken(Environment.ProcessId), ReadToken((int)shellPid));
        string? error = Reject(context.Caller, context.Shell);
        if (error != null) throw new SessionManagerException(error);
        return context;
    }

    internal static TokenState ReadToken(int pid)
    {
        using var process = OpenProcess(0x1000, false, pid); // QUERY_LIMITED_INFORMATION
        if (process.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取启动进程权限");
        if (!OpenProcessToken(process, 8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        {
            if (!GetTokenInformation(token, 20, out int elevated, sizeof(int), out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            using var identity = new WindowsIdentity(token.DangerousGetHandle());
            using var managed = Process.GetProcessById(pid);
            return new(elevated != 0, identity.User?.Value ?? throw new InvalidOperationException("无法确认 Windows 用户"), managed.SessionId);
        }
    }

    // A user may already have configured this game to run elevated. Detect it with
    // limited-query access; let the caller elevate only its connection helper.
    internal static string ReadImagePath(int pid)
    {
        using var process = OpenProcess(0x1000, false, pid);
        if (process.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var path = new System.Text.StringBuilder(32768); int length = path.Capacity;
        if (!QueryFullProcessImageName(process, 0, path, ref length)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return path.ToString();
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, System.Text.StringBuilder path, ref int length);
    internal static int Launch(string executable, string arguments = "", string? workingDirectory = null, bool hidden = false)
    {
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable)) throw new SessionManagerException("未找到要启动的程序。");
        workingDirectory ??= Path.GetDirectoryName(executable)!;
        if (SandboxProcessScope.CurrentBox.Length > 0)
        {
            var caller = ReadToken(Environment.ProcessId);
            using var child = Process.Start(new ProcessStartInfo(executable, arguments)
            { UseShellExecute = false, WorkingDirectory = workingDirectory, WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal, CreateNoWindow = hidden })
                ?? throw new SessionManagerException("隔离实例未启动。");
            var token = ReadToken(child.Id);
            if (!SandboxProcessScope.Contains(child.Id) || token.User != caller.User || token.Session != caller.Session)
                throw new SessionManagerException("启动后的进程不属于当前隔离实例，已停止后续操作。");
            return child.Id;
        }
        // COM desktop interfaces are apartment-bound. Keep lookup, dispatch and release on one STA.
        return InSta(() => LaunchInSta(executable, arguments, workingDirectory, hidden));
    }

    private static int LaunchInSta(string executable, string arguments, string workingDirectory, bool hidden)
    {
        Context context = ValidateContext();
        string name = Path.GetFileNameWithoutExtension(executable);
        var existing = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName(name))
            using (process) existing.Add(process.Id);
        object? windows = null, desktop = null, browserObject = null, viewObject = null, folderObject = null, application = null;
        try
        {
            windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), true)!)!;
            object location = 0, root = 0;
            desktop = ((IShellWindows)windows).FindWindowSW(ref location, ref root, 8, out _, 1);
            Guid service = new("4C96BE40-915C-11CF-99D3-00AA004AE837"), browserId = typeof(IShellBrowser).GUID;
            browserObject = ((IComServiceProvider)desktop).QueryService(ref service, ref browserId);
            var browser = (IShellBrowser)browserObject;
            browser.GetWindow(out nint desktopWindow);
            GetWindowThreadProcessId(desktopWindow, out uint actualPid);
            if (actualPid != context.ShellProcessId) throw new SessionManagerException("Windows 桌面在启动前发生变化，请重试。");
            viewObject = browser.QueryActiveShellView();
            Guid dispatchId = new("00020400-0000-0000-C000-000000000046");
            folderObject = ((IShellView)viewObject).GetItemObject(0, ref dispatchId);
            application = ((IShellFolderView)folderObject).Application;
            // This is Explorer's dispatch, not a new elevated Shell.Application object.
            ((IShellDispatch)application).ShellExecute(executable, arguments, workingDirectory, "open", hidden ? 0 : 1);
            var timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(10))
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        if (existing.Contains(process.Id)) continue;
                        try
                        {
                            if (!string.Equals(ReadImagePath(process.Id), executable, StringComparison.OrdinalIgnoreCase)) continue;
                            TokenState child = ReadToken(process.Id);
                            if (child.User != context.Shell.User || child.Session != context.Shell.Session)
                                throw new SessionManagerException("程序启动后的 Windows 用户或登录会话不符合预期，已停止后续自动操作。");
                            if (name.Equals("BrownDust II", StringComparison.OrdinalIgnoreCase)) WriteDiagnostic(context, process.Id, child);
                            return process.Id;
                        }
                        catch (InvalidOperationException) when (process.HasExited) { }
                    }
                }
                Thread.Sleep(50);
            }
            throw new SessionManagerException("启动请求已交给 Windows 桌面，但未确认游戏进程。请检查启动提示；不会自动重复启动。");
        }
        catch (COMException exception)
        {
            throw new SessionManagerException($"普通权限桌面启动失败（0x{exception.HResult:X8}）。请正常打开资源管理器后重试。");
        }
        finally
        {
            foreach (object? value in new[] { application, folderObject, viewObject, browserObject, desktop, windows })
                if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }
    }

    private static T InSta<T>(Func<T> action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) return action();
        T result = default!; ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() => { try { result = action(); } catch (Exception e) { failure = ExceptionDispatchInfo.Capture(e); } });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start(); thread.Join();
        failure?.Throw(); return result;
    }

    private static void WriteDiagnostic(Context context, int childId, TokenState child)
    {
        // No account IDs, registry data or authentication material are included.
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BD2AccountSessionManager");
            Directory.CreateDirectory(directory);
            string target = Path.Combine(directory, "last-launch.json"), temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(new { atUtc = DateTimeOffset.UtcNow, route = "desktop-shell", callerPid = Environment.ProcessId, callerElevated = context.Caller.Elevated, shellPid = context.ShellProcessId, shellElevated = context.Shell.Elevated, gamePid = childId, gameElevated = child.Elevated, sameWindowsUser = child.User == context.Caller.User }));
                File.Move(temp, target, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int kind, out int value, int size, out int returned);

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IShellWindows
    {
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW([MarshalAs(UnmanagedType.Struct)] ref object location, [MarshalAs(UnmanagedType.Struct)] ref object root, int kind, out int window, int options);
    }
    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IComServiceProvider
    {
        [return: MarshalAs(UnmanagedType.Interface)] object QueryService(ref Guid service, ref Guid iid);
    }
    // Slots through the requested Shell methods, in Windows SDK ABI order.
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow(out nint window); void ContextSensitiveHelp();
        void InsertMenusSB(); void SetMenuSB(); void RemoveMenusSB(); void SetStatusTextSB();
        void EnableModelessSB(); void TranslateAcceleratorSB(); void BrowseObject(); void GetViewStateStream();
        void GetControlWindow(); void SendControlMsg();
        [return: MarshalAs(UnmanagedType.Interface)] object QueryActiveShellView();
    }
    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow(); void ContextSensitiveHelp(); void TranslateAccelerator(); void EnableModeless();
        void UIActivate(); void Refresh(); void CreateViewWindow(); void DestroyViewWindow();
        void GetCurrentInfo(); void AddPropertySheetPages(); void SaveViewState(); void SelectItem();
        [return: MarshalAs(UnmanagedType.Interface)] object GetItemObject(uint aspect, ref Guid iid);
    }
    [ComImport, Guid("E7A1AF80-4D96-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IShellFolderView { object Application { [return: MarshalAs(UnmanagedType.IDispatch)] get; } }
    [ComImport, Guid("A4C6892C-3BA9-11D2-9DEA-00C04FB16162"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IShellDispatch
    {
        void ShellExecute([MarshalAs(UnmanagedType.BStr)] string file, [MarshalAs(UnmanagedType.Struct)] object arguments, [MarshalAs(UnmanagedType.Struct)] object directory, [MarshalAs(UnmanagedType.Struct)] object verb, [MarshalAs(UnmanagedType.Struct)] object show);
    }
}
