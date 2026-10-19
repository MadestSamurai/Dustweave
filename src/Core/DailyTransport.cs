using BD2.LocalIpc;
using System.Runtime.InteropServices;
namespace Dustweave;

public static class DailyTransport
{
    private static readonly object sync = new();
    private static readonly Dictionary<string, (int Pid, long Start)> readers = new(StringComparer.OrdinalIgnoreCase);
    public static void Configure(string root)
    {
        DesktopFiles.Configure(root, DailyIdentity.LiveEntries);
        LiveStore.Handles = DesktopFiles.Handles;
        LiveStore.Read = p => { DesktopFiles.Read(p, out var b); return b; };
        LiveStore.Write = (p, b, c) => DesktopFiles.Write(p, b, c);
    }
    public static PipeClient Bind(string root, GameInstance game)
    {
        lock (sync)
        {
            root = Path.GetFullPath(root);
            Configure(root);
            var pipe = DesktopFiles.Connect(root, game.ProcessId, game.StartTicks);
            readers[root] = (game.ProcessId, game.StartTicks);
            return pipe;
        }
    }
    // Reader refresh keeps a live writer's lease, but never reuses the previous process endpoint.
    public static bool Refresh(string root, GameInstance? game)
    {
        if (game == null)
            return false;
        lock (sync)
        {
            root = Path.GetFullPath(root);
            Configure(root);
            if (!readers.TryGetValue(root, out var old) || old != (game.ProcessId, game.StartTicks) || !DesktopFiles.IsConnected(root))
                Bind(root, game);
            return true;
        }
    }
    public static bool EndpointReady(GameInstance game) => EndpointReady(game, out _);
    public static bool EndpointReady(GameInstance game, out int error)
    {
        bool ready = WaitNamedPipe(@"\\.\pipe\" + Wire.Endpoint(game.ProcessId, game.StartTicks), 1);
        error = ready ? 0 : Marshal.GetLastWin32Error();
        return EndpointExists(ready, error);
    }
    // Both busy and timeout mean the named endpoint exists; the authenticated read has its own bounded wait.
    public static bool EndpointExists(bool ready, int error) => ready || error is 121 or 231;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool WaitNamedPipe(string name, uint timeout);
    public static void Reader(string root)
    {
        Configure(root);
        System.Diagnostics.Process[] games;
        try { games = Dustweave.Accounts.SandboxProcessScope.Find("BrownDust II"); }
        // Attaching to an existing endpoint is optional during host construction.
        // Explicit discovery/connection still reports the scope failure; it must
        // not prevent the ordinary window (including settings) from opening.
        catch (Dustweave.Accounts.SandboxScopeException) { return; }
        try
        {
            if (games.Length == 1)
                Refresh(root, new(games[0].Id, games[0].StartTime.ToUniversalTime().Ticks, ""));
        }
        finally { foreach (var g in games) g.Dispose(); }
    }
}
