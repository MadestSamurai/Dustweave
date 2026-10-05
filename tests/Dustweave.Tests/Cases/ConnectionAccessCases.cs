using System.Diagnostics;
using System.IO.Pipes;
using BD2Daily;
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

        // Real asynchronous pipe framing, split messages, cancellation, and bounded allocation.
        string name = "BD2Daily.Test." + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
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


