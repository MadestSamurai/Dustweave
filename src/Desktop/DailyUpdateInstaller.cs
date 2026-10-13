using System.IO;
namespace Dustweave.Desktop;

// Kept at the host boundary; installation itself runs in a separate executable.
internal static class DailyUpdateInstaller
{
    internal static void ValidateJob(DailyUpdateJob job, string path) => DailyUpdateEngine.ValidateJob(job, path);
    internal static int Run(string path, bool recovery = false) => DailyUpdateEngine.Run(path, recovery);
    internal static void Acknowledge(string nonce) => DailyUpdateEngine.Acknowledge(nonce, DailyProductVersion.Current);
    internal static bool RecoverBeforeStartup(string[] args)
    {
        // Packaged startup recovery is handled by the stable launcher before it
        // loads this assembly. A development entry must not ignore a transaction.
        string marker = Path.Combine(AppContext.BaseDirectory, DailyUpdateTransaction.MarkerName);
        if (!File.Exists(marker) || args.Length == 2 && args[0] == "--updated") return false;
        throw new IOException("updates.recovery_required");
    }
}