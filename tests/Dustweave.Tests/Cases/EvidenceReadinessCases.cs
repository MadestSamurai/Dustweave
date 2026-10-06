using Dustweave;
using System.Text.Json;
using System.Text.Json.Nodes;

static class EvidenceReadinessCases
{
    private sealed class Fixture : IDisposable
    {
        public readonly CommandDriverCases.Mailbox Box = new();
        public readonly DailyCommandDriver Driver;
        public readonly string Root;
        public double Time;
        public bool Stopped;
        public int Reads;
        public Action<Fixture>? OnRead, OnDelay;
        public Action<JsonObject>? ChangeEvidence;
        public JsonObject Context = CommandDriverCases.Context();
        public long Ticks => 638948160000000000L + (long)(Time * TimeSpan.TicksPerSecond);
        public Fixture(string root)
        {
            Root = root;
            Box.AfterWrite = (_, name, _) => { if (name == "observation-request.json") Publish(); };
            Driver = new(root, Box, () =>
            {
                Reads++;
                OnRead?.Invoke(this);
                var frame = CommandDriverCases.Frame(); frame["AtUtcTicks"] = Ticks;
                return Task.FromResult(new DailyStageFrame(frame, Context.DeepClone().AsObject()));
            }, () => Stopped, () => Ticks, () => Time, t => { Time += t.TotalSeconds; OnDelay?.Invoke(this); Publish(); return Task.CompletedTask; });
            Driver.Bind(Context);
        }
        public void Publish()
        {
            if (!Box.Values.TryGetValue("live:observation-request.json", out var bytes)) return;
            var frame = CommandDriverCases.Frame(); frame["AtUtcTicks"] = Ticks; frame["Error"] = "";
            var e = new JsonObject { ["ObservationRequest"] = JsonNode.Parse(bytes)!["Id"]!.DeepClone(), ["Frame"] = frame, ["AtUtcTicks"] = Ticks, ["Error"] = "", ["Readings"] = new JsonArray() };
            ChangeEvidence?.Invoke(e);
            Box.Values["live:evidence.json"] = JsonSerializer.SerializeToUtf8Bytes(e);
        }
        public void Dispose() => Driver.Dispose();
    }
    private static void Waiting(JsonObject e)
    {
        e["Frame"]!["Scene"] = "Map3001_001";
        e["Frame"]!["AccountKey"] = "";
        e["Frame"]!["PlayerKey"] = "";
        e["Frame"]!["Error"] = "Waiting for fresh account identity";
    }
    public static async Task Run(string root, List<string> cases)
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); cases.Add(label); }
        async Task<string> Error(Fixture f)
        {
            try { await f.Driver.EvidenceAsync(["mirror"]); return ""; }
            catch (StageHostException e) { return e.Kind; }
            catch (DailyStepException e) { return e.Kind; }
        }
        using (var f = new Fixture(Path.Combine(root, "evidence-scene-race")))
        {
            f.ChangeEvidence = e => { if (f.Time < .3) Waiting(e); };
            var e = await f.Driver.EvidenceAsync(["mirror"]);
            Check(DailyEvidence.SameActor(e["Frame"]!.AsObject(), CommandDriverCases.Frame()), "scene transition waits for complete same-actor evidence instead of false account-change failure");
            Check(f.Time >= .3 && f.Time < 1 && f.Box.Commands.Count == 0, "identity resynchronization sends no gameplay or delayed fixed sleep");
            var diagnostic = Directory.GetFiles(Path.Combine(f.Root, "live", "evidence-observations")).Single();
            Check(File.ReadAllText(diagnostic).Contains("scene_identity_resynchronized") && File.ReadAllText(diagnostic).Contains("Map3001_001"), "recovered evidence records both transition and ready identity without private readings");
        }
        using (var f = new Fixture(Path.Combine(root, "evidence-renew")))
        {
            f.ChangeEvidence = e => { if (f.Time == 0) Waiting(e); };
            string original = "";
            f.OnRead = s => { if (s.Reads == 3) { original = JsonNode.Parse(s.Box.Values["live:observation-request.json"])!["Id"]!.GetValue<string>(); s.Time += 16; } };
            var e = await f.Driver.EvidenceAsync(["mirror"]);
            Check(e["ObservationRequest"]!.GetValue<string>() != original && f.Time >= 16 && f.Box.Commands.Count == 0, "expired observation scope is renewed after bounded scene identity recovery");
        }
        foreach (string key in new[] { "ProcessId", "ProcessStartTicks", "Instance", "AccountKey", "PlayerKey" })
        using (var f = new Fixture(Path.Combine(root, "evidence-wrong-" + key)))
        {
            f.ChangeEvidence = e => { Waiting(e); e["Frame"]![key] = key is "ProcessId" or "ProcessStartTicks" ? JsonValue.Create(999) : JsonValue.Create("different"); };
            Check(await Error(f) == "identity" && f.Time == 0 && f.Box.Commands.Count == 0, "real " + key + " change is never hidden by scene recovery");
        }
        using (var f = new Fixture(Path.Combine(root, "evidence-permanent-wait")))
        {
            f.ChangeEvidence = Waiting;
            Check(await Error(f) == "adapter" && f.Time >= 3 && f.Time < 3.2 && f.Box.Commands.Count == 0, "permanently incomplete evidence stops with bounded diagnostic and no input");
        }
        foreach (bool pause in new[] { false, true })
        using (var f = new Fixture(Path.Combine(root, "evidence-stop-" + pause)))
        {
            f.ChangeEvidence = Waiting;
            f.OnDelay = s => { if (pause) s.Box.Values["live:pause"] = []; else s.Stopped = true; };
            Check(await Error(f) == "stopped" && f.Time < .2 && f.Box.Commands.Count == 0, "scene recovery honors " + (pause ? "native pause" : "operator stop"));
        }
        using (var f = new Fixture(Path.Combine(root, "evidence-cycle-change")))
        {
            f.OnRead = s => { if (s.Reads == 3) s.Context["cycle"] = "tomorrow"; };
            Check(await Error(f) == "identity" && f.Box.Commands.Count == 0, "even ready evidence is rejected when queue reset changes before acceptance");
        }
        using (var f = new Fixture(Path.Combine(root, "evidence-ready-aged-during-guard")))
        {
            f.OnRead = s => { if (s.Reads == 3) s.Time += 4; };
            Check(await Error(f) == "adapter", "evidence that ages out while validating context is never returned");
        }
        foreach (string kind in new[] { "outer-stale", "frame-stale", "old-request", "io" })
        using (var f = new Fixture(Path.Combine(root, "evidence-fresh-" + kind)))
        {
            f.ChangeEvidence = e => { if (f.Time >= .2) return; if (kind == "outer-stale") e["AtUtcTicks"] = f.Ticks - 4 * TimeSpan.TicksPerSecond; if (kind == "frame-stale") e["Frame"]!["AtUtcTicks"] = f.Ticks - 4 * TimeSpan.TicksPerSecond; if (kind == "old-request") e["ObservationRequest"] = "previous"; if (kind == "io") e["Error"] = "System.IO.IOException: transient"; };
            Check(await Error(f) == "" && f.Time >= .2 && f.Box.Commands.Count == 0, "scoped observation waits for fresh " + kind + " without replay");
        }
        foreach (string kind in new[] { "unknown-error", "missing-identity", "future" })
        using (var f = new Fixture(Path.Combine(root, "evidence-invalid-" + kind)))
        {
            f.ChangeEvidence = e => { if (kind == "unknown-error") e["Frame"]!["Error"] = "Unknown observer failure"; if (kind == "missing-identity") e["Frame"]!["AccountKey"] = ""; if (kind == "future") e["AtUtcTicks"] = f.Ticks + 1; };
            Check(await Error(f) == "adapter" && f.Time == 0, "invalid evidence " + kind + " does not enter scene recovery");
        }
        using (var f = new Fixture(Path.Combine(root, "evidence-lease-revoked")))
        {
            f.ChangeEvidence = Waiting;
            f.OnDelay = s => s.Box.Revoked = true;
            bool rejected = false;
            try { await f.Driver.EvidenceAsync(["mirror"]); } catch (BD2.LocalIpc.LeaseRevokedException) { rejected = true; }
            Check(rejected && f.Box.Commands.Count == 0, "hook handoff revokes observation recovery without taking over control");
        }
    }
}
