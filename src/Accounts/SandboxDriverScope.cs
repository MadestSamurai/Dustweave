using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Dustweave.Accounts;

// The public API_QUERY_PROCESS wire contract from Sandboxie's core/drv/api_defs.h.
// Only a read-only process query is implemented: no service start, configuration,
// injection or account mutation. Avoid loading the optional installation's DLL
// in ordinary hosts. Sandboxie-injected processes use their own DLL instead.
internal static class SandboxDriverScope
{
    internal const ulong QueryProcess = 0x12340007;
    internal const uint ControlCode = 0x222007;
    internal const int NotSandboxed = unchecked((int)0xC000000B);
    internal const int Unavailable = unchecked((int)0xC0000080);

    internal static (int Status, string Box) Query(int pid)
    {
        if (pid <= 0) throw new ArgumentOutOfRangeException(nameof(pid));
        using var deviceText = new Buffer(System.Text.Encoding.Unicode.GetBytes("\\Device\\SandboxieDriverApi\0"));
        using var deviceName = new Buffer(Marshal.SizeOf<UnicodeString>());
        Marshal.StructureToPtr(new UnicodeString { Length = (ushort)(deviceText.Size - 2), MaximumLength = (ushort)deviceText.Size, Data = deviceText.Address }, deviceName.Address, false);
        var attributes = new ObjectAttributes { Length = Marshal.SizeOf<ObjectAttributes>(), Name = deviceName.Address, Attributes = 0x40 };
        // FILE_GENERIC_READ, share read/write/delete, synchronous non-alertable.
        // No negative availability cache: starting/repairing Sandboxie can take
        // effect on the next explicit operation without restarting Dustweave.
        int status = NtOpenFile(out var device, 0x120089, ref attributes, out _, 7, 0x20);
        using (device)
        {
            if (status != 0) return (NormalizeOpenStatus(status), "");
            return QueryBuffer(pid, arguments => NtDeviceIoControlFile(device, 0, 0, 0, out _, ControlCode, arguments, 64, 0, 0));
        }
    }

    internal static (int Status, string Box) QueryBuffer(int pid, Func<ulong[], int> request)
    {
        using var boxText = new Buffer(68);
        using var boxName = new Buffer(Marshal.SizeOf<UnicodeString>());
        Marshal.StructureToPtr(new UnicodeString { MaximumLength = 68, Data = boxText.Address }, boxName.Address, false);
        ulong[] arguments = [QueryProcess, (ulong)pid, (ulong)boxName.Address, 0, 0, 0, 0, 0];
        int status = request(arguments);
        if (status != 0) return (status, "");
        var returned = Marshal.PtrToStructure<UnicodeString>(boxName.Address);
        if (returned.Length > 66 || (returned.Length & 1) != 0 || returned.Data != boxText.Address)
            throw new IOException("Sandboxie returned an invalid process-scope buffer.");
        return (status, Marshal.PtrToStringUni(boxText.Address, returned.Length / 2) ?? "");
    }

    // Exactly the two missing-device statuses mapped by SbieApi_Ioctl. Access
    // denied and all other errors retain their identity in diagnostic records.
    internal static int NormalizeOpenStatus(int status) => status is unchecked((int)0xC0000034) or unchecked((int)0xC000000E) ? Unavailable : status;

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString { public ushort Length, MaximumLength; public nint Data; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes { public int Length; public nint Root, Name; public uint Attributes; public nint Security, QualityOfService; }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatus { public nint Status; public nuint Information; }
    private sealed class Buffer : IDisposable
    {
        public int Size { get; }
        public nint Address { get; }
        public Buffer(int size) : this(new byte[size]) { }
        public Buffer(byte[] contents)
        {
            Size = contents.Length; Address = Marshal.AllocHGlobal(Size);
            Marshal.Copy(contents, 0, Address, Size);
        }
        public void Dispose() => Marshal.FreeHGlobal(Address);
    }
    [DllImport("ntdll.dll")]
    private static extern int NtOpenFile(out SafeFileHandle file, uint access, ref ObjectAttributes attributes, out IoStatus io, uint share, uint options);
    [DllImport("ntdll.dll")]
    private static extern int NtDeviceIoControlFile(SafeFileHandle file, nint signal, nint apc, nint context, out IoStatus io, uint code, [In] ulong[] input, uint inputSize, nint output, uint outputSize);
}
