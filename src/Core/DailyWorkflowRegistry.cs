using System.Text.Json.Nodes;
using static BD2Daily.DailyData;
namespace BD2Daily;

public sealed class DailyWorkflowStage(Func<JsonObject, Func<string, string?, JsonObject?, Task<JsonObject>>, DailyWorkflow> workflow, Func<DailyWorkflow, Task<JsonObject>> execute, Func<DailyStageFrame, bool>? resume = null) : IDailyManagedStage
{
    public bool CanResume(DailyStageFrame frame) => resume?.Invoke(frame) ?? false;
    public Task<JsonObject> ExecuteAsync(JsonObject context, Func<string, string?, JsonObject?, Task<JsonObject>> relay) => execute(workflow(context, relay));
}

/// <summary>The complete daily business registry. No process fallback or alternate controller.</summary>
public sealed class DailyWorkflowRegistry
{
    private readonly Func<string, string, IReadOnlyDictionary<string, string[]>, Task<DailyRuleData>>? prepareTables;
    private DailyRuleData? rules; private readonly Func<Task<DailyRuleData>>? prepareRules;
    private readonly IDailyExtension? extension; private readonly string root, directory; private readonly DailyCommandDriver driver; private readonly DailyManagedBusiness business; private readonly DailyStageNavigation navigation; private readonly DailyBusinessProof[] proofs; private readonly Func<bool> stopped; private readonly Action<string> report; private readonly Func<JsonObject?> tradeCatalog;
    public DailyWorkflowRegistry(string root, string directory, DailyCommandDriver driver, DailyManagedBusiness business, DailyStageNavigation navigation, DailyBusinessProof[] proofs, Func<bool> stopped, Action<string> report, Func<JsonObject?> tradeCatalog, IDailyExtension? extension = null, Func<Task<DailyRuleData>>? prepareRules = null, Func<string, string, IReadOnlyDictionary<string, string[]>, Task<DailyRuleData>>? prepareTables = null)
    {
        this.prepareTables = prepareTables;
        this.prepareRules = prepareRules;
        this.extension = extension;
        this.root = root;
        this.directory = directory;
        this.driver = driver;
        this.business = business;
        this.navigation = navigation;
        this.proofs = proofs;
        this.stopped = stopped;
        this.report = report;
        this.tradeCatalog = tradeCatalog;
    }
    public DailyWorkflow Create(JsonObject context, Func<string, string?, JsonObject?, Task<JsonObject>> relay) => new(root, directory, context, driver, business, navigation, proofs, stopped, relay, report, rules, prepareTables);
    public async Task PrepareAsync(string stage)
    {
        if (prepareRules == null || new[] { "guild", "room", "mail", "free_draws", "friendship", "trade", "collection_sync" }.Contains(stage))
            return;
        rules ??= await prepareRules();
        rules.AssertReady();
    }
    public static DailyBusinessProof[] Proofs(IDailyExtension? extension = null)
    {
        var result = new List<DailyBusinessProof> { DailyFreeDrawProof.Definition(), DailyFriendshipProof.Definition(), DailyHunting.Proof(), DailyMonsterHunt.Proof(), DailyMirror.Proof(), DailyWeeklyBook.Proof(), DailyWeeklyRooms.Proof(), DailyEventSweep.Definition() };
        result.AddRange(DailyManagementProof.Definitions());
        result.AddRange(DailyDispatch.Proofs());
        result.AddRange(DailySquare.Proofs());
        result.AddRange(DailyCafeteria.Proofs());
        result.AddRange(DailyEquipment.Proofs());
        result.AddRange(DailyMissions.Proofs());
        result.AddRange(DailyEventRewards.Proofs());
        result.AddRange(DailyMiniGames.Proofs());
        result.AddRange(DailyFieldRoute.Proofs());
        result.AddRange(DailyTradeProof.Proofs());
        int talent = result.FindIndex(p => p.Role == "dispatch.start");
        var old = result[talent];
        result[talent] = old with
        {
            Verify = (op, events, after) => S(op["scope"]?["_proof"]?["kind"]) == "bargain" ? DailyTradeProof.Verify(op, events, after) : DailyFieldRoute.VerifyTalent(op, events, after),
            ScopePrefixes = scope => S(scope["_proof"]?["kind"]) == "bargain" ? ["trade", "dispatch"] : old.Prefixes,
            ScopeRoles = scope => S(scope["_proof"]?["kind"]) == "bargain" ? ["dispatch.start"] : old.EventRoles,
            ScopeStages = scope => S(scope["_proof"]?["kind"]) == "bargain" ? ["trade"] : old.Stages
        };
        for (int i = 0; i < result.Count; i++)
        {
            var proof = result[i];
            if (proof.Role == "mirror.end")
                result[i] = proof with
                {
                    CanResume = (op, f) => Owned(op, f) && DailyNavigationDecision.Phase(f.Frame, "mirror").StartsWith("owned_", StringComparison.Ordinal)
                };
            if (proof.Role == "weekly.book.start")
                result[i] = proof with
                {
                    CanResume = (op, f) => Owned(op, f) && DailyNavigationDecision.Phase(f.Frame, "weekly_book").StartsWith("owned_", StringComparison.Ordinal)
                };
            if (proof.Role == "rewards.dice")
                result[i] = proof with
                {
                    CanResume = (op, f) => Owned(op, f) && DailyNavigationDecision.Types(f.Frame).Contains("EventUI")
                };
        }
        return extension?.ExtendProofs(result.ToArray()) ?? result.ToArray();
    }
    private IDailyExtension? ExtensionFor(string stage) => DailyPlugin.Current.Supports(stage) ? extension : null;
    public Dictionary<string, IDailyManagedStage> Stages()
    {
        var map = new Dictionary<string, IDailyManagedStage>(StringComparer.Ordinal);
        void Add(string name, Func<DailyWorkflow, Task<JsonObject>> run, Func<DailyStageFrame, bool>? resume = null) => map.Add(name, new DailyWorkflowStage(Create, run, resume));
        foreach (string name in new[] { "hunting", "daily_hunt", "stones", "daily_hunt_minimal" })
            Add(name, w => DailyHunting.Run(w, name));
        Add("monster_hunt", DailyMonsterHunt.Run);
        Add("mirror", DailyMirror.Run, MirrorOwner);
        Add("daily_dispatch", DailyDispatch.Run);
        foreach (string name in new[] { "square", "goddess", "square_ranking" })
            Add(name, w => DailySquare.Run(w, name));
        Add("cafeteria_guests", DailyCafeteria.Run);
        foreach (string name in new[] { "equipment", "equipment_recycle", "equipment_refine", "weekly_equipment" })
            Add(name, w => DailyEquipment.Run(w, name));
        foreach (string name in new[] { "rewards", "daily_rewards", "weekly_rewards", "mission_rewards" })
            Add(name, w => DailyMissions.Run(w, name));
        Add("pass_rewards", w => DailyPasses.Run(w));
        Add("event_rewards", DailyEventRewards.Run, f => DiceOwner(f) || DailyEventRewards.HasPendingPresentation(business, f) || business.Records(f.Context).Any(op => S(op["role"]).StartsWith("event_rewards.", StringComparison.Ordinal) && S(op["state"]) == "preview_ready" && DailyEventRewards.OwnsPreview(op, f)));
        Add("weekly_book", DailyWeeklyBook.Run, BookOwner);
        Add("weekly_room_likes", DailyWeeklyRooms.Run);
        Add("weekly_fishing", DailyWeeklyFishing.Run);
        Add("weekly_sichuan", DailyWeeklySichuan.Run);
        Add("trade", w => new DailyTradeExecution(w, tradeCatalog() ?? throw new StageHostException("adapter", "跑商数据没有通过当前客户端核对。")).Run(), TradeOwner);
        Add("collection_sync", w => new DailyWeeklyRoute(w).SyncProgress());
        Add("tactics", w => ExtensionFor("tactics")?.ExecuteAsync("tactics", w) ?? Task.FromResult(DailyWorkflow.Skipped("plugin_unavailable")), f => ExtensionFor("tactics")?.CanResume(root, "tactics", f) ?? false);
        Add("event_battle", w => ExtensionFor("event_battle")?.ExecuteAsync("event_battle", w) ?? DailyEventSweep.PublicRun(w), f => ExtensionFor("event_battle")?.CanResume(root, "event_battle", f) ?? false);
        return map;
    }
    public static bool Owned(JsonObject op, DailyStageFrame frame) => JsonNode.DeepEquals(op["cycle"], frame.Context["cycle"]) && DailyEvidence.SameActor(op["before"]!["Frame"]!.AsObject(), frame.Frame);
    private bool MirrorOwner(DailyStageFrame f) => DailyNavigationDecision.Phase(f.Frame, "mirror").StartsWith("owned_", StringComparison.Ordinal) && business.Records(f.Context, "mirror.end").Any(op => Owned(op, f) && S(op["state"]) is "dispatching" or "unknown" or "completed");
    private bool BookOwner(DailyStageFrame f) => DailyNavigationDecision.Phase(f.Frame, "weekly_book").StartsWith("owned_", StringComparison.Ordinal) && business.Records(f.Context, "weekly.book.start").Any(op => Owned(op, f) && S(op["state"]) is "dispatching" or "unknown" or "completed");
    private bool DiceOwner(DailyStageFrame f) => business.Records(f.Context, "rewards.dice").Any(op => Owned(op, f) && DailyManagedBusiness.Pending(op)) && DailyNavigationDecision.Types(f.Frame).Contains("EventUI");
    private bool TradeOwner(DailyStageFrame f) => DailyNavigationDecision.Types(f.Frame).Overlaps(new[] { "ShopUI", "ShopPopupUI", "BuyFavoritePopupUI", "CookingUI", "CookingSelectUI", "DiscountPopupUI" });
}



