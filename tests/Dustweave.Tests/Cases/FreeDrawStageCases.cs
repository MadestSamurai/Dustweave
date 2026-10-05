using BD2Daily;
using System.Text.Json;
using System.Text.Json.Nodes;
static class FreeDrawStageCases
{
    static Task<JsonObject> NoRelay(string op, string? stage, JsonObject? args) => throw new Exception("Managed free draw called Python: " + op);
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool value, string name)
        {
            if (!value)
                throw new Exception(name);
            cases.Add(name);
        }
        async Task Reject(Func<Task> action, string expected, string name)
        {
            string kind = "";
            try
            {
                await action();
            }
            catch (StageHostException e) { kind = e.Kind; }
            Check(kind == expected, name + " (" + kind + ")");
        }
        int number = 0;
        Fixture Create() => new(Path.Combine(output, "free-draw-" + (++number)));
        Check(DailyFreeDrawRules.Load().Projection["free"]!.AsArray().Count > 100, "managed free draw rules load from packaged resources without a Python data loader");
        using (var f = Create())
        {
            var result = await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(result["state"]!.GetValue<string>() == "completed" && f.Confirmations == 2 && f.Previews == 2, "managed free draws confirm costume and equipment dedicated free batches");
            Check(f.Records().Count == 2 && f.Records().All(op => op["state"]!.GetValue<string>() == "completed" && op["presentation_closed"]!.GetValue<bool>()), "managed free draws persist server/cache proof and completed animation cleanup");
            Check(f.Skips == 2 && f.ResultBacks == 2 && !f.EarlyBack && f.PaidCommands == 0, "managed free draw cleanup only skips animation then returns after readiness");
            Check(DailyNavigationDecision.Types(f.Current).Contains("MenuUI"), "managed free draw stages return to the main menu");
            await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(f.Confirmations == 2 && f.Previews == 2, "completed native free counters prevent rerun consumption");
            var op = f.Records().Single(r => r["scope"]!["category"]!.GetValue<string>() == "Costume");
            var before = op["before"]!.AsObject();
            var events = op["events"]!.AsArray();
            var after = op["after"]!.AsObject();
            var rules = new DailyFreeDrawRules(op["rules"]!.AsObject());
            var proof = DailyFreeDrawProof.Verify(before, events, after, rules);
            Check(proof["draws"]!.GetValue<int>() == 1 && proof["groups"]!["5"]!.GetValue<long>() == 1, "managed free draw proof matches the unchanged oracle free counter contract");
            void Bad(Action<JsonObject, JsonArray, JsonObject> mutate, string name)
            {
                var b = before.DeepClone().AsObject();
                var es = events.DeepClone().AsArray();
                var a = after.DeepClone().AsObject();
                mutate(b, es, a);
                bool rejected = false;
                try
                {
                    DailyFreeDrawProof.Verify(b, es, a, rules);
                }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, name);
            }
            void ChangeUsers(JsonObject e, Action<JsonArray> mutate)
            {
                var row = e["Readings"]!.AsArray().Single(r => r!["Id"]!.GetValue<string>() == "gacha.users")!;
                var value = row["Values"]!.AsArray().Single(v => v!["Path"]!.GetValue<string>() == "Values")!;
                var parsed = JsonNode.Parse(value["Json"]!.GetValue<string>())!.AsArray();
                mutate(parsed);
                value["Json"] = parsed.ToJsonString();
            }
            foreach (string key in new[] { "oneCashPickCount", "tenCashPickCount", "totalBuyCount", "tenFreePickCount" })
                Bad((b, e, a) => ChangeUsers(a, rows => rows[0]![key] = 1), "managed free draw proof rejects changed " + key);
            Bad((b, e, a) => ChangeUsers(a, rows => rows[0]!["oneFreePickCount"] = 0), "managed free draw proof requires a matching free counter increment");
            Bad((b, e, a) => ChangeUsers(a, rows => rows[1]!["oneFreePickCount"] = 1), "managed free draw proof rejects unrelated free counter changes");
            Bad((b, e, a) => a["Frame"]!["AccountKey"] = "other", "managed free draw proof rejects post-response account changes");
            Bad((b, e, a) => a["Config"] = "other", "managed free draw proof rejects changed observer definitions");
            Bad((b, e, a) => e[0]!["Sequence"] = 3, "managed free draw proof rejects a response before its request");
            Bad((b, e, a) => e.Add(e[1]!.DeepClone()), "managed free draw proof rejects duplicate consuming responses");
            Bad((b, e, a) => e[1]!["Accepted"] = false, "managed free draw proof rejects server rejection");
            Bad((b, e, a) => e[1]!["Values"]![0]!["Json"] = "[]", "managed free draw proof rejects empty draw results");
            Bad((b, e, a) => e[1]!["Values"]![0]!["Json"] = "[{\"id\":999,\"rewardInfoBundle\":{\"items\":[]}}]", "managed free draw proof rejects a paid or unrecognized draw id");
            Bad((b, e, a) => e[1]!["Values"]![0]!["Json"] = "[{\"id\":50,\"rewardInfoBundle\":{}}]", "managed free draw proof requires nonempty native rewards");
            Bad((b, e, a) => { var reading = a["Readings"]!.AsArray()[0]!; reading["Values"]!.AsArray().Single(v => v!["Path"]!.GetValue<string>() == "Keys")!["Json"] = "[5,5]"; }, "managed free draw proof rejects duplicated cache keys");
            foreach (string state in new[] { "unknown", "dispatching" })
            {
                op["state"] = state;
                f.Business.Save(op);
                int inputs = f.Box.Commands.Count;
                var report = await f.Business.ReconcileAsync(f.Context);
                Check(report["completed"]!.AsArray().Count == 1 && f.Box.Commands.Count == inputs, "managed free draw read-only reconciliation proves " + state + " from original evidence");
            }
            op["state"] = "unknown";
            op["cycle"] = "yesterday";
            f.Business.Save(op);
            int commands = f.Box.Commands.Count;
            var recovered = await f.Business.ReconcileAsync(f.Context);
            Check(recovered["completed"]!.AsArray().Count == 1 && f.Box.Commands.Count == commands, "managed free draw saved proof remains valid across a daily reset without drawing again");
        }
        using (var f = Create())
        {
            f.Counts[5] = f.Counts[6] = 1;
            var result = await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(result["state"]!.GetValue<string>() == "skipped" && f.Confirmations == 0 && f.Records().Count == 0, "empty daily free draws submit no previews or business transactions");
        }
        using (var f = Create())
        {
            f.MissingResponse = true;
            await Reject(() => f.Stage.ExecuteAsync(f.Context, NoRelay), "pending", "free draw missing response remains pending");
            Check(f.Confirmations == 1 && f.Records().Single()["state"]!.GetValue<string>() == "unknown", "free draw response timeout preserves exactly one uncertain consuming operation");
            int count = f.Box.Commands.Count;
            await Reject(() => f.Stage.ExecuteAsync(f.Context, NoRelay), "pending", "uncertain free draw blocks the complete stage");
            Check(f.Box.Commands.Count == count, "uncertain free draw rerun sends zero navigation, previews or consuming inputs");
            var op = f.Records().Single();
            op["cycle"] = "yesterday";
            f.Business.Save(op);
            var report = await f.Business.ReconcileAsync(f.Context);
            Check(report["unresolved"]!.AsArray().Count == 1 && f.Box.Commands.Count == count, "new-day cache cannot resolve a missing original free draw response");
        }
        using (var f = Create())
        {
            f.ChangeIdentity = true;
            await Reject(() => f.Stage.ExecuteAsync(f.Context, NoRelay), "pending", "identity loss after draw confirmation remains pending");
            Check(f.Confirmations == 1 && f.Records().Single()["state"]!.GetValue<string>() == "unknown", "identity loss never makes the consumed draw retryable");
        }
        using (var f = Create())
        {
            f.NoTap = true;
            await Reject(() => f.Stage.ExecuteAsync(f.Context, NoRelay), "adapter", "free draw requires native response observation before preview");
            Check(f.Previews == 0 && f.Confirmations == 0, "missing free draw tap submits no preview or consuming input");
        }
        using (var f = Create())
        {
            f.Page("GachaMainUI");
            f.AddPopup();
            await Reject(() => f.Stage.ExecuteAsync(f.Context, NoRelay), "adapter", "preexisting free draw confirmation is unowned");
            Check(f.Box.Commands.Count == 0, "preexisting free draw confirmation is preserved without generic confirmation");
        }
        using (var f = Create())
        {
            f.Page("GachaResultUI");
            await Reject(() => f.Stage.ExecuteAsync(f.Context, NoRelay), "adapter", "unowned draw result is preserved");
            Check(f.Box.Commands.Count == 0, "unowned draw animation is never skipped or redrawn");
        }
        using (var f = Create())
        {
            f.FailAnimation = true;
            await Reject(() => f.Stage.ExecuteAsync(f.Context, NoRelay), "adapter", "confirmed free draw animation timeout reports cleanup only");
            var op = f.Records().Single();
            Check(op["state"]!.GetValue<string>() == "completed" && f.Confirmations == 1, "stuck draw animation retains completed server reward proof");
            f.FailAnimation = false;
            await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(f.Confirmations == 2 && f.PaidCommands == 0, "confirmed draw cleanup resumes without repeating the first category draw");
        }
        using (var f = Create())
        {
            var op = await f.PreviewRecord();
            Check(f.Stage.CanResume(new(f.Current, f.Context)), "managed free draw can resume its own durable unconsumed preview");
            int inputs = f.Box.Commands.Count;
            var report = await f.Business.ReconcileAsync(f.Context);
            Check(report["unresolved"]![0]!["recover_in_stage"]!.GetValue<bool>() && f.Box.Commands.Count == inputs, "read-only preview reconciliation leaves confirmation to the owning stage");
            await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(f.Confirmations == 2 && f.Previews == 1, "owned preview resume confirms once and completes the remaining category");
        }
        using (var f = Create())
        {
            var op = await f.PreviewRecord();
            f.Current["UiToken"] = "new-popup";
            await Reject(() => f.Stage.ExecuteAsync(f.Context, NoRelay), "pending", "replacement preview popup cannot inherit old ownership");
            Check(f.Confirmations == 0, "replacement preview popup submits zero confirmations");
        }
        using (var f = Create())
        {
            var op = await f.PreviewRecord();
            op["state"] = "unknown_preview";
            f.Business.Save(op);
            var report = await f.Business.ReconcileAsync(f.Context);
            Check(!report["unresolved"]![0]!["recover_in_stage"]!.GetValue<bool>() && f.Box.Commands.Count == 0, "preview dispatch without observed popup proof remains read-only and blocked");
        }
        using (var f = Create())
        {
            var op = await f.PreviewRecord();
            op["state"] = "completed";
            f.Business.Save(op);
            op["state"] = "unknown";
            DailyJson.Write(Path.Combine(f.Root, "live", "business", op["id"]!.GetValue<string>() + ".json"), op);
            f.Page("MenuUI");
            await Reject(() => f.Stage.ExecuteAsync(f.Context, NoRelay), "pending", "legacy uncertain free draw still blocks the managed stage");
            Check(f.Box.Commands.Count == 0, "managed journal separation does not bypass legacy pending consumes");
        }
        using (var f = Create())
        {
            f.Stopped = true;
            await Reject(() => f.Stage.ExecuteAsync(f.Context, NoRelay), "stopped", "stopped free draw stage preserves the current UI");
            Check(f.Box.Commands.Count == 0, "stopped free draw submits zero inputs");
        }
        using (var f = Create())
        {
            f.ResultDelay = 2.5;
            var result = await f.Stage.ExecuteAsync(f.Context, NoRelay);
            Check(result["state"]!.GetValue<string>() == "completed" && f.Confirmations == 2 && f.ResultBacks == 2 && f.Skips == 2,
                "server reward before delayed animation must complete result lifecycle before switching categories");
            Check(f.Records().All(op => op["presentation_seen"]?.GetValue<bool>() == true && op["presentation_closed"]?.GetValue<bool>() == true),
                "each free draw records result appearance before closing presentation");
        }
        using (var f = Create())
        {
            f.NeverShowResult = true;
            await Reject(() => f.Stage.ExecuteAsync(f.Context, NoRelay), "adapter", "unchanged gacha main after server receipt cannot prove result animation ended");
            Check(f.Confirmations == 1 && f.Counts[6] == 0 && f.Records().Single()["state"]!.GetValue<string>() == "completed" && f.Records().Single()["presentation_closed"]?.GetValue<bool>() != true,
                "missing result retains server completion and never advances or redraws");
        }
        var resultFrame = CommandDriverCases.Frame();
        resultFrame["BridgeVersion"] = 32;
        resultFrame["Surfaces"]![0]!["Type"] = "GachaResultUI";
        resultFrame["Surfaces"]![0]!["InputReady"] = false;
        resultFrame["Surfaces"]![0]!["Targets"] = new JsonArray(new JsonObject { ["Field"] = "_objSkipButton", ["Enabled"] = true }, new JsonObject { ["Field"] = "_objBackButton", ["Enabled"] = true });
        Check(DailyFreeDrawStage.ResultTarget(resultFrame) == "_objSkipButton", "managed draw animation cannot use back before native readiness");
        resultFrame["BridgeVersion"] = 31;
        Check(DailyFreeDrawStage.ResultTarget(resultFrame) == null, "old bridge cannot skip a nonready animation");
    }
    internal sealed class Fixture : IDisposable
    {
        public string Root; public CommandDriverCases.Mailbox Box = new(); public JsonObject Context = CommandDriverCases.Context(), Current = CommandDriverCases.Frame(); public DailyCommandDriver Driver; public DailyManagedBusiness Business; public DailyFreeDrawStage Stage;
        public Dictionary<long, long> Counts = new() { [5] = 0, [6] = 0 }; public bool MissingResponse, ChangeIdentity, NoTap, FailAnimation, Stopped, EarlyBack; public int Previews, Confirmations, Skips, ResultBacks, PaidCommands;
        public double ResultDelay; public bool NeverShowResult; private double resultAt = double.PositiveInfinity; private double time; private long sequence; private long category = 5; private bool foreign, ready;
        public long UtcTicks => Ticks; public double Seconds => time; public Task Advance(TimeSpan span)
        {
            time += span.TotalSeconds;
            Publish();
            return Task.CompletedTask;
        }
        private long Ticks => 100000000 + (long)(time * TimeSpan.TicksPerSecond);
        public static DailyFreeDrawRules Rules() => new(new() { ["schema"] = 1, ["free"] = new JsonArray(new JsonObject { ["id"] = 50, ["group"] = 5, ["limit"] = 1 }, new JsonObject { ["id"] = 60, ["group"] = 6, ["limit"] = 1 }) });
        public Fixture(string root)
        {
            Root = root;
            Page("MenuUI");
            Box.AfterWrite = (c, n, b) => { if (n == "observation-request.json") Publish(); };
            Box.AfterCommand = command =>
            {
                int surface = command["SurfaceId"]!.GetValue<int>(), target = command["TargetId"]!.GetValue<int>();
                if (surface == 1)
                    Page("GachaMainUI");
                else if (surface == 3)
                {
                    if (target == 31)
                        category = 5;
                    else if (target == 32)
                        category = 6;
                    else if (target == 33)
                    {
                        Previews++;
                        AddPopup();
                    }
                    else if (target == 35)
                        Page("MenuUI");
                    else
                        PaidCommands++;
                }
                else if (surface == 5)
                {
                    Confirmations++;
                    long at = Ticks;
                    var request = new JsonObject { ["Role"] = DailyFreeDrawProof.Role, ["Kind"] = "request", ["Sequence"] = ++sequence, ["Frame"] = Current.DeepClone() };
                    Box.Values["live:events~" + at + "-" + sequence + "-fixture.json"] = JsonSerializer.SerializeToUtf8Bytes(request);
                    if (!MissingResponse)
                    {
                        var response = new JsonObject { ["Role"] = DailyFreeDrawProof.Role, ["Kind"] = "response", ["Sequence"] = ++sequence, ["Frame"] = Current.DeepClone(), ["Error"] = "", ["ErrorCode"] = 0, ["Accepted"] = true, ["Values"] = new JsonArray(new JsonObject { ["Path"] = "Results", ["Error"] = "", ["Json"] = "[{\"id\":" + (category == 5 ? 50 : 60) + ",\"rewardInfoBundle\":{\"items\":[1]}}]" }) };
                        Box.Values["live:events~" + (at + 1) + "-" + sequence + "-fixture.json"] = JsonSerializer.SerializeToUtf8Bytes(response);
                    }
                    Counts[category]++;
                    ready = false;
                    if (NeverShowResult || ResultDelay > 0)
                    {
                        Page("GachaMainUI");
                        resultAt = NeverShowResult ? double.PositiveInfinity : time + ResultDelay;
                    }
                    else Page("GachaResultUI");
                    if (ChangeIdentity)
                        foreign = true;
                }
                else if (surface == 7)
                {
                    if (target == 71)
                    {
                        Skips++;
                        if (!FailAnimation)
                        {
                            ready = true;
                            Page("GachaResultUI");
                        }
                    }
                    else if (target == 72)
                    {
                        ResultBacks++;
                        if (!ready)
                            EarlyBack = true;
                        Page("GachaMainUI");
                    }
                    else
                        PaidCommands++;
                }
                time += .01;
                Publish();
            };
            Driver = new(root, Box, () => { Current["AtUtcTicks"] = Ticks; var context = Context.DeepClone().AsObject(); if (foreign) context["actor"]![3] = "other"; return Task.FromResult(new DailyStageFrame(Current.DeepClone().AsObject(), context)); }, () => Stopped, () => Ticks, () => time, t => { time += t.TotalSeconds; Publish(); return Task.CompletedTask; });
            Driver.Bind(Context);
            Driver.Acquire("live");
            Business = new(root, Driver, [DailyFreeDrawProof.Definition()], () => Stopped, () => time, t => { time += t.TotalSeconds; Publish(); return Task.CompletedTask; });
            Stage = new(Driver, Business, () => Stopped, () => time, t => { time += t.TotalSeconds; Publish(); return Task.CompletedTask; }, Rules());
        }
        public void Page(string name)
        {
            Current = CommandDriverCases.Frame();
            Current["Protocol"] = 1;
            Current["Error"] = "";
            Current["BridgeVersion"] = DailyStageObservation.BridgeVersion;
            Current["AtUtcTicks"] = Ticks;
            Current["UiToken"] = name;
            var surface = Current["Surfaces"]![0]!;
            surface["Type"] = name;
            surface["Id"] = name == "MenuUI" ? 1 : name == "GachaMainUI" ? 3 : 7;
            surface["InputReady"] = name != "GachaResultUI" || ready;
            JsonObject Target(int id, string field, string route = "ui") => new()
            {
                ["Id"] = id,
                ["Field"] = field,
                ["Enabled"] = true,
                ["Route"] = route
            };
            surface["Targets"] = name == "MenuUI" ? new JsonArray(Target(11, "_buttonGacha")) : name == "GachaMainUI" ? new JsonArray(Target(31, "$pointer/UIRoot/Mask/BottomObject/Layout - MainCategory - Tab/Button - Costume", "pointer"), Target(32, "$pointer/UIRoot/Mask/BottomObject/Layout - MainCategory - Tab/Button - Equipment", "pointer"), Target(33, "_goFreeGachaMacro"), Target(35, "_objBackButton")) : new JsonArray(Target(ready ? 72 : 71, ready ? "_objBackButton" : "_objSkipButton"), Target(73, "_objRedrawGachaButton"));
        }
        public void AddPopup()
        {
            Current["UiToken"] = "preview";
            Current["Surfaces"]!.AsArray().Add(new JsonObject { ["Type"] = "MessagePopupUI", ["Id"] = 5, ["Popup"] = true, ["Order"] = 10, ["InputReady"] = true, ["Targets"] = new JsonArray(new JsonObject { ["Id"] = 51, ["Field"] = "_buttonOK", ["Enabled"] = true, ["Route"] = "ui" }) });
        }
        private void Publish()
        {
            if (time >= resultAt) { resultAt = double.PositiveInfinity; Page("GachaResultUI"); }
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
            var evidence = new JsonObject { ["Config"] = "free-fixture", ["ObservationRequest"] = request["Id"]!.DeepClone(), ["Frame"] = Current.DeepClone(), ["AtUtcTicks"] = Ticks, ["Error"] = "", ["Taps"] = NoTap ? new JsonArray() : new JsonArray(DailyFreeDrawProof.Role), ["Readings"] = new JsonArray(Reading("gacha.users", ("Count", Counts.Count), ("Keys", Counts.Keys.ToArray()), ("Values", Counts.Select(p => new { groupId = p.Key, totalBuyCount = 0, oneFreePickCount = p.Value }).ToArray())), Reading("gacha.ui", ("_goFreeGachaMacro.activeInHierarchy", Counts[category] == 0))) };
            Box.Values["live:evidence.json"] = JsonSerializer.SerializeToUtf8Bytes(evidence);
        }
        public async Task<JsonObject> PreviewRecord()
        {
            Page("GachaMainUI");
            var before = await Driver.EvidenceAsync(["gacha"]);
            var op = Business.Create(Context, DailyFreeDrawProof.Role, before, new()
            {
                ["native_free_only"] = true,
                ["category"] = "Costume"
            }, new()
            {
                ["ui"] = "MessagePopupUI",
                ["field"] = "_buttonOK",
                ["absent"] = "MessagePopupUI"
            });
            op["rules"] = Rules().Projection.DeepClone();
            AddPopup();
            op["state"] = "preview_ready";
            op["preview_frame"] = Current.DeepClone();
            Business.Save(op);
            Publish();
            return op;
        }
        public List<JsonObject> Records() => Business.Records(Context, includeLegacy: false).ToList();
        public void Dispose() => Driver.Dispose();
    }
}


