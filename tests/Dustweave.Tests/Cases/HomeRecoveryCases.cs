using BD2Daily;
using System.Text.Json;
using System.Text.Json.Nodes;
static class HomeRecoveryCases
{
    public static async Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string name)
        {
            if (!ok)
                throw new Exception(name);
            cases.Add(name);
        }
        int next = 0;
        Fixture Create() => new(Path.Combine(output, "home-proof-" + (++next)));
        async Task Reject(Func<Task> run, string kind, string label)
        {
            string actual = "";
            try
            {
                await run();
            }
            catch (StageHostException e) { actual = e.Kind; }
            Check(actual == kind, label);
        }
        var frame = HomeNavigationCases.Menu();
        var before = new JsonObject { ["Frame"] = frame.DeepClone() };
        JsonObject Event(string kind, int seq, string role = "missions.clear", bool accepted = false, int code = 3) => new()
        {
            ["Role"] = role,
            ["Kind"] = kind,
            ["Sequence"] = seq,
            ["Frame"] = frame.DeepClone(),
            ["Error"] = "",
            ["Accepted"] = accepted,
            ["ErrorCode"] = code,
            ["Values"] = new JsonArray()
        };
        var rejection = new JsonArray(Event("request", 1), Event("response", 2));
        Check(DailyHomeProof.ResetRejection(before, rejection)?["outcome"]?.GetValue<string>() == "rejected_no_reward", "managed reset proof accepts one complete native rejection");
        var partial = new JsonArray(Event("request", 1, "pass.reward", true, 0), Event("response", 2, "pass.reward", true, 0), Event("request", 3), Event("response", 4));
        Check(DailyHomeProof.ResetRejection(before, partial)?["outcome"]?.GetValue<string>() == "partial_then_rejected", "managed reset proof retains already accepted rewards without replay");
        foreach (string fault in new[] { "missing_response", "missing_request", "wrong_actor", "wrong_order", "response_error", "accepted_rejection", "wrong_code", "reward_in_rejection", "duplicate_rejection", "unmatched_pass", "duplicate_sequence" })
        {
            var events = rejection.DeepClone().AsArray();
            switch (fault)
            {
                case "missing_response":
                    events.RemoveAt(1);
                    break;
                case "missing_request":
                    events.RemoveAt(0);
                    break;
                case "wrong_actor":
                    events[1]!["Frame"]!["Instance"] = "foreign";
                    break;
                case "wrong_order":
                    events[1]!["Sequence"] = 0;
                    break;
                case "response_error":
                    events[1]!["Error"] = "decode";
                    break;
                case "accepted_rejection":
                    events[1]!["Accepted"] = true;
                    break;
                case "wrong_code":
                    events[1]!["ErrorCode"] = 4;
                    break;
                case "reward_in_rejection":
                    events[1]!["Values"]!.AsArray().Add(new JsonObject { ["Path"] = "RewardInfoBundle", ["Json"] = "{\"items\":[1]}", ["Error"] = "" });
                    break;
                case "duplicate_rejection":
                    events.Add(Event("request", 3));
                    events.Add(Event("response", 4));
                    break;
                case "unmatched_pass":
                    events.Add(Event("request", 3, "pass.reward"));
                    break;
                case "duplicate_sequence":
                    events[1]!["Sequence"] = 1;
                    break;
            }
            Check(DailyHomeProof.ResetRejection(before, events) == null, "managed reset proof rejects " + fault);
        }
        using (var f = Create())
        {
            f.Reset();
            var op = f.ResetOperation();
            f.Save(op);
            var original = op["before"]!.ToJsonString();
            await f.Driver.HomeRecoveryAsync("mission_reset");
            var saved = f.Load(op);
            Check(f.Box.Commands.Count == 1 && saved["state"]?.GetValue<string>() == "server_rejected" && saved["recovery_cleanup"]?["popup_closed"]?.GetValue<bool>() == true, "owned reset acknowledges once and preserves a server rejection instead of success");
            Check(saved["before"]!.ToJsonString() == original && saved["rejection"]?["replay"]?.GetValue<bool>() == false, "reset recovery preserves original entry evidence and never replays claims");
        }
        using (var f = Create())
        {
            f.Reset();
            await Reject(() => f.Driver.HomeRecoveryAsync("mission_reset"), "adapter", "unowned reset has no acknowledgement");
            Check(f.Box.Commands.Count == 0, "unowned reset sends no command");
        }
        using (var f = Create())
        {
            f.Reset();
            var op = f.ResetOperation();
            op["events"]!.AsArray().RemoveAt(1);
            f.Save(op);
            await Reject(() => f.Driver.HomeRecoveryAsync("mission_reset"), "adapter", "reset with a missing native reply stays blocked");
            Check(f.Box.Commands.Count == 0, "missing reset receipt never clicks OK");
        }
        using (var f = Create())
        {
            f.Reset();
            var op = f.ResetOperation();
            op["before"]!["Frame"]!["ProcessStartTicks"] = 11;
            f.Save(op);
            await Reject(() => f.Driver.HomeRecoveryAsync("mission_reset"), "adapter", "reused process ID cannot own an old reset popup");
        }
        using (var f = Create())
        {
            f.Reset();
            var op = f.ResetOperation();
            f.Save(op);
            f.Box.State = "unknown";
            await Reject(() => f.Driver.HomeRecoveryAsync("mission_reset"), "pending", "unknown reset acknowledgement receipt cannot be retried");
            Check(f.Box.Commands.Count == 1 && f.Load(op)["recovery_cleanup"] == null, "unknown reset leaves the original business unclosed");
        }
        using (var f = Create())
        {
            f.Reset();
            var op = f.ResetOperation();
            f.Save(op);
            f.StopAfterInput = true;
            await Reject(() => f.Driver.HomeRecoveryAsync("mission_reset"), "pending", "stop after acknowledgement retains an uncertain original input");
            Check(f.Load(op)["state"]?.GetValue<string>() == "unknown", "stopped acknowledgement does not rewrite the business as recovered");
        }
        using (var f = Create())
        {
            f.Reset();
            var op = f.ResetOperation();
            f.Save(op);
            f.OnRead = count => { if (count >= 5) f.Current["Surfaces"]![0]!["Text"] = new JsonArray("购买确认"); };
            await Reject(() => f.Driver.HomeRecoveryAsync("mission_reset"), "navigation_changed", "reset popup changing into a purchase is rejected on the final .NET observation");
            Check(f.Box.Commands.Count == 0, "changed acknowledgement semantics send no native input");
        }
        using (var f = Create())
        {
            f.Reset();
            var op = f.ResetOperation();
            f.Save(op);
            f.Current["Surfaces"]![0]!["InputReady"] = false;
            f.Current["Surfaces"]![0]!["Targets"]![0]!["Enabled"] = false;
            f.OnWait = () => { if (f.Time >= .7 && f.Box.Commands.Count == 0) { f.Current["Surfaces"]![0]!["InputReady"] = true; f.Current["Surfaces"]![0]!["Targets"]![0]!["Enabled"] = true; } };
            await f.Navigate();
            Check(f.Box.Commands.Count == 1 && f.Load(op)["state"]?.GetValue<string>() == "server_rejected", "startup waits reset animation and proves its owner before exactly one acknowledgement");
        }
        using (var f = Create())
        {
            f.Reward();
            f.ReadyAt = .7;
            await f.Driver.HomeRecoveryAsync("reward");
            Check(f.Box.Commands.Count == 1 && f.Time >= .7, "startup reward waits all native animation gates before one close");
        }
        using (var f = Create())
        {
            f.Reward();
            f.ReadyAt = 100;
            await Reject(() => f.Driver.HomeRecoveryAsync("reward"), "adapter", "startup reward animation has a bounded wait");
            Check(f.Box.Commands.Count == 0, "unready reward never receives a close or another claim");
        }
        using (var f = Create())
        {
            f.Reward();
            f.ReadyAt = 100;
            f.OnWait = () => f.Stopped = true;
            await Reject(() => f.Driver.HomeRecoveryAsync("reward"), "stopped", "stop while waiting reward sends no close");
        }
        var question = Question();
        Check(DailyQuizRecovery.AnswerId(question) == 11, "managed quiz derives the unique success branch from current stable option IDs");
        question["Displayed"] = new JsonArray(11, 13);
        Check(DailyQuizRecovery.AnswerId(question) == 11, "quiz display order cannot choose the first visible answer");
        foreach (string fault in new[] { "two_successes", "missing_jump", "wrong_display", "no_choice", "duplicate_node", "missing_select" })
        {
            var p = Question();
            switch (fault)
            {
                case "two_successes":
                    p["Talks"]![4] = JsonSerializer.Serialize(new
                    {
                        id = 14,
                        bubbleType = 1,
                        returnId = -1
                    });
                    break;
                case "missing_jump":
                    p["Talks"]![4] = JsonSerializer.Serialize(new
                    {
                        id = 14,
                        bubbleType = 1,
                        returnId = 999
                    });
                    break;
                case "wrong_display":
                    p["Displayed"] = new JsonArray(11, 99);
                    break;
                case "no_choice":
                    p["Choosing"] = false;
                    break;
                case "duplicate_node":
                    p["Talks"]![4] = p["Talks"]![0]!.DeepClone();
                    break;
                case "missing_select":
                    p["Selects"] = new JsonArray();
                    break;
            }
            bool invalid = false;
            try
            {
                DailyQuizRecovery.AnswerId(p);
            }
            catch (InvalidDataException) { invalid = true; }
            Check(invalid, "managed quiz graph rejects " + fault);
        }
        using (var f = Create())
        {
            f.Quiz(false);
            await f.Navigate();
            Check(f.Box.Commands.Select(c => c["Kind"]!.GetValue<string>()).SequenceEqual(new[] { "back", "quiz_leave" }), "idle quiz list and hub return home in .NET without playing pending questions");
        }
        using (var f = Create())
        {
            f.Quiz(true);
            var op = f.QuizOperation();
            op["before"]!["Frame"]!["Instance"] = "old";
            op["before"]!["Config"] = "old";
            f.Save(op);
            string original = op["before"]!.ToJsonString();
            f.OnWait = () => { if (f.Time >= 1 && f.QuizInputs > 0 && !f.Completed) f.CompleteQuiz(); };
            await f.Navigate();
            var saved = f.Load(op);
            Check(saved["state"]?.GetValue<string>() == "completed" && f.QuizInputs == 1 && f.Box.Commands.All(c => c["Kind"]?.GetValue<string>() != "quiz_play"), "owned startup quiz finishes once without reopening or replaying stale choice");
            Check(saved["before"]!.ToJsonString() == original && saved["continuation"]?["before"]?["Config"]?.GetValue<string>() == "fixture", "quiz observer change rebases only the receipt window and preserves the original entry");
            Check(f.Box.Commands.Count == 4, "owned quiz continuation, reward close, list close and hub leave all use the common managed driver");
        }
        using (var f = Create())
        {
            f.Quiz(true);
            f.QuizPage["Choosing"] = false;
            f.Save(f.QuizOperation());
            f.OnWait = () => { if (f.Time >= .7 && f.QuizInputs == 0) f.QuizPage["Choosing"] = true; if (f.Time >= 1.2 && f.QuizInputs > 0 && !f.Completed) f.CompleteQuiz(); };
            await f.Navigate();
            Check(f.QuizInputs == 1 && f.Time >= 1.2, "owned quiz entry animation is observed before choosing a single answer");
        }
        using (var f = Create())
        {
            f.Quiz(false);
            f.Current = HomeNavigationCases.Frame(HomeNavigationCases.Surface("BalloonScriptUI"));
            f.OnWait = () => { if (f.Time >= .7 && f.Box.Commands.Count == 0) f.Current = HomeNavigationCases.Frame(HomeNavigationCases.Surface("MiniEventQuizUI")); };
            await f.Navigate();
            Check(f.Box.Commands.Count == 2 && f.QuizInputs == 0, "finished quiz balloon exit settles before idle menus close without another answer");
        }
        using (var f = Create())
        {
            f.Quiz(false);
            f.Current = HomeNavigationCases.Frame(HomeNavigationCases.Surface("BalloonScriptUI"));
            await Reject(() => f.Driver.HomeRecoveryAsync("quiz"), "adapter", "unknown balloon cannot remain in an unlimited entry animation wait");
            Check(f.Box.Commands.Count == 0 && f.Time >= 20, "unknown balloon wait is bounded with no skip or answer");
        }
        using (var f = Create())
        {
            f.Quiz(true);
            await Reject(() => f.Driver.HomeRecoveryAsync("quiz"), "adapter", "active unowned quiz is preserved");
            Check(f.Box.Commands.Count == 0, "unowned quiz sends no answer or skip");
        }
        using (var f = Create())
        {
            f.Quiz(true);
            var a = f.QuizOperation();
            f.Save(a);
            var b = a.DeepClone().AsObject();
            b["id"] = new string('c', 32);
            f.Save(b);
            await Reject(() => f.Driver.HomeRecoveryAsync("quiz"), "adapter", "multiple owned quiz candidates stay blocked");
            Check(f.Box.Commands.Count == 0, "ambiguous quiz owner sends zero inputs");
        }
        using (var f = Create())
        {
            f.Quiz(true);
            var op = f.QuizOperation();
            f.Save(op);
            f.AddQuizEvent("request", 1);
            await Reject(() => f.Driver.HomeRecoveryAsync("quiz"), "adapter", "already sent quiz completion is never driven again");
            Check(f.QuizInputs == 0, "pending quiz completion sends no duplicate answer");
        }
        using (var f = Create())
        {
            f.Quiz(true);
            var op = f.QuizOperation();
            f.Save(op);
            var prefs = new DailyPreferences();
            prefs.Events.Quiz = false;
            new DailyPreferenceStore(f.Root).Save(new string('a', 64), prefs);
            await Reject(() => f.Driver.HomeRecoveryAsync("quiz"), "adapter", "disabled quiz preference preserves active question");
        }
        using (var f = Create())
        {
            f.Quiz(true);
            var op = f.QuizOperation();
            op["scope"]!["quiz"] = 15;
            f.Save(op);
            await Reject(() => f.Driver.HomeRecoveryAsync("quiz"), "adapter", "another quiz ID cannot own current dialogue");
        }
        using (var f = Create())
        {
            f.Quiz(true);
            var op = f.QuizOperation();
            f.Save(op);
            await Reject(() => f.Driver.HomeRecoveryAsync("quiz"), "adapter", "quiz with no cursor progress is bounded");
            Check(f.QuizInputs == 1 && f.Load(op)["state"]?.GetValue<string>() == "unknown", "stale quiz choice is sent once and its original operation remains unresolved");
        }
        using (var f = Create())
        {
            f.Quiz(true);
            var op = f.QuizOperation();
            f.Save(op);
            f.StopAfterInput = true;
            await Reject(() => f.Driver.HomeRecoveryAsync("quiz"), "pending", "stop after quiz input preserves a dispatched original command");
            Check(f.QuizInputs == 1 && f.Load(op)["command_id"]?.GetValue<string>() == "original-entry", "stopped quiz never replaces the original start command");
        }
        using (var f = Create())
        {
            f.Quiz(true);
            var op = f.QuizOperation();
            f.Save(op);
            f.ChangeIdentityAfterInput = true;
            await Reject(() => f.Driver.HomeRecoveryAsync("quiz"), "pending", "identity changes after quiz input preserve its uncertain operation");
        }
        JsonObject savedUnknown;
        using (var f = Create())
        {
            f.Quiz(true);
            var op = f.QuizOperation();
            f.Save(op);
            f.Box.State = "unknown";
            await Reject(() => f.Driver.HomeRecoveryAsync("quiz"), "pending", "uncertain quiz input keeps a durable continuation checkpoint");
            savedUnknown = f.Load(op);
            Check(savedUnknown["continuation_input"]?["state"]?.GetValue<string>() == "unknown" && savedUnknown["continuation_input"]?["command"] != null, "uncertain answer preserves its original command separately from quiz entry");
        }
        using (var f = Create())
        {
            f.Quiz(true);
            f.Save(savedUnknown);
            await Reject(() => f.Driver.HomeRecoveryAsync("quiz"), "adapter", "restart with the same quiz cursor does not replay an uncertain answer");
            Check(f.QuizInputs == 0, "durable quiz checkpoint prevents duplicate input across a new controller");
        }
        using (var f = Create())
        {
            f.Quiz(true);
            f.Save(savedUnknown);
            f.QuizPage["Cursor"] = 3;
            f.QuizPage["Choosing"] = false;
            f.QuizPage["Touch"] = true;
            f.OnWait = () => { if (f.QuizInputs > 0 && !f.Completed) f.CompleteQuiz(); };
            await f.Navigate();
            Check(f.QuizInputs == 1, "authoritative cursor progress permits continuing after a saved unknown answer");
        }
        using (var f = Create())
        {
            f.Current = HomeNavigationCases.Field();
            await Reject(() => f.Driver.HomeRecoveryAsync("quiz"), "navigation_changed", "managed startup recovery revalidates its kind before input");
            Check(f.Box.Commands.Count == 0, "changed startup kind cannot weaken input validation");
        }
    }
    internal static JsonObject Question() => new()
    {
        ["Available"] = true,
        ["EventUid"] = 32,
        ["HubUid"] = 19,
        ["Group"] = 3,
        ["QuizId"] = 14,
        ["Playing"] = true,
        ["Choosing"] = true,
        ["Touch"] = false,
        ["Cursor"] = 2,
        ["Talks"] = new JsonArray(JsonSerializer.Serialize(new
        {
            id = 10,
            bubbleType = 1,
            returnId = 0
        }), JsonSerializer.Serialize(new
        {
            id = 11,
            bubbleType = 4,
            selectDialogId = 4,
            returnId = 0
        }), JsonSerializer.Serialize(new
        {
            id = 12,
            bubbleType = 1,
            returnId = -1
        }), JsonSerializer.Serialize(new
        {
            id = 13,
            bubbleType = 4,
            selectDialogId = 0,
            returnId = 0
        }), JsonSerializer.Serialize(new
        {
            id = 14,
            bubbleType = 1,
            returnId = 10
        })),
        ["Selects"] = new JsonArray(JsonSerializer.Serialize(new
        {
            id = 4,
            dialogTextId = new[] { 11, 13 }
        })),
        ["Displayed"] = new JsonArray(13, 11),
        ["Quizzes"] = new JsonArray(new JsonObject { ["Group"] = 3, ["Id"] = 14, ["Complete"] = false, ["Open"] = true })
    };
    internal sealed class Fixture : IDisposable
    {
        public string Root; public double Time, ReadyAt; public bool Stopped, StopAfterInput, ChangeIdentityAfterInput, Completed; public int QuizInputs;
        public JsonObject Current = HomeNavigationCases.Menu(), Context = CommandDriverCases.Context(), QuizPage = Question(); public CommandDriverCases.Mailbox Box = new(); public DailyCommandDriver Driver; public Action? OnWait; public Action<int>? OnRead; private int readCount;
        public long Ticks => 100000000 + (long)(Time * TimeSpan.TicksPerSecond);
        public Fixture(string root)
        {
            Root = root;
            Box.AfterWrite = (c, n, d) => { if (n == "observation-request.json") Publish(); };
            Box.AfterCommand = Command;
            Driver = new(root, Box, Read, () => Stopped, () => Ticks, () => Time, t => { Time += t.TotalSeconds; OnWait?.Invoke(); Publish(); return Task.CompletedTask; });
            Driver.Bind(Context.DeepClone().AsObject());
            Driver.Acquire("live");
        }
        public Task<DailyStageFrame> Read()
        {
            OnRead?.Invoke(++readCount);
            Current["AtUtcTicks"] = Ticks;
            Publish();
            return Task.FromResult(new DailyStageFrame(Current.DeepClone().AsObject(), Context.DeepClone().AsObject()));
        }
        public async Task Navigate()
        {
            var nav = new DailyStageNavigation(Read, () => Stopped, clock: () => Time, delay: t => { Time += t.TotalSeconds; OnWait?.Invoke(); Publish(); return Task.CompletedTask; });
            await nav.EnterAsync("guild", Context.DeepClone().AsObject(), (op, s, a) => op switch { "navigation_step" => Driver.NavigationAsync(a!["action"]!.AsObject()), "home_recovery" => Driver.HomeRecoveryAsync(a!["kind"]!.GetValue<string>()), _ => throw new Exception("Startup called Python: " + op) });
        }
        public void Reset()
        {
            var popup = HomeNavigationCases.Surface("MessagePopupUI", true, "_buttonOK");
            popup["Path"] = "Root/ErrorMessagePopupUI";
            popup["Text"] = new JsonArray("error : 112003");
            Current = HomeNavigationCases.Frame(popup);
        }
        public void Reward()
        {
            Current = HomeNavigationCases.Frame(HomeNavigationCases.Surface("MenuUI"), HomeNavigationCases.Surface("RewardReceivePopupUI", true, null, 4));
        }
        public void Quiz(bool playing)
        {
            QuizPage = Question();
            QuizPage["Playing"] = playing;
            Current = playing ? HomeNavigationCases.Frame(HomeNavigationCases.Surface("BalloonScriptUI")) : HomeNavigationCases.Frame(HomeNavigationCases.Surface("MiniEventQuizUI"));
            Publish();
        }
        public JsonObject Evidence() => new()
        {
            ["Frame"] = Current.DeepClone(),
            ["Config"] = "fixture",
            ["AtUtcTicks"] = Ticks,
            ["Error"] = "",
            ["Taps"] = new JsonArray("minigames.quiz_clear"),
            ["Readings"] = new JsonArray(
            Reading("minigames.quiz", ("$self", QuizPage)), Reading("reward.presentation", ("ὣὤὥὦὯὦὩὤὨὠὪ", Time >= ReadyAt), ("ὪὯὣὥὬὫὬὩὠὮὠ", Time >= ReadyAt), ("ὮὬὧὦὣὠὠὤὮὦὧ", Time >= ReadyAt), ("ὦὡὮὫὧὠὮὡὭὭὬ", Time < ReadyAt)))
        };
        private static JsonObject Reading(string id, params (string Path, object Value)[] values) => new() { ["Id"] = id, ["Error"] = "", ["Values"] = new JsonArray(values.Select(v => (JsonNode)new JsonObject { ["Path"] = v.Path, ["Json"] = JsonSerializer.Serialize(v.Value), ["Error"] = "" }).ToArray()) };
        private void Publish()
        {
            if (!Box.Values.TryGetValue("live:observation-request.json", out var raw))
                return;
            Current["AtUtcTicks"] = Ticks;
            var evidence = Evidence();
            evidence["ObservationRequest"] = JsonNode.Parse(raw)!["Id"]!.DeepClone();
            Box.Values["live:evidence.json"] = JsonSerializer.SerializeToUtf8Bytes(evidence);
        }
        private void Command(JsonObject command)
        {
            string ui = Current["Surfaces"]!.AsArray().Single(n => JsonNode.DeepEquals(n!["Id"], command["SurfaceId"]))!["Type"]!.GetValue<string>();
            if (command["Kind"]?.GetValue<string>() == "quiz_talk")
                QuizInputs++;
            else if (ui == "RewardReceivePopupUI")
                Current = Completed ? HomeNavigationCases.Frame(HomeNavigationCases.Surface("MiniEventQuizUI")) : HomeNavigationCases.Menu();
            else if (ui == "MiniEventQuizUI")
                Current = HomeNavigationCases.Frame(HomeNavigationCases.Surface("MiniEventMainUI"));
            else
                Current = HomeNavigationCases.Menu();
            if (StopAfterInput)
                Stopped = true;
            if (ChangeIdentityAfterInput)
                Context["actor"]![3] = new string('c', 64);
            Publish();
        }
        public void CompleteQuiz()
        {
            Completed = true;
            QuizPage["Playing"] = false;
            QuizPage["Quizzes"]![0]!["Complete"] = true;
            Current = HomeNavigationCases.Frame(HomeNavigationCases.Surface("MiniEventQuizUI"), HomeNavigationCases.Surface("RewardReceivePopupUI", true, null, 4));
            AddQuizEvent("request", 1);
            AddQuizEvent("response", 2);
            Publish();
        }
        public void AddQuizEvent(string kind, int sequence)
        {
            var e = new JsonObject { ["Role"] = "minigames.quiz_clear", ["Kind"] = kind, ["Sequence"] = sequence, ["Frame"] = Current.DeepClone(), ["Error"] = "", ["Accepted"] = true, ["ErrorCode"] = 0, ["Values"] = new JsonArray() };
            if (kind == "response")
                e["Values"]!.AsArray().Add(new JsonObject { ["Path"] = "ClearInfo", ["Json"] = "{\"eventUid\":32,\"groupId\":3,\"id\":14}", ["Error"] = "" });
            Box.Values["live:events~" + Ticks + "-" + sequence + "-fixture.json"] = JsonSerializer.SerializeToUtf8Bytes(e);
        }
        public JsonObject ResetOperation()
        {
            var op = Operation("missions.clear");
            op["events"] = new JsonArray(new JsonObject { ["Role"] = "missions.clear", ["Kind"] = "request", ["Sequence"] = 1, ["Frame"] = Current.DeepClone() }, new JsonObject { ["Role"] = "missions.clear", ["Kind"] = "response", ["Sequence"] = 2, ["Frame"] = Current.DeepClone(), ["Error"] = "", ["ErrorCode"] = 3, ["Accepted"] = false, ["Values"] = new JsonArray() });
            return op;
        }
        public JsonObject QuizOperation()
        {
            var op = Operation("minigames.quiz_clear");
            op["scope"] = new JsonObject { ["event"] = 32, ["group"] = 3, ["quiz"] = 14 };
            op["command_id"] = "original-entry";
            return op;
        }
        private JsonObject Operation(string role) => new() { ["id"] = new string('a', 32), ["role"] = role, ["state"] = "unknown", ["at"] = Ticks - 100, ["before"] = Evidence(), ["account"] = new string('a', 64), ["player"] = new string('b', 64), ["server"] = "test", ["cycle"] = "today", ["events"] = new JsonArray() };
        private string OpPath(JsonObject op) => Path.Combine(Root, "live", "business", op["id"]!.GetValue<string>() + ".json");
        public void Save(JsonObject op) => DailyJson.Write(OpPath(op), op); public JsonObject Load(JsonObject op) => DailyJson.TryRead<JsonObject>(OpPath(op))!;
        public void Dispose() => Driver.Dispose();
    }
}
