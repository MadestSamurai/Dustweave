using System.Diagnostics;
namespace Dustweave;

public static class DailyUpdateBridge
{
    public const string TargetVersion = "1.4.15";
    public static bool IsBridge(string version) => version == DailyUpdateInventory.BridgeVersion;
    public static bool IsInteractiveEntry(string[] args) => args.Length == 0 || args[0] is
        "--desktop-session" or "--launch-desktop" or "--scheduled" or "--updated" or
        "--view-account" or "--inspect-account" or "--resume-account" or "--run-selected";

    // Called before the desktop, scheduler, account connector or any game launch
    // can initialize. A bridge is never an alternate everyday application.
    public static bool ContinueBeforeStartup(string version, string[] args)
    {
        if (!IsBridge(version) || !IsInteractiveEntry(args)) return false;
        DailyUpdateHost.RequireHost();
        if (args is ["--updated", var nonce]) DailyUpdateEngine.Acknowledge(nonce, version);
        string helper = Path.Combine(DailyUpdateHost.InstallRoot, "app", "support", "Dustweave.Updater.exe");
        DailyUpdates.EnsureNoLinks(helper);
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = DailyUpdateHost.InstallRoot };
        start.ArgumentList.Add("--continue-upgrade"); start.ArgumentList.Add(DailyUpdateHost.InstallRoot);
        using var child = Process.Start(start) ?? throw new IOException("updates.start_failed");
        return true;
    }
    public static async Task WaitForPreviousInstallerAsync(string installation, CancellationToken token)
    {
        string marker = Path.Combine(installation, DailyUpdateTransaction.MarkerName);
        var watch = Stopwatch.StartNew();
        while (File.Exists(marker))
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(60)) throw new IOException("updates.recovery_required");
            await Task.Delay(100, token);
        }
    }
}
