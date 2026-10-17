using System.Text.Json;
using System.Text.Json.Nodes;
using Dustweave;
using Dustweave.Pandora;

internal static class MinigameCompletionCases
{
    public static void Run(string root, List<string> cases)
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); cases.Add("minigame-completion: " + name); }
        var control = new Control { RunId = "current", Mode = "Challenge" };
        var target = new Stage { Group = 8, Id = 2, Challenge = true };
        var result = new RoundResult { RunId = "current", Id = 40, Revision = 2, Group = 8, StageId = 2, Challenge = true, ElapsedMilliseconds = 1200000 };
        bool Complete() => result.Completes(control, target, 40, 2);
        Check(Complete(), "native 20-minute settlement completes even when the challenge uses a generic result panel");
        result.ElapsedMilliseconds = 1199999;
        Check(!Complete(), "a failed round just below the limit is not rounded up to a win");
        result.Survived = true;
        Check(Complete(), "native successful settlement wins even when the final clock stops just below the limit");
        result.ElapsedMilliseconds = -1; Check(!Complete(), "invalid native clock cannot certify completion"); result.ElapsedMilliseconds = 1200000;
        control.Mode = "Currency"; Check(!Complete(), "currency farming keeps its repeat behavior"); control.Mode = "Challenge";
        result.Challenge = false; Check(!Complete(), "ordinary stage clear cannot finish challenge mode"); result.Challenge = true;
        result.RunId = "previous"; Check(!Complete(), "previous run cannot finish this run"); result.RunId = "current";
        result.Revision = 1; Check(!Complete(), "reused result window needs the current settlement revision"); result.Revision = 2;
        result.Group = 7; Check(!Complete(), "another map's result cannot finish the selected challenge"); result.Group = 8;
        result.StageId = 1; Check(!Complete(), "another stage's result cannot finish the selected challenge"); result.StageId = 2;
        result.Id = 41; Check(!Complete(), "result must belong to the displayed result window"); result.Id = 40;

        foreach (bool pandora in new[] { true, false })
        {
            string name = pandora ? "pandora" : "mansion", data = Path.Combine(root, "minigame-completion", name);
            var now = DateTimeOffset.UtcNow;
            byte[]? incoming = null;
            Exception? writeError = null, readError = null;
            var writes = new List<JsonObject>();
            byte[]? Read(string mailbox) { if (readError != null) throw readError; return incoming; }
            void Write(string mailbox, byte[] bytes) { if (writeError != null) throw writeError; writes.Add(JsonNode.Parse(bytes)!.AsObject()); }
            var p = pandora ? new PandoraClient(data, Read, Write, () => now) { Mode = "Challenge" } : null;
            var m = pandora ? null : new MansionClient(data, Read, Write, () => now);
            void Start() { if (p != null) p.Start(); else m!.Start(); }
            JsonObject? Poll() => p != null ? p.Poll() : m!.Poll();
            bool Enabled() => p?.Enabled ?? m!.Enabled;
            Start(); string run = writes.Single()["RunId"]!.ToString();
            void Frame(bool finished = false, string? runId = null, int age = 0, bool faulted = false) => incoming = JsonSerializer.SerializeToUtf8Bytes(new {
                At = now.AddSeconds(-age), RunId = runId ?? run, Finished = finished, Faulted = faulted, Reason = finished ? "completed" : "playing", Seconds = 1200
            });
            bool Pending() { try { Poll(); return false; } catch (DailyObservationPendingException) { return true; } }
            Check(Pending() && Enabled() && writes.Count == 1, name + ": missing observation keeps run and sends no heartbeat");
            readError = new TimeoutException("read timeout");
            Check(Pending() && Enabled() && writes.Count == 1, name + ": read timeout waits without renewing or losing control intent");
            readError = new BD2.LocalIpc.GameSessionEndedException("game exited");
            bool ended = false; try { Poll(); } catch (BD2.LocalIpc.GameSessionEndedException) { ended = true; }
            Check(ended, name + ": exited process is not disguised as a recoverable read gap"); readError = null;
            Frame(age: 8); Check(Pending() && Enabled() && writes.Count == 1, name + ": stale observation cannot authorize continued execution");
            Frame(age: -8); Check(Pending() && writes.Count == 1, name + ": future observation cannot authorize execution");
            Frame(); Poll(); Check(Enabled() && writes.Count == 2 && writes.Last()["RunId"]!.ToString() == run, name + ": fresh data resumes the same run");
            writeError = new DailyObservationPendingException("identity-not-ready");
            Check(Pending() && Enabled() && writes.Count == 2, name + ": temporary identity failure does not discard the client or run");
            writeError = null; now = now.AddSeconds(10); Frame(); Poll();
            Check(Enabled(), name + ": recovered same-run observation survives a gap beyond the lease duration");
            Frame(true); int before = writes.Count; Poll();
            Check(!Enabled() && writes.Count == before + 1 && writes.Last()["Enabled"]!.GetValue<bool>() == false, name + ": completion is read before the next heartbeat and sends only disabled control");
            Frame(); Poll(); Check(!Enabled() && writes.Last()["Enabled"]!.GetValue<bool>() == false, name + ": later nonfinal frames cannot automatically restart a completed run");
            string oldRun = run; Start(); run = writes.Last()["RunId"]!.ToString(); Frame(true, oldRun); Poll();
            Check(Enabled() && run != oldRun, name + ": fresh start ignores a previous run's completed frame");
            Frame(faulted: true); Poll(); Check(!Enabled(), name + ": explicit runtime fault still stops the run");
            Start(); run = writes.Last()["RunId"]!.ToString(); now = now.AddSeconds(6); Frame(runId: "unacknowledged");
            bool rejected = false; try { Poll(); } catch (IOException e) when (e is not DailyObservationPendingException) { rejected = true; }
            Check(rejected, name + ": current fresh state that never acknowledges control remains a real error");
        }
    }
}
