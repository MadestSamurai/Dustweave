using Dustweave;
using System.Text.Json;
using System.Text.Json.Nodes;
static class ManagementStageCases
{
    static Task<JsonObject> NoRelay(string op, string? stage, JsonObject? args) => throw new Exception("Managed settlement called Python: " + op);
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool value, string name)
        {
            if (!value)
                throw new Exception(name);
            cases.Add(name);
        }
        async Task Reject(Func<Task> action, string kind, string name)
        {
            string actual = "";
            try
            {
                await action();
            }
            catch (StageHostException e) { actual = e.Kind; }
            Check(actual == kind, name + " (" + actual + ")");
        }
        int number = 0;
        Fixture Create(string kind = "cafeteria_income") => new(Path.Combine(output, "management-" + (++number)), kind);
        using (var f = Create())
        {
            var result = await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(result["state"]!.GetValue<string>() == "completed" && f.Claims == 1 && result["result"]!["categories"]!.AsObject().Count == 3, "managed all-settlement verifies all three eligible native categories from one input");
            Check(f.RewardBacks == 1 && !f.EarlyBack && DailyNavigationDecision.Types(f.Current).Contains("MenuUI"), "managed all-settlement waits for native reward readiness and returns to menu");
            Check(f.Records().Single()["roles"]!.AsArray().Count == 3 && f.Records().Single()["state"]!.GetValue<string>() == "completed", "managed all-settlement journals every native observer role under one confirmed transaction");
            await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(f.Claims == 1, "empty accrued management cache does not repeat native settlement");
            var op = f.Records().Single();
            var before = op["before"]!.AsObject();
            var events = op["events"]!.AsArray();
            var after = op["after"]!.AsObject();
            void Bad(Action<JsonObject, JsonArray, JsonObject> mutate, string name)
            {
                var b = before.DeepClone().AsObject();
                var es = events.DeepClone().AsArray();
                var a = after.DeepClone().AsObject();
                mutate(b, es, a);
                bool rejected = false;
                try
                {
                    DailyManagementProof.Verify(b, es, a);
                }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, name);
            }
            void ReadValue(JsonObject e, string id, string path, object value)
            {
                e["Readings"]!.AsArray().Single(r => r!["Id"]!.GetValue<string>() == id)!["Values"]!.AsArray().Single(v => v!["Path"]!.GetValue<string>() == path)!["Json"] = JsonSerializer.Serialize(value);
            }
            foreach (string role in new[] { DailyManagementProof.Cafeteria, DailyManagementProof.Fishing, DailyManagementProof.Helpers })
            {
                Bad((b, e, a) => { var response = e.Single(r => r!["Role"]!.GetValue<string>() == role && r["Kind"]!.GetValue<string>() == "response"); e.Remove(response); }, "managed settlement requires the " + role + " response");
                Bad((b, e, a) => e.Single(r => r!["Role"]!.GetValue<string>() == role && r["Kind"]!.GetValue<string>() == "response")!["Accepted"] = false, "managed settlement rejects server refusal for " + role);
            }
            Bad((b, e, a) => ReadValue(a, "cafeteria.cache", "RewardReceiptTime", "100"), "managed cafeteria receipt must match the updated cache");
            Bad((b, e, a) => ReadValue(b, "cafeteria.cache", "RewardReceiptTime", "200"), "managed cafeteria reward time must advance");
            Bad((b, e, a) => ReadValue(a, "fishing.player", "Exp", 1), "managed fishing reward must match native player experience");
            Bad((b, e, a) => ReadValue(a, "fishing.ui", "IsCanSettlement()", true), "managed fishing trap must clear native settlement eligibility");
            Bad((b, e, a) => ReadValue(b, "fishing.trap", "TrapRewardReceiptTime", "200"), "managed fishing trap receipt time must advance");
            Bad((b, e, a) => ReadValue(a, "life.helpers_pending", "Count", 1), "managed helper reward queue must empty after the accepted response");
            Bad((b, e, a) => ReadValue(a, "life.helpers_cache", "Count", 2), "managed helper cache requires its complete advertised slot count");
            Bad((b, e, a) => ReadValue(b, "life.helpers_ui", "IsCanSettlement()", false), "managed all-settlement rejects a helper response when it was ineligible");
            foreach (string key in new[] { "helperIndex", "helperId", "workType", "workId" })
                Bad((b, e, a) => { var row = f.OldHelper(); row[key] = 99; ReadValue(b, "life.helpers_cache", "Values", new[] { row }); }, "managed helper settlement never changes " + key);
            Bad((b, e, a) => ReadValue(b, "life.helpers_cache", "Values", new[] { f.NewHelper() }), "managed helper accrual time must advance");
            Bad((b, e, a) => a["Frame"]!["PlayerKey"] = "other", "managed settlement rejects response/player identity loss");
            Bad((b, e, a) => a["Config"] = "new-config", "managed settlement requires a stable evidence definition");
            op["state"] = "unknown";
            f.Business.Save(op);
            int inputs = f.Box.Commands.Count;
            var recovery = await f.Business.ReconcileAsync(f.Context);
            Check(recovery["completed"]!.AsArray().Count == 1 && f.Box.Commands.Count == inputs, "managed three-category settlement can be reconciled from original proof with zero inputs");
        }
        foreach (string role in new[] { DailyManagementProof.Cafeteria, DailyManagementProof.Fishing, DailyManagementProof.Helpers })
            using (var f = Create())
            {
                foreach (string key in f.Eligible.Keys.ToArray())
                    f.Eligible[key] = key == role;
                var result = await f.Stage.ExecuteAsync(f.Context, NoRelay);
                Check(result["result"]!["categories"]!.AsObject().Count == 1 && f.Claims == 1, "managed settlement handles only eligible " + role + " without expecting other responses");
            }
        using (var f = Create())
        {
            foreach (string key in f.Eligible.Keys.ToArray())
                f.Eligible[key] = false;
            var result = await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(result["state"]!.GetValue<string>() == "skipped" && f.Claims == 0 && f.Records().Count == 0, "empty management settlement skips without a consuming intent");
        }
        using (var f = Create("life_helpers"))
        {
            var result = await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(result["state"]!.GetValue<string>() == "completed" && f.Claims == 1 && f.Records().Single()["role"]!.GetValue<string>() == DailyManagementProof.Helpers, "managed helper-only stage consumes only native accrued helper rewards");
            Check(f.Eligible[DailyManagementProof.Cafeteria] && f.Eligible[DailyManagementProof.Fishing], "helper-only settlement leaves cafeteria and fishing rewards untouched");
        }
        using (var f = Create())
        {
            f.MissingResponse = DailyManagementProof.Fishing;
            var result = await f.Stage.ExecuteAsync(f.Context, NoRelay);
            var journal = f.Records().Single();
            Check(result["state"]!.GetValue<string>() == "completed" && journal["confirmation"]!.GetValue<string>() == "native_claim_state", "free management claim accepts native eligibility clearing without a full receipt");
            Check(journal["receipt_diagnostic"] != null && journal["result"]!["receipt_verified"]!.GetValue<bool>() == false, "missing receipt remains diagnostic rather than fabricated server proof");
            await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(f.Claims == 1, "free management rerun reads empty game state without repeating claim");
            journal["state"] = "unknown"; f.Business.Save(journal);
            var recovery = await f.Business.ReconcileAsync(f.Context);
            Check(recovery["unresolved"]!.AsArray().Count == 0 && recovery["free_claim_diagnostics"]!.AsArray().Count == 1, "old incomplete free receipt cannot block queue reconciliation");
        }
        using (var f = Create())
        {
            f.FailReward = true;
            await Reject(() => f.Stage.ExecuteAsync(f.Context, NoRelay), "adapter", "management animation timeout preserves confirmed business result");
            Check(f.Records().Single()["state"]!.GetValue<string>() == "completed" && f.Claims == 1, "stuck management reward presentation retains its server/cache proof");
            f.FailReward = false;
            await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(f.Claims == 1 && f.RewardBacks == 1, "managed reward cleanup resumes without repeating the all-settlement");
        }
        using (var f = Create())
        {
            f.NoTap = true;
            var result = await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(f.Claims == 1 && result["state"]!.GetValue<string>() == "completed", "zero-cost settlement does not require network tap availability");
        }
        using (var f = Create())
        {
            f.Page("ManagementRewardPopupUI");
            f.Reward = true;
            f.Page("ManagementRewardPopupUI");
            foreach (string role in f.Eligible.Keys.ToArray()) f.Eligible[role] = false;
            Check(f.Stage.CanResume(new(f.Current, f.Context)), "known management reward can resume without a historical receipt");
            var result = await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(result["state"]?.GetValue<string>() == "skipped" && f.RewardBacks == 1 && f.Claims == 0,
                "management closes visible prior reward then skips native cooldown without claiming again");
        }
        using (var f = Create())
        {
            f.DuplicateEntry = true;
            f.Page("MenuUI");
            await Reject(() => f.Stage.ExecuteAsync(f.Context, NoRelay), "adapter", "ambiguous native management entry is rejected");
            Check(f.Box.Commands.Count == 0, "managed entry never guesses between two observed menu targets");
        }
        using (var f = Create())
        {
            var op = new JsonObject { ["id"] = Guid.NewGuid().ToString("N"), ["role"] = DailyManagementProof.Helpers, ["state"] = "unknown", ["account"] = f.Context["actor"]![3]!.DeepClone(), ["player"] = f.Context["actor"]![4]!.DeepClone(), ["server"] = f.Context["server"]!.DeepClone(), ["cycle"] = "yesterday" };
            DailyJson.Write(Path.Combine(f.Root, "live", "business", op["id"]!.GetValue<string>() + ".json"), op);
            await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(f.Claims == 1 && File.Exists(Path.Combine(f.Root, "live", "business", op["id"]!.GetValue<string>() + ".json")), "old uncertain free claim does not prevent fresh native claim and remains preserved");
        }
        foreach (string scenario in new[] { "rotate", "permanent-rotation", "account-change", "scene-change", "scene-at-dispatch", "stop", "unknown" })
        using (var f = Create())
        {
            const string prefix = "$pointer/UIRoot/Mask/Object- Left/ButtonLayout/Management/ScrollRect/Viewport/";
            f.Current["Surfaces"]![0]!["Targets"]![0]!["Field"] = prefix + "Content1";
            int selections = 0;
            if (scenario == "unknown")
            {
                var normal = f.Box.AfterCommand;
                f.Box.AfterCommand = command => { normal!(command); f.Box.State = "observed_after_dispatch"; };
            }
            f.Driver.SubmissionGuard = () =>
            {
                if (f.Current["Surfaces"]![0]!["Type"]?.GetValue<string>() != "MenuUI") return;
                if (++selections > 1 && scenario != "permanent-rotation")
                {
                    if (scenario == "scene-at-dispatch") f.Current["Scene"] = "other";
                    return;
                }
                var target = f.Current["Surfaces"]![0]!["Targets"]![0]!;
                target["Field"] = prefix + (selections % 2 == 1 ? "Content2" : "Content1");
                target["Id"] = 10 + selections;
                if (scenario == "account-change") f.Context["actor"]![3] = new string('d', 64);
                if (scenario == "scene-change") f.Current["Scene"] = "other";
                if (scenario == "stop") f.Stopped = true;
                if (scenario == "unknown") { f.Box.State = "unknown"; f.Box.Dispatched = true; }
            };
            Exception? failure = null;
            JsonObject? result = null;
            try { result = await f.Stage.ExecuteAsync(f.Context, NoRelay); }
            catch (Exception e) when (e is StageHostException or DailyStepException) { failure = e; }
            bool ok = scenario switch
            {
                "rotate" => failure == null && f.Claims == 1 && result?["state"]?.GetValue<string>() == "completed",
                "permanent-rotation" => failure is StageHostException { Kind: "adapter" } && f.Driver.MonotonicTime >= 100 && f.Driver.MonotonicTime < 105 && f.Box.Commands.Count == 0,
                "account-change" => failure is DailyStepException { Kind: "identity" } && f.Claims == 0,
                "scene-change" or "scene-at-dispatch" => failure != null && f.Claims == 0 && f.Box.Commands.Count == 0,
                "stop" => failure != null && f.Claims == 0 && f.Box.Commands.Count == 0,
                _ => failure == null && result?["state"]?.GetValue<string>() == "completed" && f.Claims == 1
            };
            Check(ok, "management entry reselect handles " + scenario + " without duplicate claims: " + failure?.Message);
        }
        foreach (int ignored in new[] { 1, 3, 4 })
        using (var f = Create())
        {
            f.IgnoredClaims = ignored;
            var result = await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(result["state"]?.GetValue<string>() == (ignored < 4 ? "completed" : "partial") &&
                f.Claims == Math.Min(ignored + 1, 4) && result["retries"]?.GetValue<int>() == Math.Min(ignored, 3),
                "management retries a still-claimable free income at most three times: " + ignored);
            Check(f.Records().Count == f.Claims && f.Records().Select(op => op["id"]!.GetValue<string>()).Distinct().Count() == f.Claims,
                "each free-income retry uses a fresh game-state plan and preserves prior attempts: " + ignored);
        }
        using (var f = Create("life_helpers"))
        {
            f.IgnoredClaims = 2;
            var result = await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(result["state"]?.GetValue<string>() == "completed" && f.Claims == 3 && result["retries"]?.GetValue<int>() == 2,
                "helper-only zero-cost claims share bounded current-state retry");
            Check(f.Eligible[DailyManagementProof.Cafeteria] && f.Eligible[DailyManagementProof.Fishing],
                "helper-only retries never claim other management categories");
        }
        using (var f = Create())
        {
            f.HoldEligibilityUntilClose = true;
            var result = await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(result["state"]?.GetValue<string>() == "skipped" && f.Claims == 1 && f.RewardBacks == 1 && result["retries"]?.GetValue<int>() == 1,
                "visible management reward cleans up an unconfirmed attempt before live cooldown prevents a second claim");
            Check(f.Records().Single()["state"]?.GetValue<string>() == "unconfirmed_free_claim",
                "presentation recovery does not fabricate a missing server receipt");
        }
        using (var f = Create())
        {
            f.Page("ManagementRewardPopupUI"); f.Reward = true; f.Page("ManagementRewardPopupUI");
            f.Current["Surfaces"]!.AsArray().Add(new JsonObject { ["Type"] = "UnknownPurchaseUI", ["Popup"] = true, ["Order"] = 999, ["Id"] = 9 });
            Check(!f.Stage.CanResume(new(f.Current, f.Context)), "covered management reward is not treated as an ordinary result window");
            await Reject(() => f.Stage.ExecuteAsync(f.Context, NoRelay), "adapter", "unknown foreground is never confirmed during management retries");
            Check(f.Box.Commands.Count == 0, "management retries do not dismiss unrelated dialogs");
        }
        using (var f = Create())
        {
            var native = f.Box.AfterCommand;
            f.Box.AfterCommand = command =>
            {
                native!(command);
                if (command["TargetId"]?.GetValue<int>() == 31)
                    f.Box.Values.Remove("live:receipts~" + command["Id"]!.GetValue<string>() + ".json");
            };
            var result = await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(result["state"]?.GetValue<string>() == "completed" && f.Claims == 1 && result["retries"]?.GetValue<int>() == 0,
                "lost transport receipt with successful native claim and reward does not trigger a second claim");
        }
        foreach (bool reversed in new[] { false, true })
        using (var f = Create())
        {
            var first = f.Current["Surfaces"]![0]!["Targets"]![0]!.DeepClone().AsObject();
            first["Field"] = "$pointer/UIRoot/Mask/Object- Left/ButtonLayout/Management/ScrollRect/Viewport/Content1";
            var second = first.DeepClone().AsObject(); second["Id"] = 12; second["Field"] = "$pointer/UIRoot/Mask/Object- Left/ButtonLayout/Management/ScrollRect/Viewport/Content2";
            f.Current["Surfaces"]![0]!["Targets"] = reversed ? new JsonArray(second, first) : new JsonArray(first, second);
            var result = await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(f.Claims == 1 && result["state"]!.GetValue<string>() == "completed", "rotating settlement banners share a single native entry regardless of observation order");
        }
        using (var f = Create("management"))
        {
            f.Settings.Stages.CafeteriaIncome = true;
            f.Settings.Stages.CafeteriaGuests = false;
            await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(f.Claims == 1, "management aggregate with guests disabled remains entirely managed");
        }
        using (var f = Create("management"))
        {
            f.Settings.Stages.CafeteriaIncome = true;
            f.Settings.Stages.CafeteriaGuests = true;
            int relays = 0;
            async Task<JsonObject> Relay(string op, string? stage, JsonObject? args)
            {
                await Task.CompletedTask;
                if (op != "execute" || stage != "cafeteria_guests")
                    throw new Exception("Unexpected managed guest relay");
                relays++;
                return new()
                {
                    ["state"] = "completed"
                };
            }
            var result = await f.Stage.ExecuteAsync(f.Context, Relay);
            Check(f.Claims == 1 && relays == 1 && result["guests_engine"]!.GetValue<string>() == "dotnet", "management aggregate executes its managed guest stage through the same relay");
        }
        using (var f = Create())
        {
            f.Stopped = true;
            await Reject(() => f.Stage.ExecuteAsync(f.Context, NoRelay), "stopped", "stopped management stage never opens or consumes");
            Check(f.Box.Commands.Count == 0, "stopped management preserves its current scene");
        }
    }
    internal sealed class Fixture : IDisposable
    {
        public string Root; public CommandDriverCases.Mailbox Box = new(); public JsonObject Context = CommandDriverCases.Context(), Current = CommandDriverCases.Frame(); public DailyCommandDriver Driver; public DailyManagedBusiness Business; public DailyManagementStage Stage; public DailyPreferences Settings = new();
        public Dictionary<string, bool> Eligible = DailyManagementProof.Categories.ToDictionary(c => c.Role, c => true); public string MissingResponse = ""; public bool NoTap, FailReward, Stopped, Reward, EarlyBack, DuplicateEntry; public int Claims, RewardBacks, IgnoredClaims; public bool HoldEligibilityUntilClose;
        private double time, readyAt; private long sequence; private bool helpersClaimed, cafeClaimed, fishClaimed;
        private long Ticks => 100000000 + (long)(time * TimeSpan.TicksPerSecond);
        public JsonObject OldHelper() => new() { ["helperSlotId"] = 1, ["helperIndex"] = 10, ["helperId"] = 20, ["workType"] = 2, ["workId"] = 4, ["assignDate"] = "100" };
        public JsonObject NewHelper()
        {
            var row = OldHelper();
            row["assignDate"] = "200";
            return row;
        }
        public Fixture(string root, string kind)
        {
            Root = root;
            Page("MenuUI");
            Box.AfterWrite = (c, n, b) => { if (n == "observation-request.json") Publish(); };
            Box.AfterCommand = command =>
            {
                int surface = command["SurfaceId"]!.GetValue<int>(), target = command["TargetId"]!.GetValue<int>();
                if (surface == 1)
                    Page("ManagementRewardPopupUI");
                else if (surface == 4)
                {
                    RewardBacks++;
                    if (time < readyAt)
                        EarlyBack = true;
                    Reward = false;
                    Page("ManagementRewardPopupUI");
                }
                else if (target == 33)
                    Page("MenuUI");
                else
                {
                    Claims++;
                    if (Claims <= IgnoredClaims) { time += .01; Publish(); return; }
                    long at = Ticks;
                    string[] roles = target == 32 ? [DailyManagementProof.Helpers] : Eligible.Where(p => p.Value).Select(p => p.Key).ToArray();
                    foreach (string role in roles)
                    {
                        var request = new JsonObject { ["Role"] = role, ["Kind"] = "request", ["Sequence"] = ++sequence, ["Frame"] = Current.DeepClone() };
                        Box.Values["live:events~" + at + "-" + sequence + "-fixture.json"] = JsonSerializer.SerializeToUtf8Bytes(request);
                        JsonObject values = role switch
                        {
                            DailyManagementProof.Cafeteria => new() { ["RewardReceiptTime"] = "200", ["CumulativeRewardInfo"] = new JsonObject { ["itemInfo"] = new JsonArray(1) } },
                            DailyManagementProof.Fishing => new() { ["TrapRewardReceiptTime"] = "200", ["Level"] = 10, ["Exp"] = 20, ["RewardInfo"] = new JsonObject { ["itemInfo"] = new JsonArray(1) } },
                            _ => new() { ["HelperInfo"] = new JsonArray(NewHelper()), ["RewardBundle"] = new JsonObject { ["itemInfo"] = new JsonArray(new JsonObject { ["id"] = 10, ["count"] = 4 }) } }
                        };
                        if (role != MissingResponse)
                        {
                            var response = new JsonObject { ["Role"] = role, ["Kind"] = "response", ["Sequence"] = ++sequence, ["Frame"] = Current.DeepClone(), ["Error"] = "", ["ErrorCode"] = 0, ["Accepted"] = true, ["Values"] = new JsonArray(values.Select(p => (JsonNode)new JsonObject { ["Path"] = p.Key, ["Error"] = "", ["Json"] = p.Value!.ToJsonString() }).ToArray()) };
                            Box.Values["live:events~" + (at + 1) + "-" + sequence + "-fixture.json"] = JsonSerializer.SerializeToUtf8Bytes(response);
                        }
                        Eligible[role] = false;
                        if (role == DailyManagementProof.Helpers)
                            helpersClaimed = true;
                        if (role == DailyManagementProof.Cafeteria)
                            cafeClaimed = true;
                        if (role == DailyManagementProof.Fishing)
                            fishClaimed = true;
                    }
                    Reward = true;
                    readyAt = time + .3;
                    Page("ManagementRewardPopupUI");
                }
                time += .01;
                Publish();
            };
            Driver = new(root, Box, () => { Current["AtUtcTicks"] = Ticks; return Task.FromResult(new DailyStageFrame(Current.DeepClone().AsObject(), Context.DeepClone().AsObject())); }, () => Stopped, () => Ticks, () => time, t => { time += t.TotalSeconds; Publish(); return Task.CompletedTask; });
            Driver.Bind(Context);
            Driver.Acquire("live");
            Business = new(root, Driver, DailyManagementProof.Definitions(), () => Stopped, () => time, t => { time += t.TotalSeconds; Publish(); return Task.CompletedTask; });
            Stage = new(root, Driver, Business, () => Stopped, kind, () => time, t => { time += t.TotalSeconds; Publish(); return Task.CompletedTask; }, context => Settings);
        }
        public void Page(string name)
        {
            Current = CommandDriverCases.Frame();
            Current["AtUtcTicks"] = Ticks;
            var surface = Current["Surfaces"]![0]!;
            surface["Id"] = name == "MenuUI" ? 1 : 3;
            surface["Type"] = name;
            surface["InputReady"] = true;
            JsonObject Target(int id, string field) => new()
            {
                ["Id"] = id,
                ["Field"] = field,
                ["Enabled"] = true,
                ["Route"] = field.StartsWith("$pointer") ? "pointer" : "ui"
            };
            surface["Targets"] = name == "MenuUI" ? new JsonArray(Target(11, "$pointer/UIRoot/Mask/Object- Left/ButtonLayout/Management/Button")) : new JsonArray(Target(31, "_settlementAllButton"), Target(32, "$pointer/Button - background/Parent/Image - Backgrond/AvatarLifeSettlementInfo/EnableRoot/Reward/RewardItemsObject/Button - Settlement"), Target(33, "_cancelButton"));
            if (DuplicateEntry && name == "MenuUI")
                surface["Targets"]!.AsArray().Add(Target(12, "$pointer/UIRoot/Mask/Object- Left/ButtonLayout/Management/Other"));
            if (Reward)
                Current["Surfaces"]!.AsArray().Add(new JsonObject { ["Type"] = "RewardReceivePopupUI", ["Id"] = 4, ["Popup"] = true, ["Order"] = 10, ["InputReady"] = true, ["Targets"] = new JsonArray() });
        }
        private void Publish()
        {
            var bytes = Box.Values.GetValueOrDefault("live:observation-request.json");
            if (bytes == null)
                return;
            var request = JsonNode.Parse(bytes)!;
            Current["AtUtcTicks"] = Ticks;
            JsonObject Reading(string id, params (string Path, object Value)[] values) => new()
            {
                ["Id"] = id,
                ["Error"] = "",
                ["Values"] = new JsonArray(values.Select(v => (JsonNode)new JsonObject { ["Path"] = v.Path, ["Error"] = "", ["Json"] = JsonSerializer.Serialize(v.Value) }).ToArray())
            };
            bool ready = !FailReward && time >= readyAt;
            var readings = new JsonArray();
            foreach (var (role, ui) in DailyManagementProof.Categories)
                readings.Add(Reading(ui, ("IsCanSettlement()", Eligible[role] || (HoldEligibilityUntilClose && Reward)), ("IsDisable()", false)));
            readings.Add(Reading("cafeteria.cache", ("RewardReceiptTime", cafeClaimed ? "200" : "100")));
            readings.Add(Reading("fishing.trap", ("TrapRewardReceiptTime", fishClaimed ? "200" : "100")));
            readings.Add(Reading("fishing.player", ("Level", 10), ("Exp", fishClaimed ? 20 : 10)));
            readings.Add(Reading("life.helpers_cache", ("Count", 1), ("Values", new[] { helpersClaimed ? NewHelper() : OldHelper() })));
            readings.Add(Reading("life.helpers_pending", ("Count", Eligible[DailyManagementProof.Helpers] ? 1 : 0)));
            readings.Add(Reading("management.ui", ("_settlementAllButtonEnableRoot.activeInHierarchy", Eligible.Any(p => p.Value))));
            readings.Add(Reading("reward.presentation", ("ὣὤὥὦὯὦὩὤὨὠὪ", ready), ("ὪὯὣὥὬὫὬὩὠὮὠ", ready), ("ὮὬὧὦὣὠὠὤὮὦὧ", ready), ("ὦὡὮὫὧὠὮὡὭὭὬ", !ready)));
            var evidence = new JsonObject { ["Config"] = "management-fixture", ["ObservationRequest"] = request["Id"]!.DeepClone(), ["Frame"] = Current.DeepClone(), ["AtUtcTicks"] = Ticks, ["Error"] = "", ["Taps"] = NoTap ? new JsonArray() : new JsonArray(DailyManagementProof.Categories.Select(c => (JsonNode)JsonValue.Create(c.Role)!).ToArray()), ["Readings"] = readings };
            Box.Values["live:evidence.json"] = JsonSerializer.SerializeToUtf8Bytes(evidence);
        }
        public List<JsonObject> Records() => Business.Records(Context, includeLegacy: false).ToList(); public void Dispose() => Driver.Dispose();
    }
}
