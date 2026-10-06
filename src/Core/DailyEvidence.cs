using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Dustweave;

public static class DailyEvidence
{
    public static bool SameActor(JsonObject a, JsonObject b) => new[] { "ProcessId", "ProcessStartTicks", "Instance", "AccountKey", "PlayerKey" }.All(k => a[k] != null && b[k] != null && JsonNode.DeepEquals(a[k], b[k]));
    public static long Integer(JsonNode? node)
    {
        if (node is not JsonValue v)
            throw new InvalidDataException("Missing evidence integer");
        if (v.TryGetValue<long>(out var n))
            return n;
        if (v.TryGetValue<int>(out var small))
            return small;
        throw new InvalidDataException("Invalid evidence integer");
    }
    public static JsonNode? Reading(JsonObject evidence, string id, string path)
    {
        var rows = evidence["Readings"]!.AsArray().Select(r => r!.AsObject()).Where(r => r["Id"]!.GetValue<string>() == id).ToArray();
        if (rows.Length != 1 || rows[0]["Error"]?.GetValue<string>() != "")
            throw new InvalidDataException("Reading unavailable: " + id);
        var values = rows[0]["Values"]!.AsArray().Select(r => r!.AsObject()).Where(r => r["Path"]!.GetValue<string>() == path).ToArray();
        if (values.Length != 1 || values[0]["Error"]?.GetValue<string>() != "")
            throw new InvalidDataException("Reading unavailable: " + id + "." + path);
        return JsonNode.Parse(values[0]["Json"]!.GetValue<string>());
    }
    public static JsonObject Values(JsonObject evt)
    {
        var result = new JsonObject();
        foreach (var node in evt["Values"]!.AsArray())
        {
            var value = node!.AsObject();
            string key = value["Path"]!.GetValue<string>();
            if (value["Error"]?.GetValue<string>() != "" || result.ContainsKey(key))
                throw new InvalidDataException("Unreadable or duplicate response evidence");
            result[key] = JsonNode.Parse(value["Json"]!.GetValue<string>());
        }
        return result;
    }
    public static JsonObject VerifyRoom(JsonObject before, JsonArray events, JsonObject after)
    {
        var frame = before["Frame"]!.AsObject();
        if (!JsonNode.DeepEquals(before["Config"], after["Config"]))
            throw new InvalidDataException("Evidence configuration changed during task");
        var rows = events.Select(e => e!.AsObject()).Where(e => e["Role"]!.GetValue<string>() == "room.info").ToArray();
        var requests = rows.Where(e => e["Kind"]!.GetValue<string>() == "request").ToArray();
        var responses = rows.Where(e => e["Kind"]!.GetValue<string>() == "response").ToArray();
        if (requests.Length != 1 || responses.Length != 1)
            throw new InvalidDataException("Expected one request and one response");
        var response = responses[0];
        if (response["Error"]?.GetValue<string>() != "" || Integer(response["ErrorCode"]) != 0 || response["Accepted"]?.GetValue<bool>() != true)
            throw new InvalidDataException("Native response failed");
        if (Integer(requests[0]["Sequence"]) >= Integer(response["Sequence"]) || rows.Any(e => !SameActor(e["Frame"]!.AsObject(), frame)) || !SameActor(after["Frame"]!.AsObject(), frame))
            throw new InvalidDataException("Response identity or ordering mismatch");
        var data = Values(response);
        long owner = Integer(data["RoomInfo.OwnerIndex"]);
        if (DailyIdentity.PlayerKey(frame["AccountKey"]!.GetValue<string>(), owner) != frame["PlayerKey"]!.GetValue<string>() || Integer(Reading(after, "room.cache", "OwnerIndex")) != owner)
            throw new InvalidDataException("Room owner or native cache mismatch");
        return new()
        {
            ["outcome"] = "visited_own_room",
            ["owner_matched"] = true,
            ["cache_matched"] = true,
            ["reward_claimed"] = false
        };
    }
}
public sealed partial class DailyCommandDriver
{
    private JsonObject? demand;
    public Task<DailyStageFrame> ObserveAsync() => ReadBound();
    public long UtcTicks => now();
    public async Task<JsonObject> DemandAsync(IEnumerable<string> prefixes)
    {
        var observed = await ReadBound();
        if (stopped() || mailbox.Read("live", "pause") != null)
            throw new StageHostException("stopped", "Stopped before evidence request");
        await ReviewTradeOnDemandAsync(observed);
        var names = prefixes.Append("reward.presentation").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (names.Length == 0 || names.Any(p => string.IsNullOrEmpty(p) || p.Length > 1024))
            throw new StageHostException("protocol", "Invalid evidence scope");
        var array = new JsonArray(names.Select(p => (JsonNode)JsonValue.Create(p)!).ToArray());
        // Compare the actual mailbox too: a stage change must not leave a child waiting for an obsolete ID.
        var currentBytes = mailbox.Read("live", "observation-request.json");
        var current = currentBytes == null ? null : JsonNode.Parse(currentBytes)?.AsObject();
        if (demand == null || !JsonNode.DeepEquals(demand["Prefixes"], array) || DailyEvidence.Integer(demand["ExpiresUtcTicks"]) - now() < 5 * TimeSpan.TicksPerSecond || current?["Id"]?.GetValue<string>() != demand["Id"]?.GetValue<string>())
        {
            demand = new()
            {
                ["Id"] = Guid.NewGuid().ToString("N"),
                ["Prefixes"] = array,
                ["ExpiresUtcTicks"] = now() + 15 * TimeSpan.TicksPerSecond
            };
            mailbox.Write("live", "observation-request.json", JsonSerializer.SerializeToUtf8Bytes(demand));
        }
        return demand.DeepClone().AsObject();
    }
    public void ReleaseDemand(string id)
    {
        Active();
        if (demand?["Id"]?.GetValue<string>() != id)
            return;
        var raw = mailbox.Read("live", "observation-request.json");
        if (raw != null && JsonNode.Parse(raw)?["Id"]?.GetValue<string>() == id)
            mailbox.Delete("live", "observation-request.json");
        demand = null;
    }
    public async Task<JsonObject> EvidenceAsync(IEnumerable<string> prefixes)
    {
        var scope = prefixes.ToArray();
        var bound = await ReadBound();
        var request = await DemandAsync(scope);
        double started = clock(), end = started + 3;
        JsonObject? firstWaiting = null, last = null;
        string outcome = "ready";
        bool resynchronized = false;
        void CheckStop()
        {
            if (stopped() || mailbox.Read("live", "pause") != null)
                throw new StageHostException("stopped", "Stopped while reading scoped evidence");
        }
        try
        {
            while (clock() < end)
            {
                CheckStop();
                var bytes = mailbox.Read("live", "evidence.json");
                if (bytes != null)
                {
                    var evidence = JsonNode.Parse(bytes)!.AsObject();
                    if (evidence["ObservationRequest"]?.GetValue<string>() == request["Id"]!.GetValue<string>())
                    {
                        var frame = evidence["Frame"]?.AsObject() ?? throw new StageHostException("adapter", "Evidence frame is missing");
                        last = EvidenceFrameSummary(frame);
                        // Check real actor changes even when the observer also reports a transient error.
                        bool ready = EvidenceFrameReady(frame, bound.Frame);
                        string error = evidence["Error"]?.GetValue<string>() ?? "missing error state";
                        if (error.Length > 0 && !error.StartsWith("System.IO.IOException:", StringComparison.Ordinal))
                            throw new StageHostException("adapter", "Evidence observer is not ready: " + error);
                        long age = now() - DailyEvidence.Integer(evidence["AtUtcTicks"]);
                        long frameAge = now() - DailyEvidence.Integer(frame["AtUtcTicks"]);
                        if (age < 0 || frameAge < 0)
                            throw new StageHostException("adapter", "Evidence observation time is invalid");
                        if (ready && error.Length == 0 && age <= 3 * TimeSpan.TicksPerSecond && frameAge <= 3 * TimeSpan.TicksPerSecond)
                        {
                            // Recheck queue account, connection and reset after waiting, before exposing business data.
                            await ReadBound();
                            CheckStop();
                            if (now() - DailyEvidence.Integer(evidence["AtUtcTicks"]) <= 3 * TimeSpan.TicksPerSecond
                                && now() - DailyEvidence.Integer(frame["AtUtcTicks"]) <= 3 * TimeSpan.TicksPerSecond)
                                return evidence.DeepClone().AsObject();
                        }
                        if (!ready)
                        {
                            firstWaiting ??= last.DeepClone().AsObject();
                            if (!resynchronized)
                            {
                                resynchronized = true;
                                // Use the existing bounded identity observer once, without input or request replay.
                                await ReadBound();
                                CheckStop();
                                request = await DemandAsync(scope); // Renew a scope that expired during scene loading.
                                end = clock() + 3;
                            }
                        }
                    }
                }
                await delay(TimeSpan.FromMilliseconds(100));
            }
            throw new StageHostException("adapter", resynchronized
                ? "切场后证据仍未就绪，已保留进度且未重发操作；可仅重跑本环节。"
                : "Scoped observation did not become ready");
        }
        catch (Exception error) { outcome = error.Message; throw; }
        finally
        {
            if (firstWaiting != null || outcome != "ready")
                SaveEvidenceObservation(outcome == "ready" ? "scene_identity_resynchronized" : outcome, bound.Frame, firstWaiting, last, request, clock() - started);
        }
    }
    public JsonArray CollectEvents(string role, long since, long? until = null)
    {
        Active();
        var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        string archive = Path.Combine(root, "live", "event-journal");
        bool Selected(string name, out string file)
        {
            file = name.StartsWith("events~", StringComparison.Ordinal) ? name[7..] : name;
            if (!file.EndsWith(".json", StringComparison.Ordinal) || Path.GetFileName(file) != file || !long.TryParse(file.Split('-', 2)[0], NumberStyles.None, CultureInfo.InvariantCulture, out long at))
                throw new InvalidDataException("Invalid native event journal name");
            return at >= since && (!until.HasValue || at <= until.Value);
        }
        foreach (string name in mailbox.List("live", "events~"))
        {
            if (!Selected(name, out var file))
                continue;
            var bytes = mailbox.Read("live", name);
            if (bytes == null)
                continue;
            var evt = JsonNode.Parse(bytes)!.AsObject();
            if (evt["Role"]?.GetValue<string>() != role)
                continue;
            string saved = Path.Combine(archive, file);
            if (!File.Exists(saved))
                DailyJson.Write(saved, evt);
            result[file] = evt;
        }
        if (Directory.Exists(archive))
            foreach (string path in Directory.EnumerateFiles(archive, "*.json"))
            {
                string file = Path.GetFileName(path);
                if (result.ContainsKey(file) || !Selected(file, out _))
                    continue;
                var evt = DailyJson.TryRead<JsonObject>(path) ?? throw new InvalidDataException("Unreadable native event journal");
                if (evt["Role"]?.GetValue<string>() == role)
                    result[file] = evt;
            }
        return new JsonArray(result.Values.OrderBy(e => DailyEvidence.Integer(e["Sequence"])).Select(e => (JsonNode)e.DeepClone()).ToArray());
    }
}

