using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BD2Daily;

/// <summary>A process-lifetime file lease; release is thread independent and survives no crash.</summary>
public static class DailyToolControl
{
    public static bool IsOccupied(string root) { try { using var lease=Acquire(root); return false; } catch(InvalidOperationException) { return true; } }
    public static IDisposable Acquire(string root)
    {
        string directory = Path.Combine(root, "tools");
        Directory.CreateDirectory(directory);
        try { return new FileStream(Path.Combine(directory, "control.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException e) { throw new InvalidOperationException("其他自动化正在运行或等待结算，请先停止该功能；无需关闭窗口。", e); }
    }
}

public sealed record DailyToolProcessState(string ToolId, int ProcessId, long StartTicks, string Executable);

/// <summary>Owns only windows started by this menu. Never kills the game or a hung tool.</summary>
public sealed class DailyToolSession(string root, string executable, Func<DailyToolDefinition, ProcessStartInfo>? launcher = null, TimeSpan? closeTimeout = null)
{
    private readonly string path = Path.Combine(root, "tools", "window.json");
    private readonly string executable = Path.GetFullPath(executable);
    private readonly SemaphoreSlim changes = new(1, 1);
    public Action<ProcessStartInfo,string>? ConfigureLaunch {get;set;}
    public string Message { get; private set; } = "连接一次即可切换工具；打开工具后不会自动开始操作。";
    public DailyToolProcessState? Current
    {
        get
        {
            var state = DailyJson.TryRead<DailyToolProcessState>(path);
            if (state == null || !DailyToolCatalog.All.Any(t => t.Id == state.ToolId) || !Matches(state)) return null;
            return state;
        }
    }
    private bool Matches(DailyToolProcessState state)
    {
        if (!string.Equals(state.Executable, executable, StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var process = Process.GetProcessById(state.ProcessId);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == state.StartTicks
                && string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
    public async Task OpenAsync(string id, bool controlReserved=false)
    {
        var tool = DailyToolCatalog.Find(id);
        if (!await changes.WaitAsync(0)) return;
        try
        {
            if (Current is { } current)
            {
                if (current.ToolId == id) { Show(); return; }
                if (!await CloseCoreAsync()) return;
            }
            // Verify idle before launch. The parent disables all other commands until the child is recorded.
            if(!controlReserved)using (DailyToolControl.Acquire(root)) { }
            if (Mutex.TryOpenExisting(tool.MutexName, out var existing))
            {
                existing.Dispose();
                throw new InvalidOperationException(tool.Name + "的独立窗口已经打开，请先使用或关闭原窗口。");
            }
            var start = launcher?.Invoke(tool) ?? new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
            if (launcher == null) { start.ArgumentList.Add("--tool"); start.ArgumentList.Add(id); }
            start.Environment["BD2_DAILY_DATA_ROOT"] = root; ConfigureLaunch?.Invoke(start,id);
            using var child = Process.Start(start) ?? throw new InvalidOperationException("无法打开工具。");
            var state = new DailyToolProcessState(id, child.Id, child.StartTime.ToUniversalTime().Ticks, executable);
            DailyJson.Write(path, state);
            await Task.Delay(350);
            if (child.HasExited) throw new InvalidOperationException(tool.Name + "未能启动，请查看工具诊断记录。");
            Message = tool.Name + "窗口已打开。未启用自动化时，可以直接运行日常或切换工具。";
        }
        finally { changes.Release(); }
    }
    public async Task<bool> CloseAsync()
    {
        if (!await changes.WaitAsync(0)) return false;
        try { return await CloseCoreAsync(); }
        finally { changes.Release(); }
    }
    private async Task<bool> CloseCoreAsync()
    {
        var state = Current;
        if (state == null) return true;
        using var process = Process.GetProcessById(state.ProcessId);
        Message = "正在等待工具正常停止和关闭…";
        process.Refresh();
        if (!process.HasExited && !process.CloseMainWindow())
        {
            Message = "工具正在准备或处理操作，请到原窗口停止并关闭；不会强制中断。";
            return false;
        }
        try { await process.WaitForExitAsync().WaitAsync(closeTimeout ?? TimeSpan.FromSeconds(15)); }
        catch (TimeoutException) { Message = "工具仍在处理操作，请在原窗口停止并关闭后再试。"; return false; }
        Message = "工具已关闭，可以继续日常。";
        return true;
    }
    public void Show()
    {
        if (Current is not { } state) return;
        using var process = Process.GetProcessById(state.ProcessId);
        process.Refresh();
        if (process.MainWindowHandle == IntPtr.Zero) { Message = "工具正在打开窗口，请稍候。"; return; }
        ShowWindowAsync(process.MainWindowHandle, 9);
        SetForegroundWindow(process.MainWindowHandle);
    }
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}
