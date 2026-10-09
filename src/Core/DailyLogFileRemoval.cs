using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace Dustweave;

// Hold the exact file exclusively from validation through deletion. No close/delete
// gap that could remove a replacement written by a running helper.
internal static class DailyLogFileRemoval
{
    [StructLayout(LayoutKind.Sequential)] struct Info
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IdHigh, IdLow;
    }
    [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern SafeFileHandle Open(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandle(SafeFileHandle file,out Info info);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool SetFileInformationByHandle(SafeFileHandle file,int kind,ref int disposition,uint size);
    public static bool Delete(DailyLogFile expected)
    {
        using var file=Open(expected.Path,0x80010000,0,IntPtr.Zero,3,0x00200000,IntPtr.Zero);
        if(file.IsInvalid)throw new IOException("Log is in use or unavailable",new Win32Exception(Marshal.GetLastWin32Error()));
        if(!GetFileInformationByHandle(file,out var info))throw new IOException("Cannot verify log identity");
        static DateTime Time(System.Runtime.InteropServices.ComTypes.FILETIME t)=>DateTime.FromFileTimeUtc(((long)(uint)t.dwHighDateTime<<32)|(uint)t.dwLowDateTime);
        if((info.Attributes & ((uint)FileAttributes.ReparsePoint|(uint)FileAttributes.Directory))!=0 || info.Links!=1
            || ((long)info.SizeHigh<<32|info.SizeLow)!=expected.Bytes || Time(info.Created)!=expected.Created || Time(info.Written)!=expected.Written)return false;
        int delete=1;
        if(!SetFileInformationByHandle(file,4,ref delete,4))throw new IOException("Cannot remove log",new Win32Exception(Marshal.GetLastWin32Error()));
        return true;
    }
}
