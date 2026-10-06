using System.Diagnostics;
namespace Dustweave;

/// <summary>Bounds a helper we launched and keeps stop/account checks live while it works.</summary>
public static class DailyHelperLifetime
{
    public static async Task<int> WaitAsync(Process helper, Func<Task> checkpoint, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        var exited = helper.WaitForExitAsync();
        try
        {
            while (!helper.HasExited)
            {
                await checkpoint().ConfigureAwait(false);
                if (clock.Elapsed >= timeout)
                    throw new TimeoutException("连接助手准备超过时限；本次助手已停止，未启动自动操作。");
                await Task.WhenAny(exited, Task.Delay(100)).ConfigureAwait(false);
            }
            // Stop or an account switch at the exit boundary must not start automation.
            await checkpoint().ConfigureAwait(false);
            return helper.ExitCode;
        }
        finally
        {
            if (!helper.HasExited)
            {
                try
                {
                    helper.Kill(entireProcessTree: true);
                    await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException) { }
            }
        }
    }
}
