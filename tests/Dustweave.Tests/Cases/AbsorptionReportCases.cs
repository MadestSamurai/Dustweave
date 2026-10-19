using System.Text.Json;
using System.Text.Json.Nodes;
using Dustweave;
using static Dustweave.DailyData;

static class AbsorptionReportCases
{
    public static void Run(string output, List<string> checks)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        string root = Path.Combine(output, "absorption-host"), box = Path.Combine(output, "absorption-box");
        string account = DailyIdentity.MemberKey("101");
        long now = DateTime.UtcNow.Ticks;
        string cycle = (now + TimeSpan.FromDays(1).Ticks + TimeSpan.FromHours(9).Ticks).ToString();
        JsonObject Context(string day) => O(("actor", new JsonArray(1, 2, "observer", account, "player")), ("server", "test"), ("cycle", day));
        JsonObject Op(long map, bool npc = false) => O(("id", Guid.NewGuid().ToString("N")), ("role", "dispatch.start"),
            ("state", "completed"), ("account", account), ("player", "player"), ("server", "test"), ("cycle", cycle), ("at", now),
            ("action", O(("operation", "mainline_talent"))),
            ("scope", O(("kind", 4), ("pack", 8), ("map", map), ("group", 405), ("along_npc", npc),
                ("pack_name", new DailyDisplayName("血之狂想曲", "血之狂想曲", "Blood Rhapsody")),
                ("map_name", new DailyDisplayName("战斗区域", "戰鬥區域", "Battle area")))), ("result", O(("IsSuccess", true))));
        var first = Op(81);
        using (var h = new WorkflowHarness(root, DailyWorkflowRegistry.Proofs()))
        {
            h.Business.Save(first);
            var day = DailyAbsorptionLog.Read(root, Context(cycle));
            Check(day is { Maps: 1 } && !day.Visits[0].AlongNpc && day.Visits[0].PackName.En == "Blood Rhapsody",
                "confirmed transaction immediately publishes named absorption even before later field settling");
            h.Business.Save(first);
            Check(DailyAbsorptionLog.Read(root, Context(cycle))!.Visits.Count == 1, "receipt recovery and repeated saves cannot double count a cast");
            Check(h.Box.Commands.Count == 0, "absorption reporting never submits game commands");
        }
        foreach (string state in new[] { "dispatching", "uncertain", "superseded", "skipped", "rejected" })
        {
            var pending = Op(82); pending["state"] = state; DailyAbsorptionLog.Record(root, pending);
        }
        var failed = Op(82); failed["result"]!["IsSuccess"] = false; DailyAbsorptionLog.Record(root, failed);
        var research = Op(82); research["scope"]!["kind"] = 6; DailyAbsorptionLog.Record(root, research);
        Check(DailyAbsorptionLog.Read(root, Context(cycle))!.Maps == 1, "uncertain casts, current empty maps and other talents are never presented as confirmed absorption");
        var npc = Op(82, true); npc["at"] = now + 100; DailyAbsorptionLog.Record(root, npc);
        var rerun = Op(83); rerun["at"] = now + 200; DailyAbsorptionLog.Record(box, rerun);
        DailyAccountProgress.Copy(root, box, account); DailyAccountProgress.Copy(box, root, account);
        var combined = DailyAbsorptionLog.Read(root, Context(cycle))!;
        Check(combined.Maps == 3 && combined.Visits.Select(v => v.Map).SequenceEqual(new long[] { 81, 82, 83 })
            && combined.Visits.Single(v => v.Map == 82).AlongNpc, "normal and isolated reruns merge named visits chronologically while preserving NPC provenance");
        DailyAbsorptionLog.Record(root, Op(81));
        Check(DailyAbsorptionLog.Read(root, Context(cycle)) is { Maps: 3, Visits.Count: 4 }, "multiple confirmed casts on one map show each time but count the map once");
        string path = Path.Combine(root, "live", "absorption-history", account + ".json");
        long stamp = File.GetLastWriteTimeUtc(path).Ticks;
        DailyAccountProgress.Copy(box, root, account);
        Check(File.GetLastWriteTimeUtc(path).Ticks == stamp, "unchanged import does not rewrite the current-day projection");
        var next = Op(84); string nextCycle = (long.Parse(cycle) + TimeSpan.FromDays(1).Ticks).ToString(); next["cycle"] = nextCycle;
        DailyAbsorptionLog.Record(root, next); DailyAccountProgress.Copy(box, root, account); DailyAbsorptionLog.Record(root, first);
        Check(DailyAbsorptionLog.Read(root, Context(nextCycle)) is { Maps: 1 } && DailyAbsorptionLog.Read(root, Context(cycle)) == null,
            "game reset starts a fresh day and late old queues or sandbox imports cannot overwrite it");
        var foreign = Context(nextCycle); foreign["actor"]![4] = "other-player";
        Check(DailyAbsorptionLog.Read(root, foreign) == null, "absorption reports are scoped to the observed player");
        foreign = Context(nextCycle); foreign["server"] = "other-server";
        Check(DailyAbsorptionLog.Read(root, foreign) == null, "absorption reports are scoped to the game server");
        string queue = DailyQueueEngine.RecordPath(root, Guid.NewGuid().ToString("N"));
        DailyJson.Write(queue, O(("schema", 1), ("state", "partial"), ("context", Context(nextCycle)),
            ("items", new JsonArray(O(("task", "weekly_mainline"), ("state", "partial")), O(("task", "weekly_npc"), ("state", "partial"))))));
        var view = DailyQueueSession.ReadRecord(root, account, queue, now);
        Check(view.Stages[0].Absorptions is { Maps: 1 } && view.Stages[1].Absorptions == null, "queue timeline attaches current-day absorption without replacing NPC task details");
        Check(JsonSerializer.Deserialize<QueueView>(JsonSerializer.Serialize(view))!.Stages[0].Absorptions is { Maps: 1 }, "parallel worker transport retains the map report");
        var stripped = view with { Stages = [view.Stages[0] with { Absorptions = null }], PlanStages = [view.Stages[0] with { Absorptions = null }] };
        var reloaded = DailyAbsorptionLog.Attach(root, stripped);
        Check(reloaded.Stages[0].Absorptions is { Maps: 1 } && reloaded.PlanStages![0].Absorptions is { Maps: 1 },
            "returning from batch history restores the cumulative report in both current plan and last record");
        File.WriteAllText(path, "{\"Account\":\"" + account + "\",\"Player\":null}");
        Check(DailyAbsorptionLog.Read(root, Context(nextCycle)) == null && DailyQueueSession.ReadRecord(root, account, queue, now).Stages.Count == 2,
            "damaged optional report cannot block the queue or its timeline");
        DailyAbsorptionLog.Record(root, next);
        Check(DailyAbsorptionLog.Read(root, Context(nextCycle)) is { Maps: 1 }, "next confirmed action repairs a damaged report");
        Check(Directory.GetFiles(Path.GetDirectoryName(path)!).Length == 1, "one compact report per account does not accumulate daily log files");
    }
}
