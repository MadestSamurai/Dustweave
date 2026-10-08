using Microsoft.Win32;

namespace Dustweave.Accounts;

internal static class GameLauncher
{
    private const string InstallRegistrySubKey = @"Software\NEOWIZ\Browndust2Starter\10000001";
    private const string FallbackExecutable = @"C:\Neowiz\Browndust2\Browndust2_10000001\BrownDust II.exe";

    internal static int LaunchDirect(string? executable = null)
    {
        executable ??= ResolveExecutable();
        DirectPcChannel.Prepare(executable, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            SessionConstants.VaultDirectoryName, "launch-settings-backups"));
        return DesktopGameLaunch.Launch(executable);
    }

    internal static void ValidateLaunchContext()
    {
        _ = DirectPcChannel.Inspect(ResolveExecutable());
        _ = DesktopGameLaunch.ValidateContext();
    }

    internal static string ResolveExecutable()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
            InstallRegistrySubKey,
            writable: false);
        string? installPath = key?.GetValue("path") as string;
        string? executableName = key?.GetValue("execute") as string;
        if (!string.IsNullOrWhiteSpace(installPath)
            && !string.IsNullOrWhiteSpace(executableName))
        {
            string candidate = Path.GetFullPath(Path.Combine(installPath, executableName));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        if (File.Exists(FallbackExecutable))
        {
            return FallbackExecutable;
        }

        throw new SessionManagerException("未找到 Brown Dust 2 游戏可执行文件，请先通过官方启动器修复安装路径。");
    }
}
