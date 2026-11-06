using Dustweave.Accounts;

namespace Dustweave;

internal static class DailySandboxStartup
{
    internal static Task EnsureAsync(CancellationToken token)
    {
        DailySandbox.RequireHost();
        return SandboxRuntimeStartup.EnsureAsync(token, phase =>
            DailyJson.Write(Path.Combine(DailyIdentity.DataRoot, "sandbox-runtime-recovery.json"),
                new { atUtc = DateTimeOffset.UtcNow, phase }));
    }
}
