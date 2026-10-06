using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dustweave.Accounts;
using Dustweave.Compatibility;
using Microsoft.Win32.SafeHandles;
using SharpMonoInjector;

namespace Dustweave;

// Only the initial observer injection can require elevation. The GUI, accounts,
// cache and all subsequent module/command traffic stay with the original user.
public static class DailyConnectionAccess
{
    public const string HelperSwitch = "--daily-connect-elevated";
    private const string PipePrefix = DailyApplication.ElevationPipePrefix;
    internal sealed record Request(string Nonce, GameInstance Game, string Fingerprint, string PayloadSha);
    internal sealed record Reply(string Nonce, string State, long Address = 0, string Error = "");

    internal static GameInstance Describe(int pid)
    {
        using var handle = OpenProcess(0x1000, false, pid); // QUERY_LIMITED_INFORMATION, including elevated games
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取游戏进程信息");
        var path = new StringBuilder(32768); int length = path.Capacity;
        if (!QueryFullProcessImageName(handle, 0, path, ref length) || !GetProcessTimes(handle, out long created, out _, out _, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法确认游戏路径或启动时间");
        return new(pid, DateTime.FromFileTimeUtc(created).Ticks, path.ToString());
    }

    internal static bool NeedsElevation(bool callerElevated, bool gameElevated, bool sameUser, bool sameSession)
    {
        if (!sameUser || !sameSession) throw new InvalidOperationException("游戏与工具不属于同一 Windows 用户或登录会话，已停止连接。");
        return gameElevated && !callerElevated;
    }

    internal static void RequireGame(GameInstance expected)
    {
        if (Path.GetFileName(expected.Executable) != "BrownDust II.exe" || Describe(expected.ProcessId) != expected)
            throw new InvalidOperationException("游戏进程已退出或改变，请重新连接。");
    }

    internal static async Task WaitForMono(GameInstance game, CancellationToken token)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        while (true)
        {
            token.ThrowIfCancellationRequested(); RequireGame(game);
            using var process = Process.GetProcessById(game.ProcessId);
            if (process.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Equals("mono-2.0-bdwgc.dll", StringComparison.OrdinalIgnoreCase))) return;
            if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("游戏引擎尚未就绪；请等待加载完成后重新连接。");
            await Task.Delay(500, token);
        }
    }

