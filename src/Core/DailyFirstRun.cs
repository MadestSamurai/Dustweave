namespace Dustweave;

public sealed record FirstRunProof(GameInstance Game, string Account, string Player, string Name, DateTimeOffset VerifiedUtc);
public sealed record FirstRunProgress(string Stage, string Detail = "");
public sealed record FirstRunRecord(int Revision = 1, string State = "started", int TourIndex = 0);

/// <summary>First-use identity verification. Never switches accounts or starts daily tasks.</summary>
public sealed class DailyFirstRun(IAccountSessions sessions, IGameHost host, string root)
{
    public FirstRunProof? Proof { get; private set; }
    public event Action<FirstRunProgress>? Progress;
    public static bool ShouldOffer(FirstRunRecord? record, bool hasAccounts)
        => record?.State is "started" or "tour" || (record == null && !hasAccounts);

    public async Task<FirstRunProof> VerifyAsync(CancellationToken token, TimeSpan? timeout = null, TimeSpan? interval = null)
    {
        Proof = null;
        sessions.EnsureControl();
        var current = sessions.Read();
        if (current.StarterRunning) throw new InvalidOperationException("onboarding.close_launcher");
        token.ThrowIfCancellationRequested();
        var game = host.Find();
        if (game == null)
        {
            Progress?.Invoke(new("launching"));
            sessions.LaunchCurrent();
            var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
            while ((game = host.Find()) == null)
            {
                token.ThrowIfCancellationRequested();
                if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("onboarding.launch_timeout");
                await Task.Delay(interval ?? TimeSpan.FromMilliseconds(500), token);
            }
        }
        Progress?.Invoke(new("connecting"));
        await host.ConnectAsync(game, detail => Progress?.Invoke(new("connecting", detail)), token);
        token.ThrowIfCancellationRequested();
        Progress?.Invoke(new("login"));
        var guard = new DailyIdentityGuard();
        var expires = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromMinutes(15));
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (host.Find() != game) throw new InvalidOperationException("onboarding.game_changed");
            var status = host.ReadStatus();
            if (status?.ProcessId == game.ProcessId && status.ProcessStartTicks == game.StartTicks && status.State == "error")
                throw new InvalidOperationException(status.Error);
            var snapshot = host.ReadSnapshot();
            var catalog = sessions.Read();
            // An existing registry token is not proof of the account currently in the game.
            if (catalog.SessionComplete && DailyProfiles.ValidKey(catalog.CurrentKey) && snapshot?.AccountKey == catalog.CurrentKey &&
                guard.Observe(snapshot, game, catalog.CurrentKey, "", DateTimeOffset.UtcNow))
            {
                Proof = new(game, snapshot.AccountKey, snapshot.PlayerKey, snapshot.PlayerName, DateTimeOffset.UtcNow);
                return Proof;
            }
            if (snapshot?.AccountKey != catalog.CurrentKey) guard = new();
            if (DateTimeOffset.UtcNow >= expires) throw new TimeoutException("onboarding.verify_timeout");
            await Task.Delay(interval ?? TimeSpan.FromSeconds(1), token);
        }
    }

    public bool ReadyToSave()
    {
        if (Proof == null) return false;
        var catalog = sessions.Read();
        if (host.Find() != null || catalog.GameRunning || catalog.StarterRunning) return false;
        if (!catalog.SessionComplete || catalog.CurrentKey != Proof.Account) throw new InvalidOperationException("onboarding.identity_changed");
        return true;
    }

    public void Save(string name)
    {
        if (!ReadyToSave()) throw new InvalidOperationException("onboarding.close_first");
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("onboarding.name_required");
        var plan = DailyAccountIdentity.SavePlan(sessions.Read());
        if (plan.AccountKey != Proof!.Account) throw new InvalidOperationException("onboarding.identity_changed");
        // The session adapter rechecks closure and full account identity before reading fresh credentials.
        sessions.Save(plan.SlotNumber, name.Trim(), plan.AccountKey);
        var saved = sessions.Read().Accounts.SingleOrDefault(x => x.Valid && x.SlotNumber == plan.SlotNumber && x.AccountKey == Proof.Account);
        if (saved == null) throw new InvalidOperationException("onboarding.save_unconfirmed");
        var profiles = new DailyProfiles(root);
        var profile = DailyAccountOrder.ProfileFor(saved, profiles.Read());
        profile.PlayerKey = Proof.Player; profile.PlayerName = Proof.Name; profile.LastVerifiedUtc = Proof.VerifiedUtc;
        profiles.Update(profile);
    }
}
