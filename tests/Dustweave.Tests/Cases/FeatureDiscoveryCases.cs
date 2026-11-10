using Dustweave;

internal static class FeatureDiscoveryCases
{
    public static void Run(string root, List<string> cases)
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); cases.Add("feature-discovery: " + name); }
        string folder = Path.Combine(root, "feature-discovery");
        var first = new DailyFeatureDiscovery(folder);
        string soul = DailyFeatureDiscovery.Tool("soul"), tactics = DailyFeatureDiscovery.Plugin("dustweave.tactics");
        Check(first.IsNew(soul), "Soul announced to existing and fresh users");
        Check(first.IsNew(tactics), "Textbook announced before installation");
        Check(!first.IsNew(DailyFeatureDiscovery.Tool("fishing")), "old catalog is not flooded with badges");
        Check(first.IsNew(DailyFeatureDiscovery.Tool("future-tool")), "future built-in automatically announced without changing migration list");
        Check(first.IsNew(DailyFeatureDiscovery.Plugin("future-plugin")), "future plugin automatically announced");
        Check(DailyFeatureDiscovery.Tool("plugin:future-plugin") == DailyFeatureDiscovery.Plugin("future-plugin"), "plugin tool has one viewed identity across both pages");
        Check(new DailyFeatureDiscovery(folder).IsNew(soul), "enumeration and restart do not acknowledge");
        first.MarkViewed(soul);
        Check(!first.IsNew(soul) && first.IsNew(tactics), "viewing one entry leaves the other unread");
        var second = new DailyFeatureDiscovery(folder);
        Check(!second.IsNew(soul), "viewed state persists across restart and application versions");
        second.MarkViewed(tactics);
        first.MarkViewed(DailyFeatureDiscovery.Tool("future-tool"));
        var merged = new DailyFeatureDiscovery(folder);
        Check(!merged.IsNew(tactics) && !merged.IsNew(soul), "saving another view merges current disk receipts");
        Check(merged.IsNew(DailyFeatureDiscovery.Plugin("future-plugin")), "refreshing catalog does not dismiss new plugin");
        var locked = Path.Combine(root, "feature-discovery-blocked"); File.WriteAllText(locked, "not a directory");
        var failing = new DailyFeatureDiscovery(locked);
        try { failing.MarkViewed(soul); } catch (IOException) { }
        Check(failing.IsNew(soul), "failed persistence does not falsely clear badge");
        File.WriteAllText(Path.Combine(folder, "feature-discovery.json"), "invalid json");
        Check(new DailyFeatureDiscovery(folder).IsNew(soul), "damaged display preferences recover without blocking tools");
        Check(!merged.IsNew("../../anything"), "invalid keys cannot create notices");
    }
}
