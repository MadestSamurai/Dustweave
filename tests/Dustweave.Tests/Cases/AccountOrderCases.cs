using Dustweave;
using Dustweave.Desktop;

internal static class AccountOrderCases
{
    public static async Task Run(string root, List<string> cases)
    {
        void Check(bool ok, string message)
        {
            if (!ok)
                throw new Exception(message);
        }
        var accounts = Enumerable.Range(1, 10).Select(n => new DailyAccount(n, $"Account {n}", DailyIdentity.MemberKey((7000 + n).ToString()), $"***{n}", true, n == 1, "")).ToArray();
        int[] Slots(IEnumerable<DailyAccountEntry> entries) => entries.Select(e => e.Account.SlotNumber).ToArray();
        var natural = Enumerable.Range(1, 10).ToArray();
        var path = Path.Combine(root, "account-order-" + Guid.NewGuid().ToString("N"));
        var store = new DailyProfiles(path);
        store.Update(new()
        {
            AccountKey = accounts[0].AccountKey,
            SlotNumber = 1,
            Order = 0,
            PlayerName = "Original"
        });
        store.Update(new()
        {
            AccountKey = accounts[9].AccountKey,
            SlotNumber = 10,
            Order = 9,
            PlayerName = "New"
        });
        var originalBytes = File.ReadAllBytes(Path.Combine(path, "accounts.json"));
        Check(Slots(DailyAccountOrder.Arrange(accounts, store.Read())).SequenceEqual(natural), "Sparse saved profiles moved slot 10 ahead of slot 2");
        Check(File.ReadAllBytes(Path.Combine(path, "accounts.json")).SequenceEqual(originalBytes), "Reading order modified profiles");
        cases.Add("account order keeps sparse saved profiles in slot order without migration writes");

        foreach (var slot in new[] { 8, 2, 5 })
        {
            var profile = DailyAccountOrder.ProfileFor(accounts[slot - 1], store.Read());
            profile.Selected = true;
            store.Update(profile);
            Check(Slots(DailyAccountOrder.Arrange(accounts, store.Read())).SequenceEqual(natural), "Checking an account changed its rank");
        }
        cases.Add("account checkboxes preserve sparse profile order across reloads");

        var moved = DailyAccountOrder.Arrange(accounts, store.Read()).ToList();
        var last = moved[9];
        moved.RemoveAt(9);
        moved.Insert(1, last);
        for (int i = 0; i < moved.Count; i++)
        {
            moved[i].Profile.Order = i;
            store.Update(moved[i].Profile);
        }
        var custom = new[] { 1, 10, 2, 3, 4, 5, 6, 7, 8, 9 };
        Check(Slots(DailyAccountOrder.Arrange(accounts.Reverse(), new DailyProfiles(path).Read())).SequenceEqual(custom), "Manual ordering lost on restart");
        var selected = DailyAccountOrder.ProfileFor(accounts[9], store.Read());
        selected.Selected = true;
        store.Update(selected);
        Check(Slots(DailyAccountOrder.Arrange(accounts, store.Read())).SequenceEqual(custom), "Saving preferences reset manual order");
        cases.Add("account explicit manual order survives restart and preference updates");

        var appended = accounts.Append(new DailyAccount(11, "New account", DailyIdentity.MemberKey("7011"), "***11", true, false, ""));
        Check(Slots(DailyAccountOrder.Arrange(appended, store.Read())).SequenceEqual(custom.Append(11)), "New end slot disrupted custom order");
        Check(accounts.Select(a => a.SlotNumber).SequenceEqual(natural), "Ordering changed vault slot numbers");
        cases.Add("new account appends after existing manual order without changing vault slots");

        var replacement = accounts[1] with
        {
            AccountKey = DailyIdentity.MemberKey("9999")
        };
        var replacementProfile = DailyAccountOrder.ProfileFor(replacement, store.Read());
        Check(!replacementProfile.Selected && replacementProfile.PlayerName == "" && replacementProfile.Order == 1, "Replacement inherited another identity's preferences");
        Check(Slots(DailyAccountOrder.Arrange([accounts[9], accounts[1]], [])).SequenceEqual(new[] { 2, 10 }), "Gaps used list indices rather than real slots");
        cases.Add("account ordering respects identity and sparse vault slots");

        var verifyPath = Path.Combine(root, "account-order-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(verifyPath);
        var env = new DemoEnvironment(verifyPath);
        var runner = new DailyCoordinator(env, env, verifyPath, new DailyOptions { PollInterval = TimeSpan.FromMilliseconds(2), LoginTimeout = TimeSpan.FromSeconds(2) });
        await runner.InspectAsync([env.Accounts[2]]);
        var verified = new DailyProfiles(verifyPath).Read();
        Check(verified.Single().Order == 2 && verified.Single().LastVerifiedUtc != null, "First verification wrote rank zero");
        Check(Slots(DailyAccountOrder.Arrange(env.Accounts, verified)).SequenceEqual(new[] { 1, 2, 3 }), "First verification reordered accounts");
        cases.Add("first login verification preserves default account order");
    }
}
