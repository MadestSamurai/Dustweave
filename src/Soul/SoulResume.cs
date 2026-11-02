using System.Text.Json.Nodes;

namespace Dustweave.Soul;

internal sealed class SoulBattleRecoveryNeeded(string message) : Exception(message);

internal static class SoulResume
{
    internal static bool InBattle(JsonObject frame)=>frame["Surfaces"]!.AsArray().Any(s=>s?["Type"]?.ToString()=="InventoryBattleUI")
        &&!frame["Surfaces"]!.AsArray().Any(s=>s?["Type"]?.ToString()=="InventoryBattleResultUI");
    internal static bool Transient(Exception error)=>error is StageHostException { ObservationTimedOut:true }
        or DailyStepException { ObservationPending:true } or SoulBattleRecoveryNeeded;

    internal static void CheckIdentity(bool sameProcess,string account,string player,string? currentAccount,string? currentPlayer)
    {
        // Freshness is checked by the driver's read-bound preflight. Missing
        // identity during automatic login is not evidence of a different user.
        if(!sameProcess||!string.IsNullOrEmpty(currentAccount)&&currentAccount!=account
            ||!string.IsNullOrEmpty(currentPlayer)&&currentPlayer!=player)
            throw new InvalidOperationException("游戏进程或账号已变化；请连接原账号后继续挑战。 / Game or account changed; reconnect the saved account and Resume.");
    }

    internal static JsonNode Response(JsonObject response,string role)
    {
        if(response["Role"]?.ToString()!=role||response["Kind"]?.ToString()!="response"||response["Accepted"]?.GetValue<bool>()!=true)
            throw new InvalidDataException("Accepted current-game response required.");
        return JsonNode.Parse(response["Values"]![0]!["Json"]!.GetValue<string>())!;
    }
}
