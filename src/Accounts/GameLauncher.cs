namespace Dustweave.Accounts;

internal static class GameLauncher
{

    internal static int LaunchDirect(string? executable = null)
    {
        executable ??= ResolveExecutable();
        DirectPcChannel.Prepare(executable, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            SessionConstants.VaultDirectoryName, "launch-settings-backups"));
        return DesktopGameLaunch.Launch(executable);
    }

    internal static void ValidateLaunchContext(string? executable = null)
    {
        _ = DirectPcChannel.Inspect(executable ?? ResolveExecutable());
        _ = DesktopGameLaunch.ValidateContext();
    }

    internal static string ResolveExecutable() => new GameInstallation().Resolve();
}
