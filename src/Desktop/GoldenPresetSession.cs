extern alias liveUtility;
using System.IO;
using System.Text.Json.Nodes;
using Dustweave.Golden;
using static Dustweave.DailyData;
namespace Dustweave.Desktop;

internal sealed record GoldenSlot(int Slot, string Name, string Code, string Backup, string Error)
{
    public string Label => $"{Slot + 1} · {(string.IsNullOrEmpty(Name) ? DailyLanguage.Current.Get("goldencode.empty_slot") : Name)}";
}
internal sealed record GoldenSnapshot(long Revision, string Account, string Status, FormationRules Rules, int[] Owned, GoldenSlot[] Slots, IReadOnlyDictionary<int, DailyDisplayName> Names)
{
    public bool ViewSynchronized { get; init; }
    public string ViewError { get; init; } = "";
}
internal sealed class GoldenPresetSession(string root, string account)
{
    public Action<string> Report { get; set; } = _ => { };
    volatile bool stopped;
    CancellationTokenSource? connectionCancel;
    public void Stop() { stopped = true; connectionCancel?.Cancel(); }
    public async Task<GoldenSnapshot> Read(string code = "") => await Execute(false, code, 0, null);
    public async Task<GoldenSnapshot> Save(string code, int slot, GoldenSnapshot approved) => await Execute(true, code, slot, approved);
    static JsonObject StateOf(JsonObject e) => State(e, "golden.presets", "$self");
    async Task<GoldenSnapshot> Execute(bool save, string code, int slot, GoldenSnapshot? approved)
    {
        stopped = false;
        using var lease = DailyToolControl.Acquire(root);
        var host = new DailyGameHost(root); var game = host.Find() ?? throw new InvalidOperationException("goldencode.open_game");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        connectionCancel = cancel;
        try
        {
            Report("goldencode.connecting");
            using var accounts = new AccountSessions();
            await CurrentGameObservation.ConnectAsync(accounts, host, _ => { }, cancel.Token);
            var identity = host.ReadSnapshot();
            if (host.Find() != game || identity?.AccountKey != account || !DailyIdentityGuard.Ready(identity, game, DateTimeOffset.UtcNow))
                throw new InvalidOperationException("goldencode.account_changed");
            Report("goldencode.preparing");
            await DailySuite.ActivateAsync(host, root, "daily", _ => { }, cancel.Token);
            await liveUtility::Dustweave.Connection.LiveEntry.RunAsync(["ready"], false,
                args => liveUtility::Dustweave.Connection.LiveEntry.RunAsync(args, false), "golden-presets-evidence-spec.json");
            cancel.Token.ThrowIfCancellationRequested();
            if (host.Find() != game) throw new InvalidOperationException("goldencode.account_changed");
            var observation = DailyStageObservation.Attach(root, account, () => stopped);
            var first = await observation.ReadFrameAsync();
            using var driver = new DailyCommandDriver(root, new DailyPipeMailbox(root, game), observation.ReadFrameAsync, () => stopped);
            driver.EnsureBound(first.Context);
            var proof = new DailyBusinessProof("golden.presets", "golden-presets", ["golden.presets"], (_, _, _) => throw new NotSupportedException());
            using var business = new DailyManagedBusiness(root, driver, [proof], () => stopped);
            var w = new DailyWorkflow(root, AppContext.BaseDirectory, first.Context, driver, business,
                new DailyStageNavigation(observation.ReadFrameAsync, () => stopped), [proof], () => stopped,
                (operation, _, request) => operation == "navigation_step" ? driver.NavigationAsync(request!["action"]!.AsObject()) : throw new NotSupportedException());
            try
            {
                await new DailyManagedBootstrap(root, driver, observation.ReadFrameAsync, () => stopped).BeginAsync(first.Context);
                Report(save ? "goldencode.saving" : "goldencode.reading");
                var e = await w.Evidence("golden.presets"); var state = StateOf(e);
                if (S(state["Status"]) is "reading" or "saving")
                    e = await w.WaitEvidence(["golden.presets"], v => S(StateOf(v)["Status"]) is not ("reading" or "saving"), 80, "goldencode.read_again");
                long revision = N(StateOf(e)["Revision"]);
                if (save)
                {
                    if (approved == null || approved.Account != account || approved.Revision != revision) throw new InvalidOperationException("goldencode.read_again");
                    FormationImport.Validate(FormationCode.Decode(code), approved.Rules, approved.Owned, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    // Persist the exact selected native slot before dispatch. This is private account data, never part of a share code.
                    DailyJson.Write(Path.Combine(root, "tools", "golden-presets", "backups", account, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-slot{slot + 1}.json"), approved.Slots.Single(s => s.Slot == slot));
                }
                var frame = (await w.Observe()).Frame;
                var ui = DailyNavigationDecision.Rows(frame).Where(r => S(r["Type"]) == "MenuUI" || S(r["Type"]) == "PVPColosseumLobbyUI" || S(r["Type"]) == "CharPresetPopupUI")
                    .OrderByDescending(r => N(r["Order"])).FirstOrDefault();
                if (ui == null) throw new InvalidOperationException("goldencode.open_lobby");
                var items = new JsonArray(); if (save || code.Length > 0) { items.Add(save ? revision : 0); foreach (char c in code) items.Add((long)c); }
                await w.Step(O(("ui", S(ui["Type"])), ("operation", save ? "golden_preset_save" : "golden_preset_read"), ("value", slot), ("items", items)));
                var result = await w.WaitEvidence(["golden.presets"], v => N(StateOf(v)["Revision"]) > revision && S(StateOf(v)["Status"]) is "ready" or "saved" or "failed" or "uncertain", 80, "goldencode.read_again");
                Identity(e, result); var final = StateOf(result);
                if (S(final["Status"]) is "failed" or "uncertain") throw new InvalidOperationException(S(final["Status"]) == "uncertain" ? "goldencode.uncertain" : S(final["Error"]),
                    new IOException(S(final["Error"]) + ": " + S(final["Detail"])));
                var parsed = Parse(final);
                try { DailyJson.Write(Path.Combine(root, "tools", "golden-presets", "logs", "last-result.json"), new {
                    atUtc = DateTimeOffset.UtcNow, account, save, slot, status = parsed.Status,
                    revision = parsed.Revision, viewSynchronized = parsed.ViewSynchronized, viewError = parsed.ViewError,
                    nameCount = parsed.Names.Count, nameError = S(final["NameError"]), slots = parsed.Slots.Select(s => new { s.Slot, s.Name, s.Error }) }); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                return parsed;
            }
            finally
            {
                if (driver.HasControl) try { await driver.HandleAsync(O(("operation", "write"), ("channel", "live"), ("name", "pause"), ("value", ""))); }
                    catch (Exception e) when (e is IOException or StageHostException or TimeoutException) { }
            }
        }
        catch (Exception error)
        {
            try { DailyJson.Write(Path.Combine(root, "tools", "golden-presets", "logs", $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json"), new { atUtc = DateTimeOffset.UtcNow, account, save, slot, error = error.ToString() }); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
        finally { connectionCancel = null; }
    }
    internal static GoldenSnapshot Parse(JsonObject state)
    {
        var r = state["Rules"]!.AsObject();
        int[] Ids(JsonNode? n) => (n?.AsArray() ?? []).Select(I).ToArray();
        Dictionary<int, int> Map(JsonNode? n) => Rows(n).ToDictionary(v => I(v["Key"]), v => I(v["Value"]));
        var rules = new FormationRules { Season = I(r["Season"]), Width = I(r["Width"]), Height = I(r["Height"]), MaxUnits = I(r["MaxUnits"]),
            Starts = N(r["Starts"]), Ends = N(r["Ends"]), CostLimit = I(r["CostLimit"]), DefaultCost = I(r["DefaultCost"]), CompleteCosts = B(r["CompleteCosts"]),
            AttackPoints = I(r["AttackPoints"]), DefensePoints = I(r["DefensePoints"]), BannedPositions = Ids(r["BannedPositions"]), BannedCostumes = Ids(r["BannedCostumes"]),
            BannedBlessings = Ids(r["BannedBlessings"]), Costs = Map(r["Costs"]), BlessingCosts = Map(r["BlessingCosts"]) };
        return new(N(state["Revision"]), S(state["Account"]), S(state["Status"]), rules, Ids(state["Owned"]), Rows(state["Slots"]).Select(s => new GoldenSlot(I(s["Slot"]), S(s["Name"]), S(s["Code"]), S(s["Backup"]), S(s["Error"]))).ToArray(), Rows(state["Names"]).ToDictionary(n => I(n["Key"]), n => new DailyDisplayName(S(n["Value"]?["Cn"]), S(n["Value"]?["Tw"]), S(n["Value"]?["En"]))))
        { ViewSynchronized = B(state["ViewSynchronized"]), ViewError = S(state["ViewError"]) };
    }
}
