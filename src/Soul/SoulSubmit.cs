using System.Text.Json.Nodes;
using Dustweave;
namespace Dustweave.Soul;

internal static class SoulSubmit
{
    internal static bool Refreshable(DailyStepException error) =>
        error.Kind == "rejected" && error.DefinitelyNotDispatched &&
        error.RejectionCode is "screen_changed" or "ui_not_ready";

    internal static async Task<JsonObject> Send(DailyCommandDriver driver, JsonObject action)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return await driver.SubmitAsync(action); }
            catch (DailyStepException error) when (attempt < 4 && Refreshable(error))
            {
                Console.WriteLine($"Native dispatch explicitly rejected ({error.RejectionCode}); refreshing observation {attempt + 1}/4");
                await Task.Delay(200);
                await driver.ObserveAsync();
            }
        }
    }

    internal static void Test()
    {
        var rejected = new DailyStepException("rejected", "changed", true)
            { MayHaveDispatched = false, RejectionCode = "screen_changed" };
        if (!Refreshable(rejected)) throw new Exception("Explicit non-dispatch should permit fresh preflight");
        foreach (var error in new[] {
            new DailyStepException("pending", "unknown", true) { RejectionCode = "screen_changed" },
            new DailyStepException("rejected", "unknown", true) { RejectionCode = "screen_changed" },
            new DailyStepException("rejected", "identity", false) { RejectionCode = "account_changed" },
            new DailyStepException("rejected", "stale", false) { RejectionCode = "soul_context_rejected" } })
            if (Refreshable(error)) throw new Exception("Uncertain or changed transaction must never be replayed");
        Console.WriteLine("Research dispatch recovery checks passed: 5");
    }
}
