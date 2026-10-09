using System.Diagnostics;
namespace Dustweave;

// A desktop/queue worker is a new owner even when launched by another Dustweave process.
// Hosted tool and connection-helper children explicitly inherit the owning desktop identity.
public static class DailySuiteOwner
{
    public static bool Inherits(string[] args) => args.Length > 0 && args[0] is "--tool" or "--utility" or "--connection" or DailyConnectionAccess.HelperSwitch;
    public static void Configure(bool inherit)
    {
        if (inherit && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BD2_DAILY_SUITE_OWNER"))
            && int.TryParse(Environment.GetEnvironmentVariable("BD2_DAILY_OWNER_PID"), out var pid) && pid > 0
            && long.TryParse(Environment.GetEnvironmentVariable("BD2_DAILY_OWNER_START"), out var start) && start > 0) return;
        using var owner = Process.GetCurrentProcess();
        Environment.SetEnvironmentVariable("BD2_DAILY_SUITE_OWNER", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("BD2_DAILY_OWNER_PID", owner.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable("BD2_DAILY_OWNER_START", owner.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}