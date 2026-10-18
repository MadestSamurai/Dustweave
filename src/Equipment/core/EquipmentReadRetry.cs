using BD2.LocalIpc;
namespace BD2Equipment.Core;

public static class EquipmentReadRetry
{
    public static bool Transient(Exception error) => error is TimeoutException or ObjectDisposedException ||
        error is IOException and not LeaseRevokedException && (error.HResult & 0xffff) is 64 or 109 or 232 or 233;
    // Only callers performing a read use this helper. Never replay command writes.
    public static T Read<T>(Func<T> read, Action<int>? delay = null)
    {
        for (int attempt = 0; ; attempt++)
            try { return read(); }
            catch (Exception error) when (attempt < 2 && Transient(error))
            { (delay ?? Thread.Sleep)(100 * (attempt + 1)); }
    }
}
