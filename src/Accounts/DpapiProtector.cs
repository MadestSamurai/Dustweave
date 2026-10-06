using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Dustweave.Accounts;

internal static class DpapiProtector
{
    private const uint CryptProtectUiForbidden = 0x1;

    internal static byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        if (plaintext.IsEmpty)
        {
            throw new ArgumentException("加密内容不能为空。", nameof(plaintext));
        }

        return Transform(plaintext, protect: true);
    }

    internal static byte[] Unprotect(ReadOnlySpan<byte> ciphertext)
    {
        if (ciphertext.IsEmpty)
        {
            throw new InvalidDataException("加密槽位内容为空。 ");
        }

        return Transform(ciphertext, protect: false);
    }

    private static byte[] Transform(ReadOnlySpan<byte> input, bool protect)
    {
        DataBlob inputBlob = default;
        DataBlob entropyBlob = default;
        DataBlob outputBlob = default;
        IntPtr description = IntPtr.Zero;

        try
        {
            inputBlob = Allocate(input);
            entropyBlob = Allocate(SessionConstants.DpapiEntropy);

            bool succeeded = protect
                ? CryptProtectData(
                    ref inputBlob,
                    null,
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob)
                : CryptUnprotectData(
                    ref inputBlob,
                    out description,
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob);

            if (!succeeded)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    protect ? "Windows DPAPI 加密失败。" : "Windows DPAPI 解密失败。槽位可能属于另一个 Windows 用户或已损坏。");
            }

            byte[] result = new byte[outputBlob.Length];
            Marshal.Copy(outputBlob.Data, result, 0, outputBlob.Length);
            return result;
        }
        finally
        {
            FreeAllocated(ref inputBlob);
            FreeAllocated(ref entropyBlob);
            if (outputBlob.Data != IntPtr.Zero)
            {
                LocalFree(outputBlob.Data);
            }

            if (description != IntPtr.Zero)
            {
                LocalFree(description);
            }
        }
    }

    private static DataBlob Allocate(ReadOnlySpan<byte> data)
    {
        byte[] copy = data.ToArray();
        IntPtr pointer = Marshal.AllocHGlobal(copy.Length);
        Marshal.Copy(copy, 0, pointer, copy.Length);
        return new DataBlob { Length = copy.Length, Data = pointer };
    }

    private static void FreeAllocated(ref DataBlob blob)
    {
        if (blob.Data == IntPtr.Zero)
        {
            return;
        }

        Marshal.FreeHGlobal(blob.Data);
        blob = default;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        internal int Length;
        internal IntPtr Data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? dataDescription,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        uint flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        out IntPtr dataDescription,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        uint flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
