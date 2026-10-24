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
        await CheckBrokerContinuity(cases);
        await CheckSharedPreparation(cases);

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
    static async Task CheckSharedPreparation(List<string> cases)
    {
        // Reproduce a fresh child's empty permission cache independently of the
        // parent's successful connection. No real game or elevation is used.
        foreach (string id in new[] { "daily", "fishing-daily", "territory-daily", "sichuan-daily",
            "fishing", "territory", "mansion-runaway", "pandora", "equipment", "infinite-gacha",
            "fiend-hunter", "rhythm", "sichuan", "secret-vision", "apostle-defense" })
        {
            var host = new FreshScopeHost();
            try { await DailySuite.ActivateAsync(host, "unused-fixture", id, _ => { }, default); }
            catch (InvalidOperationException error) when (error.Message == "请先启动游戏，并在日常助手连接一次。")
            {
                if (host.AccessChecks != 1 || host.Discoveries != 1) throw new Exception("access ordering: " + id);
                cases.Add("connection: fresh helper prepares access before discovery: " + id);
                continue;
            }
            throw new Exception("Shared preparation did not preserve game discovery: " + id);
        }
        var denied = new FreshScopeHost { Denied = true };
        try { await DailySuite.ActivateAsync(denied, "unused-fixture", "daily", _ => { }, default); throw new Exception("denial ignored"); }
        catch (IOException error) when (error.Message == "connection.scope_permission")
        {
            if (denied.Discoveries != 0) throw new Exception("Discovery after denied preparation");
            cases.Add("connection: denied preparation propagates before discovery or activation");
        }
        var cancelled = new FreshScopeHost();
        try { await DailySuite.ActivateAsync(cancelled, "unused-fixture", "daily", _ => { }, new CancellationToken(true)); throw new Exception("cancellation ignored"); }
        catch (OperationCanceledException)
        {
            if (cancelled.AccessChecks != 0 || cancelled.Discoveries != 0) throw new Exception("Cancelled helper requested access");
            cases.Add("connection: cancelled shared preparation cannot request permission or discover games");
        }
    }
    private sealed class FreshScopeHost : IGameHost
    {
        public int AccessChecks, Discoveries;
        public bool Denied;
        private bool accessible;
        public async Task PrepareAccessAsync(Action<string> progress, CancellationToken token)
        {
            AccessChecks++; await Task.Yield(); token.ThrowIfCancellationRequested();
            if (Denied) throw new IOException("connection.scope_permission");
            accessible = true;
        }
        public GameInstance? Find()
        {
            if (!accessible) throw new System.ComponentModel.Win32Exception(5);
            Discoveries++; return null;
        }
        public Task CloseAsync(GameInstance game, CancellationToken token) => throw new Exception("Unexpected game close");
        public Task ConnectAsync(GameInstance game, Action<string> progress, CancellationToken token) => throw new Exception("Unexpected injection");
        public DailySnapshot? ReadSnapshot() => throw new Exception("Unexpected observation");
        public DailyRuntimeStatus? ReadStatus() => throw new Exception("Unexpected observation");
    }
    static async Task CheckBrokerContinuity(List<string> cases)
    {
        // Exercise the actual embedded broker in this test process. Pausing its
        // acceptor after dispatch reproduces normal thread-preemption ordering.
        string source=System.Text.Encoding.UTF8.GetString(Dustweave.Compatibility.DailyHookCompiler.Resource("Hook.Ipc.PipeBroker.cs"));
        string wire=System.Text.Encoding.UTF8.GetString(Dustweave.Compatibility.DailyHookCompiler.Resource("Hook.Ipc.Wire.cs"));
        string trace=System.Text.Encoding.UTF8.GetString(Dustweave.Compatibility.DailyHookCompiler.Resource("Hook.Ipc.LoadingTrace.cs"));
        const string enqueue="ThreadPool.QueueUserWorkItem(_ => {try{Serve(connected);}finally{lock(transportSync)serving.Remove(connected);}});";
        if(!source.Contains(enqueue))throw new Exception("Broker acceptor fixture needs updating");
        source=source.Replace(enqueue,enqueue+"Thread.Sleep(35);");
        var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(p=>Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(p)).ToArray();
        using var process=Process.GetCurrentProcess();var game=new GameInstance(process.Id,process.StartTime.ToUniversalTime().Ticks,"");
        foreach(bool continuous in new[]{false,true})
        {
            string key="BD2.Test.Broker."+Guid.NewGuid().ToString("N");
            var tree=Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source.Replace("BD2.LocalIpc.Broker.v1",key));
            if(!continuous)
            {
                var root=tree.GetRoot();
                var statement=root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TryStatementSyntax>()
                    .Single(t=>t.Finally?.ToString().Contains("listening = Create(endpoint, false)")==true);
                tree=(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree)tree.WithRootAndOptions(Microsoft.CodeAnalysis.SyntaxNodeExtensions.ReplaceNode(root,statement,statement.WithFinally(null)),tree.Options);
            }
            var compilation=Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("BrokerFixture"+Guid.NewGuid().ToString("N"),
                new[]{tree,Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(wire),Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(trace)},references,
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
            using var buffer=new MemoryStream();var emitted=compilation.Emit(buffer);
            if(!emitted.Success)throw new Exception(string.Join("\n",emitted.Diagnostics.Where(d=>d.Severity==Microsoft.CodeAnalysis.DiagnosticSeverity.Error)));
            var assembly=System.Reflection.Assembly.Load(buffer.ToArray());var broker=assembly.GetType("BD2.LocalIpc.PipeBroker")!;
            string path=Path.Combine(Path.GetTempPath(),key);int missing=0;
            try
            {
                var channel=broker.GetMethod("Register")!.Invoke(null,new object[]{path,"fixture","snapshot.json","",true})!;
                channel.GetType().GetMethod("Write")!.Invoke(channel,new object[]{"snapshot.json",new byte[]{42}});
                var client=new BD2.LocalIpc.PipeClient(path,game.ProcessId,game.StartTicks);
                for(int i=0;i<24;i++)
                {
                    if(!(await Task.Run(()=>client.Read("snapshot.json")))!.SequenceEqual(new byte[]{42}))throw new Exception("broker reply corrupted");
                    await Task.Delay(5);
                    if(!DailyTransport.EndpointReady(game))missing++;
                }
            }
            finally{(AppDomain.CurrentDomain.GetData(key+".stop") as Action)?.Invoke();}
            if(continuous ? missing!=0 : missing==0)throw new Exception("broker endpoint continuity: repaired="+continuous+", missing="+missing);
            cases.Add("connection: "+(continuous?"repaired broker keeps the endpoint present across all 24 preempted accept cycles":"old broker reproduces false missing endpoint under acceptor preemption"));
        }
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


