using System.IO;
using Dustweave.Golden;
namespace Dustweave.Desktop;

// Explicit live acceptance entry. No game launch/account switching or combat. Writes at most one empty slot.
internal static class GoldenPresetCheck
{
    public static async Task<int> Run(string output, bool writeEmpty)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        try
        {
            string root = DailyIdentity.DataRoot, account;
            using (var lease = DailyToolControl.Acquire(root))
            {
                var host = new DailyGameHost(root); _ = host.Find() ?? throw new IOException("No running game");
                using var accounts = new AccountSessions();
                await CurrentGameObservation.ConnectAsync(accounts, host, _ => { }, CancellationToken.None);
                account = host.ReadSnapshot()?.AccountKey ?? throw new IOException("Game identity unavailable");
            }
            var session = new GoldenPresetSession(root, account);
            var steps = new List<object>(); session.Report = key => { steps.Add(new { at = DateTimeOffset.Now, step = key }); DailyJson.Write(Path.Combine(output, "steps.json"), steps); };
            var before = await session.Read(); DailyJson.Write(Path.Combine(output, "before.json"), before);
            var source = before.Slots.FirstOrDefault(s => {
                if (s.Code.Length == 0) return false;
                try { FormationImport.Validate(FormationCode.Decode(s.Code), before.Rules, before.Owned, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()); return true; } catch { return false; }
            });
            var empty = before.Slots.FirstOrDefault(s => s.Code.Length == 0 && s.Error.Length == 0);
            if (!writeEmpty || empty == null)
            {
                DailyJson.Write(Path.Combine(output, "result.json"), new { status = "passed", result = "read_only", validSource = source?.Slot, emptySlot = empty?.Slot, before.Rules.Season, slots = before.Slots.Length }); return 0;
            }
                        // Empty test account: make a legal one-costume fixture from its actual owned inventory.
            // Still writes only an observed empty slot, and never changes the active deck.
            var fixture = source == null ? new Formation { Season = before.Rules.Season, Width = before.Rules.Width, Height = before.Rules.Height,
                Units = [new FormationUnit { Costume = before.Owned.First(id => !before.Rules.BannedCostumes.Contains(id) &&
                    (before.Rules.CostLimit < 0 || before.Rules.Costs.GetValueOrDefault(id, before.Rules.DefaultCost) <= before.Rules.CostLimit)),
                    Position = Enumerable.Range(0, before.Rules.Width * before.Rules.Height).First(p => !before.Rules.BannedPositions.Contains(p)) }] }
                : FormationCode.Decode(source.Code);
            FormationImport.Validate(fixture, before.Rules, before.Owned, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            string code = FormationCode.Encode(fixture);
            var refreshed = await session.Read(code);
            if (refreshed.Slots.Single(s => s.Slot == empty.Slot) is not { Code.Length: 0, Error.Length: 0 }) throw new IOException("Selected empty slot changed");
            var saved = await session.Save(code, empty.Slot, refreshed); DailyJson.Write(Path.Combine(output, "after.json"), saved);
            if (saved.Slots.Single(s => s.Slot == empty.Slot).Code != code || before.Slots.Where(s => s.Slot != empty.Slot).Any(s => s.Backup != saved.Slots.Single(t => t.Slot == s.Slot).Backup)) throw new IOException("Preset roundtrip/other-slot check failed");
            DailyJson.Write(Path.Combine(output, "result.json"), new { status = "passed", result = "empty_slot_roundtrip", sourceSlot = source?.Slot + 1, targetSlot = empty.Slot + 1, code, saved.Rules.Season }); return 0;
        }
        catch (Exception e) { DailyJson.Write(Path.Combine(output, "error.json"), new { error = e.ToString() }); return 1; }
    }
}
