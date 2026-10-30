namespace Dustweave;

public sealed record DailyMultiAccountSetupRecord(string State = "pending", bool? Parallel = null);

public static class DailyMultiAccountSetup
{
    private static string FilePath(string root) => Path.Combine(root, "multi-account-setup.json");
    public static DailyMultiAccountSetupRecord? Read(string root) => DailyJson.TryRead<DailyMultiAccountSetupRecord>(FilePath(root));
    public static void Write(string root, DailyMultiAccountSetupRecord value) => DailyJson.Write(FilePath(root), value);
    public static bool ShouldOffer(DailyMultiAccountSetupRecord? previous, int before, int after, bool newlyAdded)
        => previous == null && newlyAdded && before == 1 && after == 2;
    public static bool AfterSave(string root, int before, int after, bool newlyAdded)
    {
        if (!ShouldOffer(Read(root), before, after, newlyAdded)) return false;
        Write(root, new()); return true;
    }
    public static bool Pending(string root, int accounts) => accounts >= 2 && Read(root)?.State == "pending";
}
