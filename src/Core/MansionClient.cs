using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using BD2.LocalIpc;

namespace Dustweave;
/// <summary>Hosted transport only: never launches, injects, or reconnects to another game.</summary>
public sealed class MansionClient
{
    readonly PipeClient pipe;
    string runId = Guid.NewGuid().ToString("N");
    DateTimeOffset lastObservation = DateTimeOffset.UtcNow, started;
    public bool Enabled { get; private set; }
    public string Mode { get; set; } = "Normal";
    public bool Retry { get; set; } = true;
    public bool Chain80 { get; set; }

    public MansionClient(string root, int pid, long start, string fingerprint)
    {
        if (pid <= 0 || start <= 0 || string.IsNullOrWhiteSpace(fingerprint))
            throw new InvalidOperationException("Invalid hosted game identity");
        if (Environment.GetEnvironmentVariable("BD2_DAILY_HOSTED_TOOL") != "mansion-runaway" || Environment.GetEnvironmentVariable("BD2_DAILY_GAME_PID") != pid.ToString() || Environment.GetEnvironmentVariable("BD2_DAILY_GAME_START") != start.ToString())
            throw new InvalidOperationException("Changed hosted game identity");
        pipe = new PipeClient(Path.Combine(root, "mansion"), pid, start);
        pipe.Open(fingerprint);
    }

    public void Start()
    {
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

    void Write() => pipe.Write("control.json", JsonSerializer.SerializeToUtf8Bytes(new { Enabled, RunId = runId, Mode = Chain80 ? "Challenge" : Mode, Retry, TargetChain = Chain80 ? 80 : 0, AutoFlow = true, Expires = DateTime.UtcNow.AddSeconds(4).Ticks }));
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
        if (Enabled && DateTimeOffset.UtcNow - started > TimeSpan.FromSeconds(5) && state["RunId"]?.ToString() != runId)
            throw new IOException("Minigame control was not acknowledged");
        if (state["RunId"]?.ToString() == runId && (state["Finished"]?.GetValue<bool>() == true || state["Faulted"]?.GetValue<bool>() == true))
            Enabled = false;
        return state;
    }
}
