using BD2Daily;
using System.Text.Json.Nodes;
using static BD2Daily.DailyData;

static class BusinessJournalCases
{
    public static Task Run(string output, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        var proof = new DailyBusinessProof("cafeteria.event", "cafeteria_guests", ["cafeteria"], (_, _, _) => new());
        using var f = new WorkflowHarness(Path.Combine(output, "business-journal"), [proof]);
        var op = f.Business.Create(f.Context, proof.Role, f.Evidence(), new(), O(("ui", "OverheadManageUI")));
        string file = Path.Combine(f.Root, "live", "managed-business", S(op["id"]) + ".json");
        void Changed(JsonObject value) { DailyJson.Write(file, value); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(2)); }
        bool Blocked(DailyManagedBusiness business) {
            try { business.RequireResolved(f.Context, proof.Role); return false; }
            catch (StageHostException e) when (e.Kind == "pending") { return true; }
        }
        Check(Blocked(f.Business), "locally saved pending operation remains blocked by compact journal");
        op["state"] = "completed"; f.Business.Save(op);
        f.Business.RequireResolved(f.Context, proof.Role);
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            f.Business.RequireResolved(f.Context, proof.Role);
        Check(true, "unchanged completed journal does not reopen captured evidence files");
        op["state"] = "unknown"; Changed(op);
        Check(Blocked(f.Business), "external update from completed to unknown invalidates journal header");
        op["state"] = "completed"; Changed(op); f.Business.RequireResolved(f.Context, proof.Role);
        Check(true, "external reconciliation from unknown to completed is visible without restart");
        var second = op.DeepClone().AsObject(); second["id"] = Guid.NewGuid().ToString("N"); second["state"] = "unknown";
        string secondFile = Path.Combine(Path.GetDirectoryName(file)!, S(second["id"]) + ".json");
        DailyJson.Write(secondFile, second);
        Check(Blocked(f.Business), "new pending record is found after an earlier clean check");
        second["account"] = "another-account"; DailyJson.Write(secondFile, second);
        f.Business.RequireResolved(f.Context, proof.Role);
        Check(true, "other-account pending operation does not block current account");
        second["account"] = Copy(f.Context["actor"]![3]); second["server"] = "another-server"; DailyJson.Write(secondFile, second);
        f.Business.RequireResolved(f.Context, proof.Role);
        Check(true, "other-server pending operation stays isolated");
        second["server"] = Copy(f.Context["server"]); second["cycle"] = "old-cycle"; DailyJson.Write(secondFile, second);
        var restarted = new DailyManagedBusiness(f.Root, f.Driver, [proof], () => false);
        Check(Blocked(restarted), "fresh process still detects unresolved history across cycle changes");
        File.Delete(secondFile); f.Business.RequireResolved(f.Context, proof.Role);
        Check(true, "removed journal entries do not leave stale pending state");
        string legacy = Path.Combine(f.Root, "live", "business", S(op["id"]) + ".json");
        DailyJson.Write(legacy, op); f.Business.RequireResolved(f.Context, proof.Role);
        Check(true, "identical historical copies remain readable");
        var conflict = op.DeepClone().AsObject(); conflict["result"] = O(("different", true)); DailyJson.Write(legacy, conflict);
        Check(Blocked(f.Business), "same-header copies with conflicting evidence still block");
        File.Delete(legacy);
        op["state"] = "unknown"; f.Business.Save(op);
        Check(Blocked(f.Business), "local save invalidates a formerly completed cached entry");
        op["state"] = "completed"; f.Business.Save(op);
        File.WriteAllText(file, "invalid json");
        bool invalid = false; try { f.Business.RequireResolved(f.Context, proof.Role); } catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException) { invalid = true; }
        Check(invalid && f.Box.Commands.Count == 0, "unreadable changed record cannot be treated as a successful claim");
        RecordQueries(output, cases);
        EquipmentSources(output, cases);
        return Task.CompletedTask;
    }
    private static void RecordQueries(string output, List<string> cases)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); cases.Add(name); }
        var proofs = new[] {
            new DailyBusinessProof("cafeteria.event", "cafeteria_guests", ["cafeteria"], (_, _, _) => new()),
            new DailyBusinessProof("mirror.end", "mirror", ["mirror"], (_, _, _) => new()),
            new DailyBusinessProof("event_rewards.1", "event_rewards", ["events"], (_, _, _) => new())
        };
        using var f = new WorkflowHarness(Path.Combine(output, "journal-queries"), proofs);
        JsonObject Completed(string role) {
            var op = f.Business.Create(f.Context, role, f.Evidence(), new(), new());
            op["state"] = "completed"; f.Business.Save(op); return op;
        }
        string FileFor(JsonObject op, string dir = "managed-business") => Path.Combine(f.Root, "live", dir, S(op["id"]) + ".json");
        bool Conflict(Action read) { try { read(); return false; } catch (StageHostException e) when (e.Kind == "pending") { return true; } }
        var restaurant = Completed("cafeteria.event");
        var mirror = Completed("mirror.end");
        mirror["cycle"] = "old-cycle"; mirror["result"] = O(("wins", 1)); f.Business.Save(mirror);
        Check(f.Business.Records(f.Context).Count() == 2, "public record query preserves completed and old-cycle history");
        using (var locked = new FileStream(FileFor(restaurant), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check(f.Business.Records(f.Context, "mirror.end").Single()["result"]?["wins"]?.GetValue<int>() == 1, "role query does not reopen unrelated historical evidence");
        var foreign = mirror.DeepClone().AsObject(); foreign["id"] = Guid.NewGuid().ToString("N"); foreign["account"] = "other"; f.Business.Save(foreign);
        using (var locked = new FileStream(FileFor(foreign), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check(f.Business.Records(f.Context, "mirror.end").Count() == 1, "account query filters unrelated evidence before opening its body");
        f.Stopped = true;
        Check(f.Business.Records(f.Context, "mirror.end").Count() == 1, "read-only records remain available after automation is stopped");
        f.Stopped = false;
        var copy = f.Business.Records(f.Context, "mirror.end").Single(); copy["result"]!["wins"] = 999;
        Check(N(f.Business.Records(f.Context, "mirror.end").Single()["result"]?["wins"]) == 1, "query results are fresh bodies and cannot mutate cached evidence");
        mirror["result"]!["wins"] = 2; DailyJson.Write(FileFor(mirror), mirror);
        Check(N(f.Business.Records(f.Context, "mirror.end").Single()["result"]?["wins"]) == 2, "external body-only update is observed even with unchanged routing fields");
        foreign["account"] = Copy(f.Context["actor"]![3]); DailyJson.Write(FileFor(foreign), foreign);
        Check(f.Business.Records(f.Context, "mirror.end").Count() == 2, "external account change refreshes cached routing");
        foreign["server"] = "other-server"; DailyJson.Write(FileFor(foreign), foreign);
        Check(f.Business.Records(f.Context, "mirror.end").Count() == 1, "external server change excludes the former match");
        foreign["server"] = Copy(f.Context["server"]); foreign["player"] = "other-player"; DailyJson.Write(FileFor(foreign), foreign);
        Check(f.Business.Records(f.Context, "mirror.end").Count() == 1, "same-account different-player evidence remains isolated");
        DailyJson.Write(FileFor(mirror, "business"), mirror);
        Check(f.Business.Records(f.Context, "mirror.end").Count() == 1, "identical legacy records are returned only once");
        var conflict = mirror.DeepClone().AsObject(); conflict["result"]!["wins"] = 3; DailyJson.Write(FileFor(mirror, "business"), conflict);
        Check(Conflict(() => f.Business.Records(f.Context, "mirror.end").ToArray()), "public record query still rejects same-header conflicting evidence");
        Check(f.Business.Records(f.Context, "mirror.end", false).Count() == 1, "exclude-legacy option keeps its original meaning");
        File.Delete(FileFor(mirror, "business")); File.Delete(FileFor(mirror));
        Check(!f.Business.Records(f.Context, "mirror.end").Any(), "deleted matching record is not returned from memory");

        f.Page("EventUI");
        var eventOp = Completed("event_rewards.1"); eventOp["presentation"] = "pending"; f.Business.Save(eventOp);
        var frame = new DailyStageFrame(f.Frame.DeepClone().AsObject(), f.Context.DeepClone().AsObject());
        using (var locked = new FileStream(FileFor(restaurant), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check(DailyEventRewards.HasPendingPresentation(f.Business, frame), "event presentation lookup reads only matching event bodies");
        eventOp["presentation"] = "settled"; f.Business.Save(eventOp);
        using (var locked = new FileStream(FileFor(eventOp), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check(!DailyEventRewards.HasPendingPresentation(f.Business, frame), "settled presentation is filtered without reopening evidence");
        string eventFile = FileFor(eventOp);
        var beforeRewrite = new FileInfo(eventFile); long previousSize = beforeRewrite.Length;
        var previousWritten = beforeRewrite.LastWriteTimeUtc; var previousCreated = beforeRewrite.CreationTimeUtc;
        eventOp["presentation"] = "pending"; DailyJson.Write(eventFile, eventOp);
        File.SetLastWriteTimeUtc(eventFile, previousWritten); File.SetCreationTimeUtc(eventFile, previousCreated);
        var afterRewrite = new FileInfo(eventFile);
        Check(afterRewrite.Length == previousSize && afterRewrite.LastWriteTimeUtc == previousWritten && afterRewrite.CreationTimeUtc == previousCreated,
            "atomic replacement fixture retains identical size and timestamps");
        Check(DailyEventRewards.HasPendingPresentation(f.Business, frame), "external pending presentation becomes visible immediately");
        eventOp["cycle"] = "old-cycle"; f.Business.Save(eventOp);
        using (var locked = new FileStream(FileFor(eventOp), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Check(!DailyEventRewards.HasPendingPresentation(f.Business, frame), "old-cycle presentation is excluded before reading its body");
        var registry = new DailyWorkflowRegistry(f.Root, TestPaths.SourceRoot, f.Driver, f.Business, null!, proofs, () => false, _ => {}, () => null);
        var stage = registry.Stages()["event_rewards"];
        f.Page("MessagePopupUI"); eventOp["cycle"] = Copy(f.Context["cycle"]); eventOp["state"] = "preview_ready"; eventOp["preview_frame"] = f.Frame.DeepClone(); f.Business.Save(eventOp);
        Check(stage.CanResume(new(f.Frame.DeepClone().AsObject(), f.Context.DeepClone().AsObject())), "matching event preview still resumes through current dialog ownership");
        f.Page("MainMenuUI"); File.WriteAllText(FileFor(restaurant), "invalid json");
        Check(!stage.CanResume(new(f.Frame.DeepClone().AsObject(), f.Context.DeepClone().AsObject())), "unrelated page does not inspect historical event records at all");
        bool invalid = false; try { f.Business.Records(f.Context).ToArray(); } catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException) { invalid = true; }
        Check(invalid && f.Box.Commands.Count == 0, "changed unreadable history still fails an actual record query without any input");
    }
    private static void EquipmentSources(string output, List<string> cases)
    {
        using var f = new WorkflowHarness(Path.Combine(output, "journal-equipment-sources"), DailyEquipment.Proofs().ToArray());
        JsonObject Completed(string role, params string[] ids) {
            var op = f.Business.Create(f.Context, role, f.Evidence(), new(), new());
            op["state"] = "completed"; op["result"] = O(("inventory_matched", true), ("instances", ids));
            f.Business.Save(op); return op;
        }
        Completed("equipment.craft", "10", "20", "30");
        Completed("equipment.recycle", "10");
        var broken = Completed("equipment.break", "20");
        var draw = typeof(DailyEquipment).GetMethod("Drawn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var remaining = (string[]?)draw.Invoke(null, [f.Workflow]);
        if (remaining == null || !remaining.SequenceEqual(["30"])) throw new Exception("Equipment journal omitted already consumed sources");
        cases.Add("equipment source query keeps both recycle and break history so consumed gear is not reused");
        broken["state"] = "unknown"; f.Business.Save(broken);
        bool blocked = false;
        try { draw.Invoke(null, [f.Workflow]); }
        catch (System.Reflection.TargetInvocationException e) when (e.InnerException is InvalidDataException) { blocked = true; }
        if (!blocked) throw new Exception("Unknown recycle receipt did not block equipment reuse");
        cases.Add("equipment source query still blocks unknown consumption before reuse");
    }
}