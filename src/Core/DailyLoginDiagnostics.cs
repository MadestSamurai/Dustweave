namespace Dustweave;

public static class DailyLoginDiagnostics
{
    public static void Capture(string root, string phase, DailyAccountCatalog catalog, IGameHost host, IAccountSessions sessions)
    {
        GameInstance? game = null; DailySnapshot? snapshot = null; string readError = "";
        try { game = host.Find(); snapshot = host.ReadSnapshot(); catalog = sessions.Read(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException)
        { readError = e.GetType().Name; }
        Record(root, phase, catalog, game, snapshot, readError);
    }
    // Three bounded summaries per connection, never token bytes or the full game DTO.
    public static void Record(string root, string phase, DailyAccountCatalog catalog, GameInstance? game, DailySnapshot? s, string readError = "")
    {
        if (phase is not ("request" or "verified" or "failed")) return;
        try
        {
            DailyJson.Write(Path.Combine(root, "live", "diagnostics", "login-" + phase + ".json"), new
            {
                atUtc = DateTimeOffset.UtcNow, phase, readError, catalog.SessionComplete, catalog.CurrentKey, catalog.LocalLogin,
                savedAccounts = catalog.Accounts.Count(a => a.Valid),
                game = game == null ? null : new { game.ProcessId, game.StartTicks },
                observed = s == null ? null : new { s.Runtime, s.ProcessId, s.ProcessStartTicks, s.InstanceId, s.Sequence, s.FrameUtcTicks,
                    s.State, s.Scene, s.AccountKey, s.PlayerKey, startupVisible = s.Startup?.Visible, startupStage = s.Startup?.Stage,
                    identityReady = game != null && DailyIdentityGuard.Ready(s, game, DateTimeOffset.UtcNow) }
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
