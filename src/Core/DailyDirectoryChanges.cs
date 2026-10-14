using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace Dustweave;

// Poll the kernel's completed change buffer, rather than waiting for a managed
// FileSystemWatcher callback. A write immediately followed by a command must not
// see a stale index. No background thread or per-file polling is needed.
internal sealed class DailyDirectoryChanges : IDisposable
{
    const int Capacity = 64 * 1024;
    readonly SafeFileHandle directory;
    readonly string path;
    readonly FileIdentity identity;
    readonly EventWaitHandle ready = new(false, EventResetMode.ManualReset);
    IntPtr buffer, overlapped;
    bool pending, disposed;
    [StructLayout(LayoutKind.Sequential)]
    struct Overlap { public IntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr Event; }
    [StructLayout(LayoutKind.Sequential)]
    struct FileIdentity
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IdHigh, IdLow;
        public readonly bool Same(FileIdentity other) => Volume == other.Volume && IdHigh == other.IdHigh && IdLow == other.IdLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle Open(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadDirectoryChangesW(SafeFileHandle directory, IntPtr buffer, uint size, bool subtree, uint filter, IntPtr returned, IntPtr overlapped, IntPtr completion);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetOverlappedResult(SafeFileHandle directory, IntPtr overlapped, out uint transferred, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CancelIoEx(SafeFileHandle directory, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileIdentity identity);

    public DailyDirectoryChanges(string path)
    {
        this.path = path;
        directory = Open(path, 1, 7, IntPtr.Zero, 3, 0x42000000, IntPtr.Zero);
        try
        {
            if (directory.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!GetFileInformationByHandle(directory, out identity)) throw new Win32Exception(Marshal.GetLastWin32Error());
            buffer = Marshal.AllocHGlobal(Capacity);
            overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<Overlap>());
            Arm();
        }
        catch { Dispose(); throw; }
    }
    void Arm()
    {
        ready.Reset();
        Marshal.StructureToPtr(new Overlap { Event = ready.SafeWaitHandle.DangerousGetHandle() }, overlapped, false);
        // Names, attributes, size, last-write and creation also catch atomic
        // replacement followed by restoration of the original timestamps.
        if (!ReadDirectoryChangesW(directory, buffer, Capacity, false, 0x5f, IntPtr.Zero, overlapped, IntPtr.Zero))
            throw new IOException("Cannot observe operation journal changes", new Win32Exception(Marshal.GetLastWin32Error()));
        pending = true;
    }
    public (bool Rescan, string[] Names) Poll()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        // Detect replacement/renaming of the containing directory, not just its
        // files. Two folder handles per guard replace thousands of file opens.
        using (var current = Open(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero))
            if (current.IsInvalid || !GetFileInformationByHandle(current, out var now) || !identity.Same(now))
                throw new IOException("Operation journal directory changed");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool rescan = false;
        // Bounded even if an external writer is continuously editing files.
        for (int round = 0; round < 16 && ready.WaitOne(0); round++)
        {
            bool completed = GetOverlappedResult(directory, overlapped, out uint bytes, false);
            int error = completed ? 0 : Marshal.GetLastWin32Error();
            pending = false;
            if (!completed && error != 1022) // ERROR_NOTIFY_ENUM_DIR also requires a full rebuild.
                throw new IOException("Cannot read operation journal changes", new Win32Exception(error));
            if (!completed) bytes = 0;
            if (bytes == 0) rescan = true; // Overflow: rebuild once, never assume no change.
            for (int offset = 0; offset < bytes;)
            {
                if (offset + 12 > bytes) { rescan = true; break; }
                int next = Marshal.ReadInt32(buffer, offset), length = Marshal.ReadInt32(buffer, offset + 8);
                if (length < 0 || length % 2 != 0 || (long)offset + 12 + length > bytes) { rescan = true; break; }
                string name = Marshal.PtrToStringUni(IntPtr.Add(buffer, offset + 12), length / 2)!;
                if (Path.GetFileName(name) == name && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) names.Add(name);
                if (next == 0) break;
                if (next < 12 + length || (long)offset + next >= bytes) { rescan = true; break; }
                offset += next;
            }
            Arm();
        }
        if (ready.WaitOne(0)) rescan = true;
        return (rescan, names.ToArray());
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (pending)
        {
            CancelIoEx(directory, overlapped);
            GetOverlappedResult(directory, overlapped, out _, true);
        }
        directory.Dispose();
        if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        if (overlapped != IntPtr.Zero) Marshal.FreeHGlobal(overlapped);
        ready.Dispose();
        GC.SuppressFinalize(this);
    }
    ~DailyDirectoryChanges() { Dispose(); }
}
