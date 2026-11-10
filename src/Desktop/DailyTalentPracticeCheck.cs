using System.IO;
namespace Dustweave.Desktop;

// Explicit, bounded live verification. Never launches, switches or restores an
// account. Uses exactly the same host lease/session/workflow as the tool window.
internal static class DailyTalentPracticeCheck
{
    public static async Task<int> Run(string output, long character, int rounds)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        if (rounds < 0 || rounds > 20 || character == 0 && rounds != 0) throw new ArgumentOutOfRangeException(nameof(rounds));
        string root = DailyIdentity.DataRoot;
        try
        {
            string account;
            using (var lease = DailyToolControl.Acquire(root))
            {
                var host = new DailyGameHost(root);
                _ = host.Find() ?? throw new IOException("No running game");
                using var accounts = new AccountSessions();
                await CurrentGameObservation.ConnectAsync(accounts, host, _ => { }, CancellationToken.None);
                account = host.ReadSnapshot()?.AccountKey ?? throw new IOException("Game identity unavailable");
            }
            var session = new DailyBargainSession(root, account);
            var messages = new List<object>();
            session.Report = key => { messages.Add(new { at = DateTimeOffset.Now, step = key }); DailyJson.Write(Path.Combine(output, "steps.json"), messages); };
            var progress = new List<object>();
            session.Progress = p => { progress.Add(new { at = DateTimeOffset.Now, progress = p }); DailyJson.Write(Path.Combine(output, "progress.json"), progress); };
            var before = await session.Read();
            DailyJson.Write(Path.Combine(output, "before.json"), new { before.Characters, before.Potions, before.Player, before.Talents });
            string result = "read_only";
            if (rounds > 0)
            {
                var selected = before.Characters.Single(c => c.Character == character);
                if (!DailyTalentPracticeProof.FieldKind(selected.Kind)) throw new ArgumentException("Field practice only");
                result = await session.Train(selected, rounds);
            }
            var after = rounds == 0 ? before : await session.Read();
            DailyJson.Write(Path.Combine(output, "result.json"), new { status = "passed", result, after.Characters, after.Potions, after.Player, after.Talents });
            return 0;
        }
        catch (Exception e) { DailyJson.Write(Path.Combine(output, "error.json"), new { error = e.ToString() }); return 1; }
    }
}
