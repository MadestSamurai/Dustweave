namespace Dustweave;

/// <summary>One campaign, independent of cache cleanup and future application versions.</summary>
public sealed class DailyResourceReview(string root)
{
    private sealed record Receipt(int Revision);
    private string Path => System.IO.Path.Combine(root, "resource-settings-reviewed.json");
    public bool Needed => DailyJson.TryRead<Receipt>(Path)?.Revision != 1;
    public void Dismiss() => DailyJson.Write(Path, new Receipt(1));
    public sealed record Options(bool OpenBoxes, bool Refill, long Target, bool PreserveCocktail)
    {
        public static Options From(DailyPreferences p) => new(p.Trade.OpenBoxesBeforeTrade,
            p.General.RefillTalentPotions, p.General.TalentPotionTarget, p.Stages.PreserveBloodCocktailMail);
        public void Apply(DailyPreferences p)
        {
            p.Trade.OpenBoxesBeforeTrade = OpenBoxes;
            p.General.RefillTalentPotions = Refill;
            p.General.TalentPotionTarget = Target;
            p.Stages.PreserveBloodCocktailMail = PreserveCocktail;
            p.Validate();
        }
    }
    public void Save(IReadOnlyDictionary<string, Options> edits)
    {
        var store = new DailyPreferenceStore(root);
        // Validate every edited account before any write. Read again to preserve
        // unrelated autosaved preferences, rather than writing a stale full copy.
        var values = edits.Select(e => { var p = store.Read(e.Key); e.Value.Apply(p); return (e.Key, p); }).ToArray();
        foreach (var (key, value) in values) store.Save(key, value);
    }
}
