using Dustweave;
using Dustweave.Desktop;

internal static class FirstRunCases
{
    internal static async Task Run(string root, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add("first run: " + name); }
        async Task Reject(Func<Task> action, string name)
        {
            try { await action(); } catch (Exception error) when (error is InvalidOperationException or TimeoutException or OperationCanceledException) { Check(true, name); return; }
            throw new Exception("Not rejected: " + name);
        }
        Check(DailyFirstRun.ShouldOffer(null, false), "new users get setup");
        Check(!DailyFirstRun.ShouldOffer(null, true), "existing accounts are not interrupted by a new tutorial");
        Check(!DailyFirstRun.ShouldOffer(new(State: "skipped"), false), "skipping is remembered");
        Check(!DailyFirstRun.ShouldOffer(new(State: "completed"), true), "completion is remembered");
        Check(DailyFirstRun.ShouldOffer(new(State: "tour", TourIndex: 3), true), "unfinished tour resumes after saving an account");
        Check(!DailyMultiAccountSetup.ShouldOffer(null, 0, 1, true), "first account does not offer sandbox setup");
        Check(DailyMultiAccountSetup.ShouldOffer(null, 1, 2, true), "adding a second account offers execution choices");
        Check(!DailyMultiAccountSetup.ShouldOffer(null, 1, 2, false), "updating credentials does not offer execution choices");
        Check(!DailyMultiAccountSetup.ShouldOffer(null, 2, 2, false) && !DailyMultiAccountSetup.ShouldOffer(null, 2, 3, true), "later saves and added accounts do not repeat the prompt");
        foreach (string state in new[] { "pending", "completed", "dismissed" })
            Check(!DailyMultiAccountSetup.ShouldOffer(new(state), 1, 2, true), "remembered multi-account choice suppresses repeat: " + state);
        string setupRoot = System.IO.Path.Combine(root, "multi-account-trigger");
        Check(!DailyMultiAccountSetup.AfterSave(setupRoot, 0, 1, true) && DailyMultiAccountSetup.Read(setupRoot) == null, "single account does not create a setup record");
        Check(DailyMultiAccountSetup.AfterSave(setupRoot, 1, 2, true) && DailyMultiAccountSetup.Pending(setupRoot, 2), "pending second-account setup survives restart");
        Check(!DailyMultiAccountSetup.Pending(setupRoot, 1), "removing the second account suppresses pending setup");
        DailyMultiAccountSetup.Write(setupRoot, new("dismissed"));
        Check(!DailyMultiAccountSetup.Pending(setupRoot, 2) && !DailyMultiAccountSetup.AfterSave(setupRoot, 1, 2, true), "not now stays dismissed across restart and re-adding an account");
        var demo = new DemoEnvironment(root);
        demo.Game = null;
        var flow = new DailyFirstRun(demo, demo, root);
        var first = await flow.VerifyAsync(CancellationToken.None, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(5));
        Check(demo.Calls.Count(x => x == "launch-current") == 1 && !demo.Calls.Any(x => x.StartsWith("launch:")), "first use launches once without switching accounts");
        Check(first.Account == demo.CurrentKey && first.Player.Length > 0 && !flow.ReadyToSave(), "actual identity is required and cannot save while game runs");
        await Reject(() => { flow.Save("saved while running"); return Task.CompletedTask; }, "running game cannot be saved");
        Check(!demo.Calls.Any(x => x.StartsWith("save:")), "blocked save never reaches storage");
        demo.Game = null; Check(flow.ReadyToSave(), "normal exit unlocks saving");
        string expected = demo.CurrentKey; demo.CurrentKey = demo.Accounts[1].AccountKey;
        await Reject(() => { flow.Save("wrong account"); return Task.CompletedTask; }, "different account after verification cannot replace the detected one");
        demo.CurrentKey = expected; flow.Save("My account");
        Check(demo.Calls.Count(x => x == "save:1") == 1 && demo.Accounts.Count == 3, "known identity updates its original slot without a duplicate");
        Check(new DailyProfiles(root).Read().Single(p => p.AccountKey == expected).PlayerKey == first.Player, "saved account retains the verified game character");
        demo.Game = first.Game; demo.Calls.Clear();
        await flow.VerifyAsync(CancellationToken.None, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(5));
        Check(!demo.Calls.Contains("launch-current") && demo.Calls.Count(x => x == "connect") == 1, "running game is reused instead of relaunched");
        demo.IncompleteCatalogReads = int.MaxValue;
        await flow.VerifyAsync(CancellationToken.None, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(5));
        Check(flow.Proof?.Account == expected, "onboarding can identify an online account before credentials are saveable");
        demo.Game = null;
        await Reject(() => { flow.Save("not ready"); return Task.CompletedTask; }, "onboarding still refuses saving incomplete credentials");
        demo.Game = first.Game; demo.IncompleteCatalogReads = 0;
        demo.Ready = false;
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(35)))
            await Reject(async () => { await flow.VerifyAsync(cancel.Token, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(5)); }, "skipping cancels an ongoing identity wait");
        Check(demo.Game != null && !demo.Calls.Contains("close") && flow.Proof == null, "cancellation preserves the game and clears unverified identity");
        await Reject(async () => { await flow.VerifyAsync(CancellationToken.None, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(5)); }, "missing runtime identity times out instead of falsely completing");
        demo.Ready = true; demo.ForcedKey = demo.Accounts[1].AccountKey;
        await Reject(async () => { await flow.VerifyAsync(CancellationToken.None, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(5)); }, "registry identity alone does not accept a different live account");
        demo.ForcedKey = null; demo.Accounts.Clear();
        var fresh = new DailyFirstRun(demo, demo, Path.Combine(root, "new-account"));
        await fresh.VerifyAsync(CancellationToken.None, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(5)); demo.Game = null;
        fresh.Save("First account");
        Check(demo.Accounts.Count == 1 && demo.Accounts[0].AccountKey == expected, "first account is saved to an empty slot after verification");
    }
}
