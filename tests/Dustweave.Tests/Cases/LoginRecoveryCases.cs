using Dustweave;
using Dustweave.Desktop;

internal static class LoginRecoveryCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); cases.Add("login-recovery: " + label); }
        string root = Path.Combine(output, "login-recovery"); Directory.CreateDirectory(root);
        var fixture = new DemoEnvironment(root) { Game = null, FailOrdinaryLaunch = true };
        var target = fixture.Accounts[1]; fixture.CurrentKey = target.AccountKey;
        var coordinator = new DailyCoordinator(fixture, fixture, root, new DailyOptions { PollInterval = TimeSpan.FromMilliseconds(2), LoginTimeout = TimeSpan.FromSeconds(2) });
        bool refused = false;
        try { await coordinator.InspectAsync([target]); } catch (InvalidOperationException e) { refused = e.Message == "account.relogin.required"; }
        Check(refused && fixture.Game == null, "ordinary launch reproduces unavailable sandbox handoff");
        var intent = new DailyLoginRecovery(target.SlotNumber, target.AccountKey, target.Name, new string('a', 64), DateTimeOffset.UtcNow);
        fixture.LoginRecovery = intent;
        int before = fixture.Calls.Count;
        await coordinator.InspectRecoveredAsync(target);
        Check(fixture.Calls.Skip(before).Contains("launch-recovered:2") && !fixture.Calls.Skip(before).Any(c => c.StartsWith("launch:")),
            "manual save and retry never re-enters the failed ordinary collection route");
        Check(fixture.LoginRecovery == null && new DailyProfiles(root).Read().Single().AccountKey == target.AccountKey,
            "recovered launch still verifies the live account and updates its identity record");
        before = fixture.Calls.Count; fixture.LoginRecovery = intent;
        refused = false;
        try { await coordinator.InspectRecoveredAsync(target); } catch (InvalidOperationException) { refused = true; }
        Check(refused && fixture.Game != null && !fixture.Calls.Skip(before).Any(c => c.StartsWith("save-recovered:")),
            "recovery cannot close or replace an already running game");
        fixture.Game = null; fixture.LoginRecovery = null;
        refused = false;
        try { await coordinator.InspectRecoveredAsync(target); } catch (InvalidOperationException e) { refused = e.Message == "account.relogin.no_pending"; }
        Check(refused && fixture.Game == null, "special recovery launch requires an explicit pending recovery");
        fixture.LoginRecovery = intent with { Slot = fixture.Accounts[0].SlotNumber, Account = fixture.Accounts[0].AccountKey };
        refused = false;
        try { await coordinator.InspectRecoveredAsync(target); } catch (InvalidOperationException e) { refused = e.Message == "account.relogin.no_pending"; }
        Check(refused && fixture.Game == null, "selected account must match the recovery before any save or launch");
    }
}