    internal static async Task<long> InjectAsync(GameInstance game, byte[] payload, Action<string> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); RequireGame(game);
        var caller = DesktopGameLaunch.ReadToken(Environment.ProcessId);
        var target = DesktopGameLaunch.ReadToken(game.ProcessId);
        if (NeedsElevation(caller.Elevated, target.Elevated, caller.User == target.User, caller.Session == target.Session))
        {
            progress("游戏以管理员权限运行，请允许连接组件的 Windows 权限提示；主窗口保持普通权限");
            return await ElevateAsync(game, payload, token);
        }
        progress("等待游戏引擎初始化");
        await WaitForMono(game, token);
        progress("连接账户识别组件");
        return await Task.Run(() => Inject(game, payload, token), CancellationToken.None);
    }

    private static long Inject(GameInstance game, byte[] payload, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); RequireGame(game);
        using var injector = new Injector(game.ProcessId);
        return injector.Inject(payload, "BD2Daily.Runtime", "Loader", "Load").ToInt64();
    }

    private static async Task<long> ElevateAsync(GameInstance game, byte[] payload, CancellationToken token)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位连接组件");
        if (!DailyApplication.IsExecutable(executable))
            throw new InvalidOperationException("请使用正式日常工具连接管理员游戏。");
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), pipeName = PipePrefix + Guid.NewGuid().ToString("N");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var self = Describe(Environment.ProcessId);
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = AppContext.BaseDirectory };
        start.ArgumentList.Add(HelperSwitch); start.ArgumentList.Add(pipeName); start.ArgumentList.Add(nonce);
        start.ArgumentList.Add(self.ProcessId.ToString()); start.ArgumentList.Add(self.StartTicks.ToString());
        // ShellExecute may wait for UAC. Never block the UI or repeat a declined request.
        var launch = Task.Run(() => Process.Start(start) ?? throw new IOException("连接组件未启动。"));
        Process? helper = null;
        try
        {
            helper = await launch.WaitAsync(timeout.Token);
            var connected = pipe.WaitForConnectionAsync(timeout.Token);
            var exited = helper.WaitForExitAsync(timeout.Token);
            if (await Task.WhenAny(connected, exited) == exited && !pipe.IsConnected)
                throw new InvalidOperationException("管理员连接组件已退出，请使用当前 Windows 用户允许授权后重新连接。");
            await connected;
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint client) || client != helper.Id)
                throw new InvalidOperationException("连接组件身份不匹配。");
            var elevated = DesktopGameLaunch.ReadToken(helper.Id);
            var original = DesktopGameLaunch.ReadToken(Environment.ProcessId);
            if (!elevated.Elevated || elevated.User != original.User || elevated.Session != original.Session)
                throw new InvalidOperationException("请使用当前 Windows 用户允许连接，不支持切换到另一个管理员账号。");
            await Send(pipe, new Request(nonce, game, DailyHookCompiler.Fingerprint, Convert.ToHexString(SHA256.HashData(payload))), timeout.Token);
            var ready = await Receive<Reply>(pipe, timeout.Token); RequireReply(ready, nonce, "ready");
            token.ThrowIfCancellationRequested(); RequireGame(game);
            await Send(pipe, new Reply(nonce, "connect"), timeout.Token);
            var result = await Receive<Reply>(pipe, timeout.Token); RequireReply(result, nonce, "connected");
            return result.Address;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { throw new InvalidOperationException("已取消管理员连接授权；游戏与日常任务均未改动。", e); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("管理员连接等待超时，请处理 Windows 权限提示后重新连接；不会自动重复申请权限。"); }
        finally
        {
            helper?.Dispose();
            // A UAC dialog can outlive cancellation. The closed pipe makes late approval harmless.
            if (helper == null) _ = launch.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) t.Result.Dispose(); else _ = t.Exception; }, TaskScheduler.Default);
        }
    }

    public static async Task<int> RunHelperAsync(string[] args)
    {
        if (args.Length != 5 || args[0] != HelperSwitch || !args[1].StartsWith(PipePrefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(args[1][PipePrefix.Length..], "N", out _) || args[2].Length != 64
            || !int.TryParse(args[3], out int parentPid) || !long.TryParse(args[4], out long parentStart)) return 2;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            var parent = Describe(parentPid); var self = Describe(Environment.ProcessId);
            var parentToken = DesktopGameLaunch.ReadToken(parentPid); var selfToken = DesktopGameLaunch.ReadToken(Environment.ProcessId);
            if (parent.StartTicks != parentStart || !string.Equals(parent.Executable, self.Executable, StringComparison.OrdinalIgnoreCase)
                || !selfToken.Elevated || parentToken.User != selfToken.User || parentToken.Session != selfToken.Session) return 2;
            await pipe.ConnectAsync(10000, timeout.Token);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint server) || server != parentPid) return 2;
            var request = await Receive<Request>(pipe, timeout.Token);
            if (request.Nonce != args[2] || request.Fingerprint != DailyHookCompiler.Fingerprint) throw new InvalidDataException("连接请求版本或身份不匹配。");
            RequireGame(request.Game);
            var target = DesktopGameLaunch.ReadToken(request.Game.ProcessId);
            NeedsElevation(selfToken.Elevated, target.Elevated, target.User == selfToken.User, target.Session == selfToken.Session);
            string managed = Path.Combine(Path.GetDirectoryName(request.Game.Executable)!, Path.GetFileNameWithoutExtension(request.Game.Executable) + "_Data", "Managed");
            // Compile our embedded observer; never accept a caller-selected DLL, code, or output path.
            var prepared = await Task.Run(() => DailyHookCompiler.Prepare(managed, suite: new Dictionary<string, byte[]>(), manifest: ""), timeout.Token);
            if (Convert.ToHexString(SHA256.HashData(prepared.Payload)) != request.PayloadSha) throw new InvalidDataException("连接组件校验不一致，尚未连接游戏。");
            await WaitForMono(request.Game, timeout.Token);
            await Send(pipe, new Reply(request.Nonce, "ready"), timeout.Token);
            RequireReply(await Receive<Reply>(pipe, timeout.Token), request.Nonce, "connect");
            if (Describe(parentPid) != parent) throw new InvalidOperationException("日常主窗口已退出。");
            long address = Inject(request.Game, prepared.Payload, timeout.Token);
            await Send(pipe, new Reply(request.Nonce, "connected", address), timeout.Token);
            return 0;
        }
        catch (Exception error)
        {
            if (pipe.IsConnected)
                try { using var finish = new CancellationTokenSource(TimeSpan.FromSeconds(2)); await Send(pipe, new Reply(args[2], "error", Error: error.GetBaseException().Message), finish.Token); } catch { }
            return 1;
        }
    }

    internal static void RequireReply(Reply reply, string nonce, string state)
    {
        if (reply.Nonce != nonce) throw new InvalidDataException("连接响应身份不匹配。");
        if (reply.State == "error") throw new InvalidOperationException("连接组件：" + reply.Error);
        if (reply.State != state) throw new InvalidDataException("连接响应阶段不匹配。");
    }
    internal static async Task Send<T>(Stream stream, T value, CancellationToken token)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > 16384) throw new InvalidDataException("连接消息过大。");
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), token); await stream.WriteAsync(bytes, token); await stream.FlushAsync(token);
    }
    internal static async Task<T> Receive<T>(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, token);
        int length = BitConverter.ToInt32(header);
        if (length < 1 || length > 16384) throw new InvalidDataException("连接消息长度无效。");
        byte[] bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, token);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("连接消息为空。");
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(SafeProcessHandle handle, uint flags, StringBuilder path, ref int length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(SafeProcessHandle handle, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle handle, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle handle, out uint pid);
}


