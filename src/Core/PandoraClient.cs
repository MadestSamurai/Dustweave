using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using BD2.LocalIpc;

namespace Dustweave;
/// <summary>Hosted transport only: never launches, injects, or reconnects to another game.</summary>
public sealed class PandoraClient
{
    readonly Func<string, byte[]?> read;
    readonly Action<string, byte[]> write;
    readonly Func<DateTimeOffset> utcNow = () => DateTimeOffset.UtcNow;
    readonly string diagnostics;
    readonly JsonArray transitions = new();
    string lastDiagnostic = "";
    string runId = Guid.NewGuid().ToString("N");
    DateTimeOffset started;
    public bool Enabled { get; private set; }
    public string Mode { get; set; } = "Currency";
    public int StageId { get; set; }
    public int StageGroup { get; set; }
    public bool AutoSelectEquipment { get; set; } = true;
    // Opt-in, bounded IPC snapshot for local calibration; not retained by normal diagnostics.
    public bool TraceCombat { get; set; }
    public int CharacterGroup { get; set; }
    public int CharacterId { get; set; }
    public bool ForecastMovement { get; set; } = true;


    public PandoraClient(string root, int pid, long start, string fingerprint)
    {
        if (pid <= 0 || start <= 0 || string.IsNullOrWhiteSpace(fingerprint))
            throw new InvalidOperationException("Invalid hosted game identity");
        if (Environment.GetEnvironmentVariable("BD2_DAILY_HOSTED_TOOL") != "pandora" || Environment.GetEnvironmentVariable("BD2_DAILY_GAME_PID") != pid.ToString() || Environment.GetEnvironmentVariable("BD2_DAILY_GAME_START") != start.ToString())
            throw new InvalidOperationException("Changed hosted game identity");
        diagnostics = Path.Combine(root, "tools");
        var pipe = new PipeClient(Path.Combine(root, "pandora"), pid, start);
        pipe.Open(fingerprint);
        read = pipe.Read; write = (name, bytes) => pipe.Write(name, bytes);
    }

    internal PandoraClient(string root, Func<string, byte[]?> read, Action<string, byte[]> write, Func<DateTimeOffset> clock)
    {
        diagnostics = Path.Combine(root, "tools"); this.read = read; this.write = write; utcNow = clock;
    }

    public void Start()
    {
        if (Mode is not ("Currency" or "Challenge") || Mode == "Currency" && (StageId <= 0 || StageGroup <= 0))
            throw new InvalidOperationException("Choose an unlocked Pandora stage first");
        runId = Guid.NewGuid().ToString("N");
        started = utcNow();
        Enabled = true;
        try
        {
            Write();
        }
        catch
        {
            Enabled = false;
            throw;
        }
    }

    public void Stop()
    {
        Enabled = false;
        Write();
    }

    void Write() => write("control.json", JsonSerializer.SerializeToUtf8Bytes(new { Enabled, RunId = runId, Mode, StageId, StageGroup, AutoSelectEquipment, TraceCombat, CharacterGroup, CharacterId, ForecastMovement, Expires = utcNow().UtcDateTime.AddSeconds(4).Ticks }));
    public JsonObject? Poll()
    {
        byte[]? bytes;
        try { bytes = read("state.json"); }
        catch (Exception e) when (e is IOException or TimeoutException && e is not InvalidDataException && !PipeClient.SessionEnded(e))
        { throw new DailyObservationPendingException("minigame-read-unavailable", e.Message); }
        if (bytes == null) throw new DailyObservationPendingException("minigame-snapshot-missing");
        var state = JsonNode.Parse(bytes)?.AsObject() ?? throw new InvalidDataException("Invalid minigame state");
        if (!DateTimeOffset.TryParse(state["At"]?.ToString(), out var at) || at < utcNow().AddSeconds(-5) || at > utcNow().AddSeconds(2))
            throw new DailyObservationPendingException("minigame-snapshot-stale");
        Record(state);
        if (Enabled && utcNow() - started > TimeSpan.FromSeconds(5) && state["RunId"]?.ToString() != runId)
            throw new IOException("Minigame control was not acknowledged");
        if (state["RunId"]?.ToString() == runId && (state["Finished"]?.GetValue<bool>() == true || state["Faulted"]?.GetValue<bool>() == true))
            Enabled = false;
        Write();
        return state;
    }

    internal static JsonObject DiagnosticState(JsonObject state)
    {
        var result = new JsonObject();
        foreach (var key in new[] { "At", "Pid", "RunId", "Running", "Finished", "Faulted", "Reason", "Detail", "Completed", "Result", "ResultError", "Seconds", "Ui", "Stage", "Mode", "AutoSelectEquipment", "Character", "RequestedCharacter", "Characters", "Selection", "Surfaces", "Entry", "TransitionUi", "Movement", "Tactics", "Build" })
            result[key] = state[key]?.DeepClone();
        return result;
    }

    internal static void AppendTransition(JsonArray history, JsonObject record)
    {
        history.Add(record.DeepClone());
        while (history.Count > 64) history.RemoveAt(0);
    }

    void Record(JsonObject state)
    {
        var record = DiagnosticState(state);
        var key = record.DeepClone().AsObject();
        key.Remove("At");
        key.Remove("Seconds"); // Keep final time in evidence without writing a log every clock tick.
        key.Remove("Movement"); // Attach the decision to transitions, not a new log per plan.
        key.Remove("Tactics");
        string signature = key.ToJsonString();
        if (signature == lastDiagnostic) return;
        try
        {
            // Bounded state changes only; no per-frame positions, health or loot.
            AppendTransition(transitions, record);
            DailyJson.Write(Path.Combine(diagnostics, "pandora-transitions.json"), transitions);
            DailyJson.Write(Path.Combine(diagnostics, "pandora-state.json"), record);
            if (state["Faulted"]?.GetValue<bool>() == true)
                DailyJson.Write(Path.Combine(diagnostics, "pandora-runtime-error.json"), record);
            lastDiagnostic = signature;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { /* A log directory problem must not interrupt a live round. */ }
    }
}
