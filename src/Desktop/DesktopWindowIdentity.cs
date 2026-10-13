using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Dustweave.Accounts;

namespace Dustweave.Desktop;

internal static class DesktopWindowIdentity
{
    internal static string AppId => "MadestSamurai.Dustweave" + (DailySandbox.Current is {} binding ? "."+binding.Account[..20] : "");
    internal static void Initialize(Window window)
    {
        // The apphost path and entry assembly changed in the split distribution.
        // Do not leave shell grouping/icon lookup to the launcher's inferred identity.
        SetCurrentProcessExplicitAppUserModelID(AppId);
        window.SourceInitialized += (_,_) =>
        {
            try
            {
                var store=Open(new WindowInteropHelper(window).Handle);
                try
                {
                    Set(store,5,AppId);
                    Set(store,3,Environment.ProcessPath+",0");
                    // A pinned isolated window must not relaunch its account outside its box.
                    if(SandboxProcessScope.CurrentBox.Length==0)
                    {
                        Set(store,2,"\""+Environment.ProcessPath+"\"");
                        Set(store,4,"Dustweave");
                    }
                    Marshal.ThrowExceptionForHR(store.Commit());
                }
                finally { Marshal.ReleaseComObject(store); }
            }
            catch(Exception error) when(error is COMException or InvalidCastException)
            {
                try { DailyJson.Write(Path.Combine(DailyIdentity.DataRoot,"startup","taskbar.json"),new {error=error.Message}); } catch(IOException) { } catch(UnauthorizedAccessException) { }
            }
        };
    }
    internal static string Read(nint handle,uint id)
    {
        var store=Open(handle);
        try { var key=new PropertyKey(id); Marshal.ThrowExceptionForHR(store.GetValue(ref key,out var value)); try{return value.Type==31 ? Marshal.PtrToStringUni(value.Pointer)??"" : "";} finally {PropVariantClear(ref value);} }
        finally { Marshal.ReleaseComObject(store); }
    }
    private static void Set(IPropertyStore store,uint id,string text)
    {
        var key=new PropertyKey(id); var value=new PropVariant { Type=31,Pointer=Marshal.StringToCoTaskMemUni(text) };
        try { Marshal.ThrowExceptionForHR(store.SetValue(ref key,ref value)); } finally {PropVariantClear(ref value);}
    }
    private static IPropertyStore Open(nint handle)
    {
        var iid=typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(handle,ref iid,out var store));return store;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey(uint id) { public Guid Format=new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"); public uint Id=id; }
    [StructLayout(LayoutKind.Explicit,Size=24)] private struct PropVariant { [FieldOffset(0)]public ushort Type; [FieldOffset(8)]public nint Pointer; }
    [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]int GetCount(out uint count);
        [PreserveSig]int GetAt(uint index,out PropertyKey key);
        [PreserveSig]int GetValue(ref PropertyKey key,out PropVariant value);
        [PreserveSig]int SetValue(ref PropertyKey key,ref PropVariant value);
        [PreserveSig]int Commit();
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [DllImport("shell32.dll")]private static extern int SHGetPropertyStoreForWindow(nint window,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out IPropertyStore store);
    [DllImport("ole32.dll")]private static extern int PropVariantClear(ref PropVariant value);
}
