using BD2.LocalIpc;
using System.Text;
namespace BD2Daily;

/// <summary>The queue owns the writer; transitional adapters only borrow its mailbox API.</summary>
public interface IDailyCommandMailbox
{
    void Acquire(string channel);
    byte[]? Read(string channel, string name);
    void Write(string channel, string name, byte[] value, bool create = false);
    void Delete(string channel, string name);
    string[] List(string channel, string prefix);
}
public sealed class DailyPipeMailbox : IDailyCommandMailbox
{
    private readonly Dictionary<string, PipeClient> clients;
    private readonly HashSet<string> acquired = new(StringComparer.Ordinal);
    public DailyPipeMailbox(string root, GameInstance game)
    {
        clients = new(StringComparer.Ordinal)
        {
            ["daily"] = new(root, game.ProcessId, game.StartTicks),
            ["live"] = new(Path.Combine(root, "live"), game.ProcessId, game.StartTicks)
        };
    }
    private PipeClient Client(string channel) => clients.TryGetValue(channel, out var client) ? client : throw new StageHostException("protocol", "Unknown daily mailbox channel");
    public void Acquire(string channel)
    {
        var client = Client(channel);
        // Never reopen a revoked lease. Only a new explicit queue may take ownership.
        if (acquired.Contains(channel))
        {
            client.Read("runtime.json");
            return;
        }
        string fingerprint = client.Fingerprint() ?? throw new StageHostException("transport", "Daily mailbox is not ready");
        client.Open(fingerprint);
        acquired.Add(channel);
    }
    public byte[]? Read(string channel, string name) => Client(channel).Read(name);
    public void Write(string channel, string name, byte[] value, bool create = false) => Client(channel).Write(name, value, create);
    public void Delete(string channel, string name) => Client(channel).Delete(name);
    public string[] List(string channel, string prefix) => Client(channel).List(prefix);
}
/// <summary>Shares the account controller byte-range lock with the account manager.</summary>
public sealed class DailyControlOwner : IDisposable
{
    private readonly string path;
    private FileStream? file;
    public bool Held => file != null;
    public DailyControlOwner(string root) => path = Path.Combine(root, "live", "controller.lock");
    public void Acquire()
    {
        if (file != null)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var candidate = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        try
        {
            if (candidate.Length == 0)
            {
                candidate.WriteByte(0);
                candidate.Flush(true);
            }
            candidate.Lock(0, 1);
            file = candidate;
        }
        catch { candidate.Dispose(); throw; }
    }
    public void Dispose()
    {
        var owned = file;
        file = null;
        if (owned == null)
            return;
        try
        {
            owned.Unlock(0, 1);
        }
        finally { owned.Dispose(); }
    }
}
