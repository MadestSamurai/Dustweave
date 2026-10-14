using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Dustweave;
using BD2Daily.Runtime;

internal static class ConnectionAccessCases
{
    public static async Task Run(List<string> cases)
    {
        void Check(bool value, string label) { if (!value) throw new Exception(label); cases.Add("connection: " + label); }
        void Reject(Action action, string label) { try { action(); } catch (Exception e) when (e is InvalidOperationException or InvalidDataException) { cases.Add("connection: " + label); return; } throw new Exception(label); }
        
        Neo.Unity.Neon.NeonSdk.Reset();
        for (int i = 0; i < 20; i++) Check(!SdkIdentity.TryRead(out var key) && key == "", "cold SDK read waits " + i);
        Check(Neo.Unity.Neon.NeonSdk.Reads == 0, "cold observer never touches exception-caching Auth");
        Neo.Unity.Neon.NeonSdk.IsInitialized = true;
        Check(SdkIdentity.TryRead(out var account) && account == DailyIdentity.MemberKey("42"), "first ready SDK read identifies account without poisoning Lazy");
        Neo.Unity.Neon.NeonSdk.Reset();
        try { _ = Neo.Unity.Neon.NeonSdk.Auth; } catch (NeonInitException) { }
        Neo.Unity.Neon.NeonSdk.IsInitialized = true;
        bool poisoned = false;
        try { SdkIdentity.TryRead(out _); } catch (NeonInitException) { poisoned = true; }
        Check(poisoned, "fixture reproduces cached early Auth failure after readiness");
        Neo.Unity.Neon.NeonSdk.Reset();
        Check(!DailyConnectionAccess.NeedsElevation(false, false, true, true), "normal game stays normal");
        Check(DailyConnectionAccess.NeedsElevation(false, true, true, true), "only elevated game requests helper");
        Check(!DailyConnectionAccess.NeedsElevation(true, true, true, true), "already elevated caller does not prompt twice");
        Check(!DailyConnectionAccess.NeedsElevation(true, false, true, true), "normal game needs no extra elevation");
        Reject(() => DailyConnectionAccess.NeedsElevation(false, true, false, true), "different Windows user rejected");
        Reject(() => DailyConnectionAccess.NeedsElevation(false, true, true, false), "different desktop session rejected");
        using var process = Process.GetCurrentProcess();
        var image = DailyConnectionAccess.Describe(process.Id);
        Check(image.ProcessId == process.Id && image.StartTicks == process.StartTime.ToUniversalTime().Ticks && image.Executable == Environment.ProcessPath, "limited process query preserves path and process generation");
        Reject(() => DailyConnectionAccess.RequireGame(image), "helper refuses a non-game target");
        string nonce = "nonce";
        DailyConnectionAccess.RequireReply(new(nonce, "ready"), nonce, "ready");
        Reject(() => DailyConnectionAccess.RequireReply(new("other", "ready"), nonce, "ready"), "stale response nonce rejected");
        Reject(() => DailyConnectionAccess.RequireReply(new(nonce, "connected"), nonce, "ready"), "skipped handshake rejected");
        Reject(() => DailyConnectionAccess.RequireReply(new(nonce, "error", Error: "fixture"), nonce, "ready"), "helper errors do not become success");
        Check(await DailyConnectionAccess.RunHelperAsync([DailyConnectionAccess.HelperSwitch, "wrong"]) == 2, "malformed helper invocation cannot connect");
        var snapshot = new DailySnapshot { ProcessId = image.ProcessId, ProcessStartTicks = image.StartTicks, FrameUtcTicks = DateTime.UtcNow.Ticks, Sequence = 1, InstanceId = "fixture", State = "waiting_sdk" };
        Check(!DailyLoginReadiness.CanRecover(snapshot, DailyIdentity.MemberKey("42")), "SDK wait cannot invoke account recovery");
        DailyLoginReadiness.Check(snapshot, image, DateTime.UtcNow.Ticks);
        snapshot.State = "read_error"; snapshot.ErrorCode = "NeonInitException";
        Reject(() => DailyLoginReadiness.Check(snapshot, image, DateTime.UtcNow.Ticks), "poisoned SDK produces explicit restart diagnostic");
        snapshot.ProcessStartTicks++;
        DailyLoginReadiness.Check(snapshot, image, DateTime.UtcNow.Ticks);
        Check(true, "stale SDK failure from another process is ignored");

        // Reproduce the user's truncated local reply without connecting to a game.
        async Task<int> InterruptedReply(string verb, int breaks)
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            string endpoint = BD2.LocalIpc.Wire.Endpoint(process.Id, process.StartTime.ToUniversalTime().Ticks);
            using var service = new NamedPipeServerStream(endpoint, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            int requests = 0;
            var serving = Task.Run(async () => {
                for (int i=0; i <= breaks; i++)
                {
                    await service.WaitForConnectionAsync(limit.Token);
                    _ = BD2.LocalIpc.Wire.ReadFrame(service); requests++;
                    if (i == breaks) BD2.LocalIpc.Wire.WriteFrame(service, BD2.LocalIpc.Wire.Encode(w => {
                        w.Write("ok"); w.Write("fixture"); w.Write(""); BD2.LocalIpc.Wire.Bytes(w, new byte[]{42});
                    }));
                    service.Disconnect();
                    if (verb == "write") break;
                }
            });
            var local = new BD2.LocalIpc.PipeClient(Path.Combine(Path.GetTempPath(), "Dustweave-test-"+Guid.NewGuid()), process.Id, process.StartTime.ToUniversalTime().Ticks);
            try
            {
                if (verb == "write")
                {
                    bool failed=false;
                    try { await Task.Run(() => local.Write("control.json", new byte[]{1})); } catch (EndOfStreamException) { failed=true; }
                    Check(failed,"uncertain write is reported rather than replayed");
                }
                else Check((await Task.Run(() => local.Read("frame.json")))!.SequenceEqual(new byte[]{42}),"truncated read reconnects and receives a complete fresh frame");
                await serving;
                return requests;
            }
            finally { limit.Cancel(); }
        }
        Check(await InterruptedReply("read",2)==3,"read-only reconnect is limited to two retries");
        Check(await InterruptedReply("write",1)==1,"mutating mailbox request is sent exactly once");

        // Real asynchronous pipe framing, split messages, cancellation, and bounded allocation.
        string name = "BD2Daily.Test." + Guid.NewGuid().ToString("N");
        using var server = DailyConnectionAccess.CreateElevationServer(name);
        using var client = DailyConnectionAccess.CreateElevationClient(name);
        using var identity = WindowsIdentity.GetCurrent();
        var security = server.GetAccessControl();
        var access = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToArray();
        Check(security.AreAccessRulesProtected && security.GetOwner(typeof(SecurityIdentifier)).Equals(identity.User),
            "elevation pipe is owned by the user SID, not the token owner group");
        Check(access.Length == 1 && access[0].IdentityReference.Equals(identity.User)
            && access[0].AccessControlType == AccessControlType.Allow && access[0].PipeAccessRights == PipeAccessRights.FullControl,
            "elevation pipe ACL grants only the current user and does not admit other users or administrator accounts");
        bool duplicateRejected = false;
        try { using var duplicate = DailyConnectionAccess.CreateElevationServer(name); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { duplicateRejected = true; }
        Check(duplicateRejected, "elevation handshake cannot share its name with a second server");
        Check(DailyConnectionAccess.BrokenElevationPipe(new IOException("localized", unchecked((int)0x8007006D)))
            && DailyConnectionAccess.BrokenElevationPipe(new EndOfStreamException())
            && !DailyConnectionAccess.BrokenElevationPipe(new IOException("access", unchecked((int)0x80070005))),
            "broken transport is classified by native status without hiding other access failures");
        var helperError = new InvalidOperationException("管理员连接组件通信中断，请重新连接；若仍失败，请通过织尘启动游戏后再试，并导出诊断日志。", new IOException("Pipe is broken."));
        Check(DailyIssues.Classify(helperError).Code == "elevation-pipe"
            && DailyUserText.Error(helperError).Contains("重新连接"), "helper failure retains an actionable connection explanation over its inner pipe error");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
        var request = new DailyConnectionAccess.Request(nonce, image, "fingerprint", "hash");
        var send = DailyConnectionAccess.Send(client, request, timeout.Token);
        Check(await DailyConnectionAccess.Receive<DailyConnectionAccess.Request>(server, timeout.Token) == request, "same-user pipe preserves exact game identity");
        await send;
        using (var stop = new CancellationTokenSource(30))
        {
            bool cancelled = false;
            try { await DailyConnectionAccess.Receive<DailyConnectionAccess.Reply>(client, stop.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "waiting helper reply is cancellable");
        }
        var invalidSend = client.WriteAsync(BitConverter.GetBytes(16385), timeout.Token);
        bool bounded = false;
        try { await DailyConnectionAccess.Receive<DailyConnectionAccess.Reply>(server, timeout.Token); } catch (InvalidDataException) { bounded = true; }
        Check(bounded, "invalid frame size rejected before allocating payload");
        await invalidSend;
    }
}
internal sealed class NeonInitException : Exception { }
namespace Neo.Unity.Neon
{
    internal static class NeonSdk
    {
        public static bool IsInitialized { get; set; }
        public static int Reads;
        private static Lazy<NeonAuth> auth = Create();
        private static Lazy<NeonAuth> Create() => new(() => IsInitialized ? new NeonAuth() : throw new NeonInitException());
        public static NeonAuth Auth { get { Reads++; return auth.Value; } }
        public static void Reset() { IsInitialized = false; Reads = 0; auth = Create(); }
    }
    internal sealed class NeonAuth { public Member LoggedMember => new(); }
    internal sealed class Member { public long MemberId => 42; }
}


