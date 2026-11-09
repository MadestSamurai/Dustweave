using System.Text;
using Microsoft.Win32;

namespace Dustweave;

internal static class FreeDistributionCases
{
    public static void Run(List<string> cases)
    {
        void Check(string name, bool condition) { if (!condition) throw new Exception(name); cases.Add(name); }
        void Fails(string name, Action action) { bool failed = false; try { action(); } catch { failed = true; } Check(name, failed); }
        byte[]? bytes = null;
        int writes = 0;
        string identity = "machine-a/user-a";
        byte[] Same(byte[] value) => value.ToArray();
        DailyFreeDistributionAcknowledgement Store() => new(() => bytes, value => { bytes = value.ToArray(); writes++; }, () => identity, Same, Same);
        var store = Store();
        Check("free notice starts visible without a record", !store.IsAcknowledged);
        foreach (string language in new[] { "zh-CN", "zh-TW", "en-US" })
        {
            bytes = null; int before = writes;
            Check("empty confirmation rejected: " + language, !DailyFreeDistributionAcknowledgement.Matches("", language));
            Check("partial confirmation rejected: " + language, !DailyFreeDistributionAcknowledgement.Matches("I", language));
            Fails("direct invalid confirmation cannot write: " + language, () => store.Confirm("yes", language));
            Check("invalid input preserved storage: " + language, bytes == null && writes == before);
            store.Confirm(" " + DailyFreeDistributionAcknowledgement.Phrase(language) + " ", language);
            Check("typed confirmation survives a fresh store: " + language, Store().IsAcknowledged && writes == before + 1);
        }
        Check("English confirmation tolerates case", DailyFreeDistributionAcknowledgement.Matches("i CONFIRM", "en-US"));
        Check("Chinese locale uses its displayed phrase", !DailyFreeDistributionAcknowledgement.Matches("I confirm", "zh-CN"));
        byte[] saved = bytes!.ToArray();
        identity = "machine-b/user-a";
        Check("copy to another machine stays visible", !Store().IsAcknowledged);
        identity = "machine-a/user-b";
        Check("copy to another Windows user stays visible", !Store().IsAcknowledged);
        identity = "machine-a/user-a";
        Check("original machine user still accepts same receipt", Store().IsAcknowledged);
        bytes = [1, 2, 3]; Check("corrupt receipt stays visible", !Store().IsAcknowledged);
        bytes = new byte[8193]; Check("oversize receipt stays visible", !Store().IsAcknowledged);
        bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(saved).Replace("Dustweave.free-distribution", "Other.application"));
        Check("foreign purpose stays visible", !Store().IsAcknowledged);
        bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(saved).Replace("\"Revision\":1", "\"Revision\":2"));
        Check("unknown acknowledgement revision stays visible", !Store().IsAcknowledged);
        bytes = null;
        var denied = new DailyFreeDistributionAcknowledgement(() => bytes, _ => throw new UnauthorizedAccessException(), () => identity, Same, Same);
        Fails("denied persistence does not report success", () => denied.Confirm("我确认", "zh-CN"));
        Check("denied persistence leaves notice visible", !denied.IsAcknowledged);
        var lostWrite = new DailyFreeDistributionAcknowledgement(() => null, _ => { }, () => identity, Same, Same);
        Fails("readback is mandatory before dismissal", () => lostWrite.Confirm("我确认", "zh-CN"));
        var unavailable = new DailyFreeDistributionAcknowledgement(() => throw new IOException(), _ => { }, () => identity, Same, Same);
        Check("read failure cannot break startup", !unavailable.IsAcknowledged);
        var missingIdentity = new DailyFreeDistributionAcknowledgement(() => bytes, _ => writes++, () => "", Same, Same);
        Fails("missing machine identity cannot produce portable receipt", () => missingIdentity.Confirm("我确认", "zh-CN"));
        store.Confirm("我确认", "zh-CN");
        Check("retry after storage recovery succeeds", Store().IsAcknowledged);

        // Exercise actual Windows DPAPI and flushed REG_BINARY with a uniquely
        // owned test key. Never read/write the production acknowledgement value.
        string testKey = @"Software\MadestSamurai\Dustweave\Tests\FreeNotice-" + Guid.NewGuid().ToString("N");
        try
        {
            byte[]? Read() { using var key = Registry.CurrentUser.OpenSubKey(testKey); return key?.GetValue("Receipt") as byte[]; }
            void Write(byte[] value) { using var key = Registry.CurrentUser.CreateSubKey(testKey); key.SetValue("Receipt", value, RegistryValueKind.Binary); key.Flush(); }
            var native = new DailyFreeDistributionAcknowledgement(Read, Write);
            native.Confirm("I confirm", "en-US");
            Check("native encrypted registry record survives reopen", new DailyFreeDistributionAcknowledgement(Read, Write).IsAcknowledged);
            byte[] encrypted = Read()!;
            Check("registry acknowledgement is not plaintext JSON", !Encoding.UTF8.GetString(encrypted).Contains("Dustweave.free-distribution"));
            encrypted[^1] ^= 0x7f; Write(encrypted);
            Check("DPAPI rejects a modified encrypted receipt", !new DailyFreeDistributionAcknowledgement(Read, Write).IsAcknowledged);
            native.Confirm("我确认", "zh-CN");
            Check("native corrupted record can be replaced by explicit confirmation", native.IsAcknowledged);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(testKey, throwOnMissingSubKey: false); }
    }
}
