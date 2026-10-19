using System;
namespace BD2Sichuan
{
    public static partial class SichuanIdentity
    {
        public const string LiveEntries="runtime.json|sichuan~latest.json|sichuan~enabled-until.txt|sichuan~run-command.json|sichuan~execution-lease.json|sichuan~network-status.json";
        public const string RuntimeName="BD2Sichuan.Runtime2";
        public static string DataRoot=>System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BD2Sichuan");
        public static string BoardRoot=>System.IO.Path.Combine(DataRoot,"sichuan");
        public static bool IsGameProcessName(string name)=>string.Equals(name,"BrownDust II",StringComparison.OrdinalIgnoreCase)||string.Equals(name,"BrownDust II.exe",StringComparison.OrdinalIgnoreCase);
    }
    public sealed class SichuanRuntimeStatus
    {
        public string State {get;set;}="";
        public string Error {get;set;}="";
        public string Runtime {get;set;}="";
        public string AtUtc {get;set;}="";
        public int ProcessId {get;set;}
    }
}
