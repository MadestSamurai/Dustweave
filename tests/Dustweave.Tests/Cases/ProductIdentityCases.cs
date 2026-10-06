using Dustweave;
using Dustweave.Accounts;
using System.Text;

static class ProductIdentityCases
{
    public static void Run(List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add("product identity: " + name); }
        Check(typeof(DailyTools).Assembly.GetName().Name == "Dustweave.Core", "core assembly uses the product name");
        Check(typeof(SessionConstants).Assembly.GetName().Name == "Dustweave.Accounts", "account assembly uses the product name");
        Check(DailyApplication.IsExecutable(@"C:\example\Dustweave.exe") && DailyApplication.IsExecutable("DUSTWEAVE.EXE"), "desktop and elevation accept the product executable");
        Check(!DailyApplication.IsExecutable("BD2DailyAssistant.exe") && !DailyApplication.IsExecutable("not-Dustweave.exe"), "unrelated filenames cannot enter the privileged launch path");
        Check(DailyApplication.InstanceMutex == @"Local\BD2DailyAssistant-v1" && DailyApplication.AccountMutex == @"Local\BD2AccountSessionManager-v1", "old and new versions share ownership identities");
        Check(DailyApplication.ElevationPipePrefix == "BD2Daily.Connect.", "connection handshake identity is unchanged");
        Check(SessionConstants.VaultDirectoryName == "BD2AccountSessionManager" && SessionConstants.SlotExtension == ".bd2slot" && SessionConstants.RecoveryFileName == "latest.bd2recovery", "saved accounts and recovery keep their existing locations");
        Check(SessionConstants.FileMagic.SequenceEqual("BD2SLOT1"u8.ToArray()) && SessionConstants.DpapiEntropy.SequenceEqual("BD2AccountSessionManager/v1/current-user"u8.ToArray()), "saved account format and encryption entropy remain compatible");
        byte[] sample = Encoding.UTF8.GetBytes("synthetic sign-in; no credentials");
        Check(DpapiProtector.Unprotect(DpapiProtector.Protect(sample)).SequenceEqual(sample), "renamed account assembly can protect and restore synthetic data");
        string? previous = Environment.GetEnvironmentVariable("BD2_DAILY_DATA_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("BD2_DAILY_DATA_ROOT", null);
            Check(DailyIdentity.DataRoot == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BD2DailyAssistant"), "default data path reuses existing user state without migration");
            Environment.SetEnvironmentVariable("BD2_DAILY_DATA_ROOT", @"C:\synthetic\daily-data");
            Check(DailyIdentity.DataRoot == @"C:\synthetic\daily-data", "isolated data override remains compatible");
        }
        finally { Environment.SetEnvironmentVariable("BD2_DAILY_DATA_ROOT", previous); }
        Check(DailyIdentity.RuntimeName == "BD2Daily.Runtime4" && SuiteRules.ModuleEntry("daily") == "BD2Daily.Live.Bridge", "runtime handoff and serialized bridge contracts retain their names");
    }
}
