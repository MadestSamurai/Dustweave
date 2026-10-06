namespace Dustweave;

/// <summary>Product filenames change independently from persisted/shared ownership identities.</summary>
public static class DailyApplication
{
    public const string ExecutableName = "Dustweave.exe";
    // Shared with already installed versions; changing these would permit competing owners.
    public const string InstanceMutex = @"Local\BD2DailyAssistant-v1";
    public const string AccountMutex = @"Local\BD2AccountSessionManager-v1";
    public const string ElevationPipePrefix = "BD2Daily.Connect.";
    public static bool IsExecutable(string path) =>
        Path.GetFileName(path).Equals(ExecutableName, StringComparison.OrdinalIgnoreCase);
}
