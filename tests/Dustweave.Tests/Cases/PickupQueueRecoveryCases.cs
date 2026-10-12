using Dustweave;
using System.Text.Json.Nodes;
using static Dustweave.DailyData;

static class PickupQueueRecoveryCases
{
    public static async Task Run(string root, List<string> cases)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); cases.Add(label); }
        foreach (string entry in new[] { "fresh", "resume", "retry" })
        foreach (string page in new[] { "MenuUI", "GameFieldDefaultUI" })
        foreach (bool settled in new[] { true, false })
        {
            using var f = new WorkflowHarness(Path.Combine(root, "pickup-queue-" + Guid.NewGuid().ToString("N")), DailyFieldRoute.Proofs().ToArray());
            bool available = true;
            JsonObject Drop(long id, bool pending)
            {
                var row = WorkflowCases.Reading("mainline.reward", (DailyFieldRoute.ObjectPath, id),
                    ("ὯὩὮὩὮὣὯὫὮὡὧ", true), ("ὦὫὧὯὥὫὦὪὣὦὪ", false), (DailyFieldRoute.DropAvailablePath, pending));
                row["InstanceId"] = id + 2000; return row;
            }
            f.Readings = () => [WorkflowCases.Reading("mainline.map", (DailyFieldRoute.MapPath, O(("id", 23)))),
                WorkflowCases.Reading("mainline.talent_counts", ("Values", new JsonArray())), Drop(201, available), Drop(227, false)];
            f.Page(page);
            var op = f.Business.Create(f.Context, "mainline.pickup", f.Evidence(), O(("map", 23), ("object", 201)),
                O(("ui", "GameFieldDefaultUI"), ("operation", "mainline_walk"), ("value", 1201)));
            // Pre-upgrade record: old observer, old Unity pointer, old evidence schema.
            op["state"] = "unknown";
            op["before"]!["Frame"]!["Instance"] = "previous-observer";
            foreach (var row in Rows(op["before"]?["Readings"]).Where(r => S(r["Id"]) == "mainline.reward"))
                row["Values"]!.AsArray().RemoveAt(3);
            op["error"] = "interrupted"; op["recovery_error"] = "old schema";
            op["last_observation"] = op["before"]!.DeepClone();
            var request = WorkflowCases.Event("mainline.pickup", "request", 31);
            var reply = WorkflowCases.Event("mainline.pickup", "response", 32, ("RewardInfoBundle", new JsonObject()));
            foreach (var evt in new[] { request, reply }) { evt["Frame"] = op["before"]!["Frame"]!.DeepClone(); evt["AtUtcTicks"] = f.Ticks; }
            op["events"] = settled ? new JsonArray(request, reply) : new JsonArray(request);
            f.Business.Save(op);
            var originalEvents = op["events"]!.DeepClone();
            f.Time = 1;

            if (entry == "fresh" && page == "MenuUI" && settled)
            {
                foreach (string mutation in new[] { "account", "player", "server", "frame", "reply-actor", "future", "role", "operation", "request-only", "missing-code", "missing-values" })
                {
                    var bad = op.DeepClone().AsObject(); var context = f.Context.DeepClone().AsObject(); var frame = f.Frame.DeepClone().AsObject();
                    switch (mutation)
                    {
                        case "account": bad["account"] = "other"; break;
                        case "player": bad["player"] = "other"; break;
                        case "server": context["server"] = "other"; break;
                        case "frame": frame["ProcessId"] = 99; break;
                        case "reply-actor": bad["events"]![1]!["Frame"]!["Instance"] = "other"; break;
                        case "future": bad["events"]![1]!["AtUtcTicks"] = f.Ticks + 1; break;
                        case "role": bad["role"] = "dispatch.start"; break;
                        case "operation": bad["action"]!["operation"] = "mainline_talent"; break;
                        case "request-only": bad["events"]!.AsArray().RemoveAt(1); break;
                        case "missing-code": bad["events"]![1]!.AsObject().Remove("ErrorCode"); break;
                        case "missing-values": bad["events"]![1]!["Values"] = new JsonArray(); break;
                    }
                    Check(!DailyPickupRecovery.CanRetireWalk(bad, bad["events"]!.AsArray(), new(frame, context)), "historical walk retains guard: " + mutation);
                }
                var next = f.Context.DeepClone().AsObject(); next["actor"]![0] = 9; next["actor"]![1] = 20; next["cycle"] = "tomorrow";
                var newGame = f.Frame.DeepClone().AsObject(); newGame["ProcessId"] = 9; newGame["ProcessStartTicks"] = 20;
                Check(DailyPickupRecovery.CanRetireWalk(op, op["events"]!.AsArray(), new(newGame, next)),
                    "settled historical navigation can retire after restart/reset without claiming or replaying anything");
            }
            var priorContext = f.Context.DeepClone().AsObject(); priorContext["actor"]![2] = "previous-observer";
            string prior = Guid.NewGuid().ToString("N");
            DailyJson.Write(DailyQueueEngine.RecordPath(f.Root, prior), O(("schema", 1), ("id", prior), ("state", "partial"),
                ("context", priorContext), ("items", new JsonArray(O(("task", "weekly_mainline"), ("state", "recovery_required")),
                    O(("task", "mail"), ("state", "pending"))))));
            int moves = 0;
            f.OnCommand = command =>
            {
                Check(S(command["Kind"]) == "mainline_walk" && N(command["Value"]) == 2201,
                    "recovery selects current target pointer instead of replaying the historical command");
                ++moves; available = false;
                f.Event("mainline.pickup", "request"); f.Event("mainline.pickup", "response", ("RewardInfoBundle", new JsonObject()));
            };
            await using var host = new Host(f);
            var engine = new DailyQueueEngine(host, () => false, () => new DailyPreferences { Weekly = new() { Mainline = true } });
            var result = await engine.RunAsync(new(f.Root, S(f.Context["actor"]![3]), Path.Combine(f.Root, "queue-ui.json"),
                Resume: entry == "resume" ? prior : null, Tasks: entry == "resume" ? null : ["weekly_mainline", "mail"], RetryOf: entry == "retry" ? prior : null));
            var item = Rows(result["items"]).Single(r => S(r["task"]) == "weekly_mainline");
            string label = entry + "/" + page + "/" + settled;
            Check(S(item["state"]) == (settled ? "completed" : "recovery_required") && moves == (settled ? 1 : 0) && host.WeeklyNavigations == moves,
                "full queue reconciles before navigation and confirms fresh pickup: " + label);
            Check(S(Rows(result["items"]).Single(r => S(r["task"]) == "mail")["state"]) == "completed", "unrelated task continues: " + label);
            var saved = f.Business.Records(f.Context).Single(r => S(r["id"]) == S(op["id"]));
            Check(S(saved["state"]) == (settled ? "superseded" : "unknown") && saved["result"] == null && JsonNode.DeepEquals(saved["events"], originalEvents),
                "historical evidence remains intact with no invented target completion: " + label);
            if (settled) Check(saved["error"] == null && saved["recovery_error"] == null, "retired record removes stale blocking errors: " + label);
            if (entry == "resume") Check(result["connection_history"] is JsonArray { Count: 1 }, "resume records observer renewal: " + label);
        }
    }

    sealed class Host(WorkflowHarness f) : IDailyStageHost
    {
        public int WeeklyNavigations;
        public async Task<JsonObject> CallAsync(string operation, string? stage = null, JsonObject? arguments = null)
        {
            if (operation == "observe") return O(("context", f.Context), ("adapters", new JsonArray("weekly_mainline", "mail")), ("owners", new JsonArray()));
            if (operation == "reconcile") return await f.Business.ReconcileAsync(f.Context);
            if (operation == "navigate" && stage == "weekly_mainline") { ++WeeklyNavigations; f.Page("GameFieldDefaultUI"); }
            if (operation == "execute" && stage == "weekly_mainline")
            {
                var route = new DailyFieldRoute(f.Workflow);
                var before = await f.Driver.EvidenceAsync(["mainline"]);
                var op = await route.WalkPickup(before, DailyFieldRoute.Drops(before).Single());
                return O(("state", "completed"), ("stages", O(("weekly_mainline", O(("state", S(op["state"])))))));
            }
            return O(("state", "completed"));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
