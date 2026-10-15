using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using BD2.LocalIpc;

namespace Dustweave;
/// <summary>Hosted transport only: never launches, injects, or reconnects to another game.</summary>
public sealed class PandoraClient
{
    readonly PipeClient pipe;
    readonly string diagnostics;
    readonly JsonArray transitions = new();
    string lastDiagnostic = "";
    string runId = Guid.NewGuid().ToString("N");
    DateTimeOffset lastObservation = DateTimeOffset.UtcNow, started;
    public bool Enabled { get; private set; }
    public string Mode { get; set; } = "Currency";
    public int StageId { get; set; }
    public int StageGroup { get; set; }
    public bool AutoSelectEquipment { get; set; } = true;


    public PandoraClient(string root, int pid, long start, string fingerprint)
    {
        if (pid <= 0 || start <= 0 || string.IsNullOrWhiteSpace(fingerprint))
            throw new InvalidOperationException("Invalid hosted game identity");
        if (Environment.GetEnvironmentVariable("BD2_DAILY_HOSTED_TOOL") != "pandora" || Environment.GetEnvironmentVariable("BD2_DAILY_GAME_PID") != pid.ToString() || Environment.GetEnvironmentVariable("BD2_DAILY_GAME_START") != start.ToString())
            throw new InvalidOperationException("Changed hosted game identity");
        diagnostics = Path.Combine(root, "tools");
        pipe = new PipeClient(Path.Combine(root, "pandora"), pid, start);
        pipe.Open(fingerprint);
    }

    public void Start()
    {
        if (Mode is not ("Currency" or "Challenge") || Mode == "Currency" && (StageId <= 0 || StageGroup <= 0))
            throw new InvalidOperationException("Choose an unlocked Pandora stage first");
        runId = Guid.NewGuid().ToString("N");
        started = DateTimeOffset.UtcNow;
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

    void Write() => pipe.Write("control.json", JsonSerializer.SerializeToUtf8Bytes(new { Enabled, RunId = runId, Mode, StageId, StageGroup, AutoSelectEquipment, Expires = DateTime.UtcNow.AddSeconds(4).Ticks }));
    public JsonObject? Poll()
    {
        Write();
        var bytes = pipe.Read("state.json");
        if (bytes == null)
        {
            if (DateTimeOffset.UtcNow - lastObservation > TimeSpan.FromSeconds(5))
                throw new IOException("Minigame observation missing");
            return null;
        }

        var state = JsonNode.Parse(bytes)?.AsObject() ?? throw new InvalidDataException("Invalid minigame state");
        if (!DateTimeOffset.TryParse(state["At"]?.ToString(), out var at) || at < DateTimeOffset.UtcNow.AddSeconds(-5))
            throw new IOException("Minigame observation expired");
        lastObservation = at;
        Record(state);
        if (Enabled && DateTimeOffset.UtcNow - started > TimeSpan.FromSeconds(5) && state["RunId"]?.ToString() != runId)
            throw new IOException("Minigame control was not acknowledged");
        if (state["RunId"]?.ToString() == runId && (state["Finished"]?.GetValue<bool>() == true || state["Faulted"]?.GetValue<bool>() == true))
            Enabled = false;
        return state;
    }

    internal static JsonObject DiagnosticState(JsonObject state)
    {
        var result = new JsonObject();
        foreach (var key in new[] { "At", "Pid", "RunId", "Running", "Finished", "Faulted", "Reason", "Detail", "Completed", "Ui", "Stage", "Mode", "AutoSelectEquipment", "Selection", "Surfaces", "Entry", "TransitionUi", "Movement", "Tactics" })
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
